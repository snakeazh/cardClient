using System;
using System.Collections.Generic;
using CardShare.Contracts;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>一场 1v1 子桌：两座进引擎，其余座位垫成未存活。发 5 张，双方锁定后才比选出的 3 张。</summary>
    public sealed class PvpDuelTable
    {
        private readonly object _gate = new object();
        private readonly PlayerPublic[] _publics;
        private readonly bool[] _peeked = new bool[2];
        private readonly bool[] _locked = new bool[2];

        private PvpDuelTable(BattleEngine engine, PlayerPublic[] publics, bool vsMonster, string monsterName)
        {
            Engine = engine;
            _publics = publics;
            VsMonster = vsMonster;
            MonsterName = monsterName;
        }

        public BattleEngine Engine { get; }

        /// <summary>比牌前回调（座位下标 + 引擎内座位输入）：技能在选牌阶段消耗，剩余次数必须在结算一刻回写，不能开桌时填死。</summary>
        public Action<int, SeatSetup>? BeforeCompare { get; set; }

        public bool VsMonster { get; }

        public string MonsterName { get; }

        public bool Resolved { get; private set; }

        public bool HpApplied { get; private set; }

        /// <summary>结算后实际扣血（含模式轮次系数）。0 = 未结算或平局无伤害。客户端只做状态同步，不重算。</summary>
        public int AppliedDamage { get; private set; }

        /// <summary>结算胜方座位（0/1），-1 = 未结算或平局。</summary>
        public int AppliedWinner { get; private set; } = -1;

        public string LeftUserId => _publics[0].UserId;

        public string RightUserId => _publics[1].UserId;

        public static PvpDuelTable Open(
            int seed,
            SeatSetup left,
            SeatSetup right,
            IGameTables tables,
            bool vsPlayer)
        {
            left.SeatId = 0;
            right.SeatId = 1;
            var mode = vsPlayer ? (BattleMode)new PvpMode() : new PveMode();
            var engine = new BattleEngine(mode, seed, new[] { left, right }, tables);
            engine.Apply(new BattleCommand { Type = BattleCommandType.DealHole });
            var publics = new[]
            {
                new PlayerPublic { UserId = left.UserId, NickName = left.NickName },
                new PlayerPublic { UserId = right.UserId, NickName = right.NickName }
            };
            var table = new PvpDuelTable(engine, publics, !vsPlayer, vsPlayer ? string.Empty : right.NickName);
            table._locked[0] = !left.IsHuman;
            table._locked[1] = !right.IsHuman;
            if (table._locked[0] && table._locked[1])
            {
                table.Compare();
            }

            return table;
        }

        public bool Involves(string userId)
            => PvpBattleTable.SameUser(LeftUserId, userId)
               || (!VsMonster && PvpBattleTable.SameUser(RightUserId, userId));

        public int ViewerSeat(string userId)
        {
            if (PvpBattleTable.SameUser(LeftUserId, userId))
            {
                return 0;
            }

            if (PvpBattleTable.SameUser(RightUserId, userId))
            {
                return 1;
            }

            return 0;
        }

        public bool IsLocked(int seatId) => seatId >= 0 && seatId <= 1 && _locked[seatId];

        public void Pick(int seatId, int[] indexes)
        {
            lock (_gate)
            {
                RequireEditable(seatId);
                Engine.Apply(new BattleCommand { Type = BattleCommandType.Pick, SeatId = seatId, Indexes = indexes });
            }
        }

        public void Rub(int seatId, int index)
        {
            lock (_gate)
            {
                RequireEditable(seatId);
                Engine.Apply(new BattleCommand { Type = BattleCommandType.Rub, SeatId = seatId, Index = index });
            }
        }

        public void Replace(int seatId)
        {
            lock (_gate)
            {
                RequireEditable(seatId);
                Engine.Apply(new BattleCommand { Type = BattleCommandType.Replace, SeatId = seatId });
            }
        }

        public void Peek(int viewerSeat)
        {
            lock (_gate)
            {
                RequireEditable(viewerSeat);
                if (viewerSeat < 0 || viewerSeat > 1)
                {
                    throw new InvalidOperationException("Invalid seat.");
                }

                _peeked[viewerSeat] = true;
            }
        }

        /// <summary>妙手：透视后随机抽走对手 1 张，与己方点数最小的 1 张交换。</summary>
        public bool TryPeekSteal(int viewerSeat, Random rng)
        {
            lock (_gate)
            {
                if (rng == null || viewerSeat < 0 || viewerSeat > 1 || !_peeked[viewerSeat])
                {
                    return false;
                }

                var foe = 1 - viewerSeat;
                var snap = Engine.Snapshot;
                var mine = snap.Hands[viewerSeat];
                var theirs = snap.Hands[foe];
                var foeIndices = new List<int>();
                for (var i = 0; i < theirs.Count; i++)
                {
                    if (theirs[i].IsValid)
                    {
                        foeIndices.Add(i);
                    }
                }

                if (foeIndices.Count == 0)
                {
                    return false;
                }

                var minRank = int.MaxValue;
                var myIndex = -1;
                for (var i = 0; i < mine.Count; i++)
                {
                    if (!mine[i].IsValid)
                    {
                        continue;
                    }

                    var key = (int)mine[i].Rank;
                    if (key < minRank)
                    {
                        minRank = key;
                        myIndex = i;
                    }
                }

                if (myIndex < 0)
                {
                    return false;
                }

                var stealIndex = foeIndices[rng.Next(foeIndices.Count)];
                Engine.SwapHoleCards(viewerSeat, myIndex, foe, stealIndex);
                return true;
            }
        }

        /// <summary>锁定本座当前 3 张。双方都锁定后才比牌。</summary>
        public bool LockSeat(int seatId)
        {
            lock (_gate)
            {
                RequireOpen();
                if (seatId < 0 || seatId > 1)
                {
                    throw new InvalidOperationException("Invalid seat.");
                }

                _locked[seatId] = true;
                if (!_locked[0] || !_locked[1])
                {
                    return false;
                }

                Compare();
                return true;
            }
        }

        public BattleSnapshot Showdown()
        {
            lock (_gate)
            {
                if (Resolved)
                {
                    return Engine.Snapshot;
                }

                _locked[0] = true;
                _locked[1] = true;
                Compare();
                return Engine.Snapshot;
            }
        }

        public void MarkHpApplied(int winner, int damage)
        {
            HpApplied = true;
            AppliedWinner = winner;
            AppliedDamage = damage;
        }

        public BattleStateDto ViewFor(string userId, string roomId)
        {
            lock (_gate)
            {
                var viewer = ViewerSeat(userId);
                return BattleWire.ForDuel(Engine.Snapshot, viewer, roomId, _publics, _peeked[viewer], _locked);
            }
        }

        private void Compare()
        {
            if (Resolved)
            {
                return;
            }

            // 结算一刻回写座位剩余技能次数（选牌阶段的消耗发生在开桌之后；引擎构造时已克隆座位，必须写引擎内副本）。
            var beforeCompare = BeforeCompare;
            if (beforeCompare != null)
            {
                beforeCompare(0, Engine.MutableSeat(0));
                beforeCompare(1, Engine.MutableSeat(1));
            }

            // 亮牌前给没显式选牌的座位补选：机器人/野怪取 5 张里牌型最高的 3 张，
            // 真人（倒计时超时未选）兜底前 3 张。都走 Pick 命令，摊牌后 Selected 即实际比牌。
            var snap = Engine.Snapshot;
            for (var seat = 0; seat <= 1; seat++)
            {
                if (seat >= snap.PickedExplicit.Length || snap.PickedExplicit[seat])
                {
                    continue;
                }

                var pick = snap.Picked[seat];
                if (pick == null || pick.Count != BattleLimits.OpenHandSize)
                {
                    continue;
                }

                var indexes = new int[pick.Count];
                for (var n = 0; n < pick.Count; n++)
                {
                    indexes[n] = pick[n];
                }

                Engine.Apply(new BattleCommand { Type = BattleCommandType.Pick, SeatId = seat, Indexes = indexes });
            }

            Engine.Apply(new BattleCommand { Type = BattleCommandType.Showdown });
            Resolved = true;
        }

        private void RequireEditable(int seatId)
        {
            RequireOpen();
            if (seatId >= 0 && seatId <= 1 && _locked[seatId])
            {
                throw new InvalidOperationException("Hand is locked.");
            }
        }

        private void RequireOpen()
        {
            if (Resolved)
            {
                throw new InvalidOperationException("Duel already resolved.");
            }
        }
    }
}
