using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    public sealed class PvpFighter
    {
        public int SeatIndex { get; init; }

        public string UserId { get; init; } = string.Empty;

        public string NickName { get; init; } = string.Empty;

        public SeatSetup Combat { get; init; } = new SeatSetup();

        public int Hp { get; set; }

        public int MaxHp { get; set; }

        public int Gold { get; set; }

        public bool Alive { get; set; } = true;

        public int Rank { get; set; }

        public int RubLeft { get; set; }

        public int ReplaceLeft { get; set; }

        public int PeekLeft { get; set; }

        public bool IsBot { get; init; }

        public bool Disconnected { get; set; }

        public int RewardGold { get; set; }

        public List<int> OwnedRelicIds { get; } = new List<int>();

        public int[] ShopPoolIds { get; set; } = Array.Empty<int>();

        public List<int> ShopOfferIds { get; } = new List<int>();

        public int ShopRefreshCount { get; set; }

        public int FreeShopRefreshLeft { get; set; }

        public bool ShopDone { get; set; }
    }

    public sealed class PvpMatch
    {
        public const string PhaseFight = "fight";
        public const string PhaseSettle = "settle";
        public const string PhaseShop = "shop";
        public const string PhaseFinished = "finished";

        private readonly IGameTables _tables;
        private readonly PvpModeConfig _mode;
        private readonly IReadOnlyList<PvpRoundConfig> _rounds;
        private readonly PvpFighter[] _fighters;
        private readonly List<PvpDuelTable> _duels = new List<PvpDuelTable>();
        private readonly List<PvpMatchEventDto> _pendingEvents = new List<PvpMatchEventDto>();
        private readonly object _gate = new object();
        private readonly Random _shopRandom;
        private int _pvpCycle;
        private bool _rewardsGranted;
        private PvpRoundConfig _row = null!;
        private PvpFightKind _kind;

        private PvpMatch(
            Guid roomId,
            int seed,
            IReadOnlyList<PlayerPublic> players,
            IGameTables tables,
            PvpModeConfig mode,
            IReadOnlyList<PvpRoundConfig> rounds,
            PvpFighter[] fighters)
        {
            RoomId = roomId;
            Seed = seed;
            Players = players;
            _tables = tables;
            _mode = mode;
            _rounds = rounds;
            _fighters = fighters;
            _shopRandom = new Random(unchecked(seed ^ 0x5A0F9));
            ModeId = mode.Id;
            Round = 1;
            Phase = PhaseFight;
        }

        public Guid RoomId { get; }

        public int Seed { get; }

        public int ModeId { get; }

        public string ModeName => _mode.Name ?? string.Empty;

        public int Round { get; private set; }

        public string Phase { get; private set; }

        /// <summary>当前阶段截止时刻（UTC 毫秒）。0 = 无倒计时。</summary>
        public long PhaseDeadlineUtcMs { get; private set; }

        /// <summary>状态版本号：每次状态变更单调递增。</summary>
        public long StateVersion { get; private set; }

        /// <summary>进入 finished 的时刻（UTC 毫秒）。0 = 未结束，宿主据此回收房间。</summary>
        public long FinishedUtcMs { get; private set; }

        public PvpFightKind FightKind => _kind;

        public IReadOnlyList<PlayerPublic> Players { get; }

        public IReadOnlyList<PvpFighter> Fighters => _fighters;

        public IReadOnlyList<PvpDuelTable> Duels => _duels;

        public static PvpMatch Open(
            Guid roomId,
            int seed,
            IReadOnlyList<PlayerPublic> players,
            IGameTables tables,
            IReadOnlyList<SeatSetup> combatSeats,
            int modeId = PvpSchedule.DefaultModeId)
        {
            if (!PvpSchedule.TryLoad(tables, modeId, out var mode, out var rounds))
            {
                throw new InvalidOperationException($"PvpModeConfig {modeId} not found.");
            }

            var fighters = new PvpFighter[players.Count];
            for (var i = 0; i < players.Count; i++)
            {
                var pub = players[i];
                var combat = i < combatSeats.Count ? combatSeats[i] : new SeatSetup
                {
                    SeatId = i,
                    UserId = pub.UserId,
                    NickName = pub.NickName,
                    IsHuman = !pub.IsBot,
                    Alive = true,
                    Hp = 1,
                    MaxHp = 1,
                    Attack = 1
                };
                var hp = combat.MaxHp > 0 ? combat.MaxHp : Math.Max(1, combat.Hp);
                fighters[i] = new PvpFighter
                {
                    SeatIndex = i,
                    UserId = pub.UserId,
                    NickName = pub.NickName,
                    Combat = combat,
                    Hp = hp,
                    MaxHp = hp,
                    Gold = mode.InitialGold,
                    Alive = true,
                    IsBot = pub.IsBot,
                    ShopPoolIds = combat.ShopPoolIds ?? Array.Empty<int>()
                };
            }

            var match = new PvpMatch(roomId, seed, players, tables, mode, rounds, fighters);
            match.StartRound();
            return match;
        }

        public int SeatOf(string userId)
        {
            for (var i = 0; i < _fighters.Length; i++)
            {
                if (PvpBattleTable.SameUser(_fighters[i].UserId, userId))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Showdown(string userId)
        {
            Act(userId, "showdown", 0, Array.Empty<int>());
            AdvanceIfReady();
        }

        public void AdvanceIfReady()
        {
            lock (_gate)
            {
                TryAdvanceRound();
            }
        }

        /// <summary>阶段到点推进：fight 自动锁定结算、settle 进商店/结束、shop 进下一轮。返回是否有状态变化。</summary>
        public bool ApplyTimeouts(long nowUtcMs)
        {
            lock (_gate)
            {
                if (PhaseDeadlineUtcMs <= 0 || nowUtcMs < PhaseDeadlineUtcMs)
                {
                    return false;
                }

                if (Phase == PhaseSettle)
                {
                    AdvanceFromSettle();
                    return true;
                }

                if (Phase == PhaseShop)
                {
                    AdvanceFromShop();
                    return true;
                }

                if (Phase != PhaseFight)
                {
                    return false;
                }

                var changed = false;
                for (var i = 0; i < _duels.Count; i++)
                {
                    var duel = _duels[i];
                    if (duel.Resolved)
                    {
                        continue;
                    }

                    for (var seat = 0; seat <= 1; seat++)
                    {
                        if (!duel.IsLocked(seat))
                        {
                            duel.LockSeat(seat);
                            changed = true;
                        }
                    }
                }

                if (!changed)
                {
                    return false;
                }

                Touch();
                SettleResolvedDuels();
                TryAdvanceRound();
                return true;
            }
        }

        /// <summary>取走自上次以来的增量事件（广播后清空，只 drain 一次）。</summary>
        public List<PvpMatchEventDto> DrainEvents()
        {
            lock (_gate)
            {
                if (_pendingEvents.Count == 0)
                {
                    return new List<PvpMatchEventDto>();
                }

                var events = new List<PvpMatchEventDto>(_pendingEvents);
                _pendingEvents.Clear();
                return events;
            }
        }

        public void SetDisconnected(string userId, bool disconnected)
        {
            lock (_gate)
            {
                var index = SeatOf(userId);
                if (index < 0)
                {
                    return;
                }

                var fighter = _fighters[index];
                if (fighter.Disconnected == disconnected)
                {
                    return;
                }

                fighter.Disconnected = disconnected;
                Bump(disconnected ? "player_offline" : "player_online", userId, 0);
                if (!disconnected || Phase != PhaseFight)
                {
                    return;
                }

                // 掉线座位当轮自动锁定（超时同款兜底），保证对局不被卡住。
                var duel = FindDuel(userId);
                if (duel == null || duel.Resolved)
                {
                    return;
                }

                var seat = duel.ViewerSeat(userId);
                if (!duel.IsLocked(seat))
                {
                    duel.LockSeat(seat);
                }

                SettleResolvedDuels();
                TryAdvanceRound();
            }
        }

        /// <summary>名次奖励一次性标志：仅 finished 后首次调用返回 true。</summary>
        public bool TryMarkRewardsGranted()
        {
            lock (_gate)
            {
                if (Phase != PhaseFinished || _rewardsGranted)
                {
                    return false;
                }

                _rewardsGranted = true;
                return true;
            }
        }

        public void Act(string userId, string action, int index, int[] indexes)
        {
            lock (_gate)
            {
                if (Phase == PhaseShop)
                {
                    ActShop(userId, action, index);
                    return;
                }

                if (Phase != PhaseFight)
                {
                    throw new InvalidOperationException("Match is not in a fight.");
                }

                var duel = FindDuel(userId);
                if (duel == null)
                {
                    throw new InvalidOperationException("Not in a duel.");
                }

                var fighter = FighterOf(userId);
                var seat = duel.ViewerSeat(userId);
                switch (action)
                {
                    case "pick":
                        duel.Pick(seat, indexes ?? Array.Empty<int>());
                        break;
                    case "rub":
                        if (fighter.RubLeft <= 0)
                        {
                            throw new InvalidOperationException("No rub left.");
                        }

                        duel.Rub(seat, index);
                        fighter.RubLeft--;
                        break;
                    case "replace":
                        if (fighter.ReplaceLeft <= 0)
                        {
                            throw new InvalidOperationException("No replace left.");
                        }

                        duel.Replace(seat);
                        fighter.ReplaceLeft--;
                        break;
                    case "peek":
                        if (fighter.PeekLeft <= 0)
                        {
                            throw new InvalidOperationException("No peek left.");
                        }

                        duel.Peek(seat);
                        fighter.PeekLeft--;
                        break;
                    case "showdown":
                    case "open":
                        duel.LockSeat(seat);
                        SettleResolvedDuels();
                        break;
                    default:
                        throw new InvalidOperationException("Unknown battle action.");
                }

                Touch();
            }
        }

        public PvpMatchStateDto ViewFor(string userId)
        {
            lock (_gate)
            {
                PvpDuelTable? own = null;
                var summaries = new PvpDuelSummaryDto[_duels.Count];
                for (var i = 0; i < _duels.Count; i++)
                {
                    var duel = _duels[i];
                    summaries[i] = new PvpDuelSummaryDto
                    {
                        LeftUserId = duel.LeftUserId,
                        RightUserId = duel.RightUserId,
                        MonsterName = duel.MonsterName,
                        Resolved = duel.Resolved
                    };
                    if (duel.Involves(userId))
                    {
                        own = duel;
                    }
                }

                var roster = new PvpFighterDto[_fighters.Length];
                for (var i = 0; i < _fighters.Length; i++)
                {
                    var f = _fighters[i];
                    roster[i] = new PvpFighterDto
                    {
                        UserId = f.UserId,
                        NickName = f.NickName,
                        Hp = Math.Max(0, f.Hp),
                        MaxHp = f.MaxHp,
                        HeroId = f.Combat.HeroId,
                        Attack = f.Combat.Attack,
                        Gold = f.Gold,
                        Alive = f.Alive,
                        Rank = f.Rank,
                        RubLeft = f.RubLeft,
                        ReplaceLeft = f.ReplaceLeft,
                        PeekLeft = f.PeekLeft,
                        IsBot = f.IsBot,
                        Disconnected = f.Disconnected,
                        RewardGold = f.RewardGold,
                        RelicIds = f.OwnedRelicIds.ToArray()
                    };
                }

                return new PvpMatchStateDto
                {
                    RoomId = RoomId.ToString("N"),
                    Seed = Seed,
                    ModeId = ModeId,
                    ModeName = ModeName,
                    Round = Round,
                    Phase = Phase,
                    FightKind = _kind == PvpFightKind.Monster ? "monster" : "pvp",
                    Players = roster,
                    Duels = summaries,
                    Duel = own == null ? null : own.ViewFor(userId, RoomId.ToString("N")),
                    DuelDamage = own == null || !own.HpApplied ? 0 : own.AppliedDamage,
                    PhaseDeadlineUtcMs = PhaseDeadlineUtcMs,
                    ServerNowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StateVersion = StateVersion,
                    Events = _pendingEvents.Count == 0
                        ? Array.Empty<PvpMatchEventDto>()
                        : _pendingEvents.ToArray(),
                    Shop = ShopViewFor(userId)
                };
            }
        }

        private void StartRound()
        {
            _duels.Clear();
            if (!PvpSchedule.TryGetRound(_rounds, Round, out _row))
            {
                FinishByHp();
                return;
            }

            var alive = AliveSeats();
            if (alive.Count <= 1)
            {
                FinishSurvivor();
                return;
            }

            _kind = PvpSchedule.EffectiveKind(_mode, _row, alive.Count);
            ResetSkills();
            IReadOnlyList<PvpPairSlot> slots;
            if (_kind == PvpFightKind.Monster)
            {
                slots = PvpPairing.ForMonster(alive);
            }
            else
            {
                slots = PvpPairing.ForPvp(alive, _pvpCycle);
                _pvpCycle++;
            }

            var monsterGroup = _row.MonsterGroup;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var left = ToPlayerSeat(_fighters[slot.LeftSeat], 0);
                SeatSetup right;
                var vsPlayer = !slot.IsMonster;
                if (vsPlayer)
                {
                    right = ToPlayerSeat(_fighters[slot.RightSeat!.Value], 1);
                }
                else
                {
                    right = ToMonsterSeat(monsterGroup);
                }

                var duelSeed = unchecked(Seed * 397 ^ Round * 911 ^ i * 17);
                var duel = PvpDuelTable.Open(duelSeed, left, right, _tables, vsPlayer);
                var leftFighter = _fighters[slot.LeftSeat];
                var rightFighter = vsPlayer ? _fighters[slot.RightSeat!.Value] : null;
                duel.BeforeCompare = (seat, setup) =>
                {
                    var fighter = seat == 0 ? leftFighter : rightFighter;
                    if (fighter == null)
                    {
                        return;
                    }

                    setup.RubLeft = fighter.RubLeft;
                    setup.PeekLeft = fighter.PeekLeft;
                    setup.ReplaceLeft = fighter.ReplaceLeft;
                };
                _duels.Add(duel);
            }

            Phase = PhaseFight;
            PhaseDeadlineUtcMs = _mode.OpenPhaseSeconds > 0
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PvpTiming.DealAnimMs + _mode.OpenPhaseSeconds * 1000L
                : 0;
            Bump("round_start", string.Empty, Round);
            SettleResolvedDuels();
            TryAdvanceRound();
        }

        private void SettleResolvedDuels()
        {
            for (var d = 0; d < _duels.Count; d++)
            {
                var duel = _duels[d];
                if (!duel.Resolved || duel.HpApplied)
                {
                    continue;
                }

                ApplyDuelDamage(duel);
            }
        }

        private void ApplyDuelDamage(PvpDuelTable duel)
        {
            var snap = duel.Engine.Snapshot;
            if (snap.Winners == null || snap.Winners.Count != 1)
            {
                duel.MarkHpApplied(-1, 0);
                BumpDuelResolved(duel, 0);
                return;
            }

            var win = snap.Winners[0];
            if (win != 0 && win != 1)
            {
                duel.MarkHpApplied(-1, 0);
                BumpDuelResolved(duel, 0);
                return;
            }

            var raw = snap.Damages[win];
            var damage = (int)Math.Floor(raw * (1f + _mode.DamageRoundScale * Round));
            if (damage < 0)
            {
                damage = 0;
            }

            var deathHp = new Dictionary<int, int>();
            ApplyToSeat(duel, 1 - win, damage, deathHp);
            duel.MarkHpApplied(win, damage);
            ApplyDuelGold(duel, win, damage);
            ApplyMonsterWinHeal(duel, win);
            RankDead(deathHp);
            BumpDuelResolved(duel, damage);
        }

        /// <summary>野怪轮玩家胜：回复 10% 最大生命（至少 1，不超过上限）。败北/平局不回。</summary>
        private void ApplyMonsterWinHeal(PvpDuelTable duel, int win)
        {
            if (!duel.VsMonster || win != 0)
            {
                return;
            }

            var winner = FighterAt(duel.LeftUserId);
            if (winner == null || !winner.Alive)
            {
                return;
            }

            var heal = Math.Max(1, (int)Math.Floor(winner.MaxHp * 0.1));
            winner.Hp = Math.Min(winner.MaxHp, winner.Hp + heal);
        }

        /// <summary>胜 = 本轮 GoldBase + damage/12 + 20×未用技能数；负 = 胜者金币半额；平局不结算。野怪轮玩家胜同样发金。</summary>
        private void ApplyDuelGold(PvpDuelTable duel, int win, int damage)
        {
            var winner = win == 0 ? FighterAt(duel.LeftUserId) : duel.VsMonster ? null : FighterAt(duel.RightUserId);
            var skillGold = _tables.GameConst.EverySkillProvideGold > 0 ? _tables.GameConst.EverySkillProvideGold : 20;
            var winGold = Math.Max(0, _row.GoldBase) + damage / 12;
            if (winner != null)
            {
                winGold += skillGold * (winner.RubLeft + winner.ReplaceLeft + winner.PeekLeft);
                winner.Gold += winGold;
            }

            var loser = win == 1 ? FighterAt(duel.LeftUserId) : duel.VsMonster ? null : FighterAt(duel.RightUserId);
            if (loser != null)
            {
                loser.Gold += winGold / 2;
            }
        }

        private void BumpDuelResolved(PvpDuelTable duel, int damage)
        {
            Bump("duel_resolved", duel.LeftUserId, damage);
            if (!duel.VsMonster)
            {
                Bump("duel_resolved", duel.RightUserId, damage);
            }
        }

        private PvpFighter? FighterAt(string userId)
        {
            var index = SeatOf(userId);
            return index < 0 ? null : _fighters[index];
        }

        private void TryAdvanceRound()
        {
            if (Phase != PhaseFight || !AllResolved())
            {
                return;
            }

            EnterSettle();
        }

        private void EnterSettle()
        {
            Phase = PhaseSettle;
            PhaseDeadlineUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PvpTiming.SettleAnimMs;
            Bump("settle_start", string.Empty, 0);
        }

        private void AdvanceFromSettle()
        {
            var alive = AliveSeats();
            if (alive.Count <= 1)
            {
                FinishSurvivor();
                return;
            }

            if (Round >= PvpSchedule.LastRound(_rounds))
            {
                FinishByHp();
                return;
            }

            if (_mode.ShopSeconds <= 0)
            {
                AdvanceFromShop();
                return;
            }

            EnterShop();
        }

        private void EnterShop()
        {
            Phase = PhaseShop;
            PhaseDeadlineUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _mode.ShopSeconds * 1000L;
            for (var i = 0; i < _fighters.Length; i++)
            {
                var fighter = _fighters[i];
                if (!fighter.Alive)
                {
                    continue;
                }

                fighter.ShopDone = fighter.IsBot || fighter.Disconnected;
                fighter.ShopRefreshCount = 0;
                fighter.FreeShopRefreshLeft = 0;
                fighter.ShopOfferIds.Clear();
                PvpShopRules.FillOffers(fighter, _tables, _shopRandom);
            }

            Bump("shop_start", string.Empty, 0);
            if (AllShopDone())
            {
                AdvanceFromShop();
            }
        }

        private void AdvanceFromShop()
        {
            Round++;
            StartRound();
        }

        private bool AllShopDone()
        {
            for (var i = 0; i < _fighters.Length; i++)
            {
                if (_fighters[i].Alive && !_fighters[i].ShopDone)
                {
                    return false;
                }
            }

            return true;
        }

        private void ActShop(string userId, string action, int index)
        {
            var fighter = FighterOf(userId);
            if (!fighter.Alive || fighter.ShopDone)
            {
                throw new InvalidOperationException("Shop is closed.");
            }

            switch (action)
            {
                case "buy":
                    BuyRelic(fighter, index);
                    break;
                case "sell":
                    SellRelic(fighter, index);
                    break;
                case "refresh":
                    RefreshShop(fighter);
                    break;
                case "shop_done":
                    fighter.ShopDone = true;
                    if (AllShopDone())
                    {
                        AdvanceFromShop();
                    }

                    break;
                default:
                    throw new InvalidOperationException("Unknown shop action.");
            }

            Touch();
        }

        private void BuyRelic(PvpFighter fighter, int relicId)
        {
            if (!fighter.ShopOfferIds.Contains(relicId) || !_tables.TryGetRelic(relicId, out var relic))
            {
                throw new InvalidOperationException("Relic is not on sale.");
            }

            if (fighter.OwnedRelicIds.Contains(relicId))
            {
                throw new InvalidOperationException("Relic already owned.");
            }

            var max = _tables.GameConst.DefaultRelicNumMax > 0 ? _tables.GameConst.DefaultRelicNumMax : 3;
            if (fighter.OwnedRelicIds.Count >= max)
            {
                throw new InvalidOperationException("Relic bag is full.");
            }

            var price = PvpShopRules.BuyPrice(relic);
            if (fighter.Gold < price)
            {
                throw new InvalidOperationException("Not enough gold.");
            }

            fighter.Gold -= price;
            fighter.OwnedRelicIds.Add(relicId);
            fighter.ShopOfferIds.Remove(relicId);
        }

        private void SellRelic(PvpFighter fighter, int relicId)
        {
            if (!fighter.OwnedRelicIds.Contains(relicId) || !_tables.TryGetRelic(relicId, out var relic))
            {
                throw new InvalidOperationException("Relic not owned.");
            }

            fighter.OwnedRelicIds.Remove(relicId);
            fighter.Gold += PvpShopRules.SellPrice(relic);
        }

        private void RefreshShop(PvpFighter fighter)
        {
            if (fighter.FreeShopRefreshLeft > 0)
            {
                fighter.FreeShopRefreshLeft--;
            }
            else
            {
                var cost = PvpShopRules.RefreshCost(fighter, _tables.GameConst);
                if (fighter.Gold < cost)
                {
                    throw new InvalidOperationException("Not enough gold.");
                }

                fighter.Gold -= cost;
                fighter.ShopRefreshCount++;
            }

            PvpShopRules.RerollOffers(fighter, _tables, _shopRandom);
        }

        private void ApplyToSeat(PvpDuelTable duel, int duelSeat, int damage, Dictionary<int, int> deathHp)
        {
            string userId;
            if (duelSeat == 0)
            {
                userId = duel.LeftUserId;
            }
            else if (duel.VsMonster)
            {
                return;
            }
            else
            {
                userId = duel.RightUserId;
            }

            var index = SeatOf(userId);
            if (index < 0)
            {
                return;
            }

            var fighter = _fighters[index];
            if (!fighter.Alive)
            {
                return;
            }

            fighter.Hp -= damage;
            if (fighter.Hp <= 0)
            {
                deathHp[index] = fighter.Hp;
                fighter.Alive = false;
            }
        }

        private void RankDead(Dictionary<int, int> deathHp)
        {
            if (deathHp.Count == 0)
            {
                return;
            }

            var order = new List<int>(deathHp.Keys);
            order.Sort((a, b) =>
            {
                var cmp = Math.Abs(deathHp[a]).CompareTo(Math.Abs(deathHp[b]));
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            var aliveAfter = 0;
            for (var i = 0; i < _fighters.Length; i++)
            {
                if (_fighters[i].Alive)
                {
                    aliveAfter++;
                }
            }

            var rank = aliveAfter + 1;
            for (var i = 0; i < order.Count; i++)
            {
                _fighters[order[i]].Rank = rank++;
                if (_fighters[order[i]].Hp < 0)
                {
                    _fighters[order[i]].Hp = 0;
                }

                Bump("player_eliminated", _fighters[order[i]].UserId, _fighters[order[i]].Rank);
            }
        }

        private void FinishSurvivor()
        {
            Phase = PhaseFinished;
            PhaseDeadlineUtcMs = 0;
            for (var i = 0; i < _fighters.Length; i++)
            {
                if (_fighters[i].Alive && _fighters[i].Rank == 0)
                {
                    _fighters[i].Rank = 1;
                }
            }

            FillRankRewards();
            FinishedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Bump("match_finished", string.Empty, 0);
        }

        private void FinishByHp()
        {
            Phase = PhaseFinished;
            PhaseDeadlineUtcMs = 0;
            var living = new List<PvpFighter>();
            for (var i = 0; i < _fighters.Length; i++)
            {
                if (_fighters[i].Alive && _fighters[i].Rank == 0)
                {
                    living.Add(_fighters[i]);
                }
            }

            living.Sort((a, b) =>
            {
                var cmp = b.Hp.CompareTo(a.Hp);
                return cmp != 0 ? cmp : a.SeatIndex.CompareTo(b.SeatIndex);
            });
            for (var i = 0; i < living.Count; i++)
            {
                living[i].Rank = i + 1;
            }

            FillRankRewards();
            FinishedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Bump("match_finished", string.Empty, 0);
        }

        private void FillRankRewards()
        {
            var rewards = _mode.RankReward ?? Array.Empty<int>();
            for (var i = 0; i < _fighters.Length; i++)
            {
                var rank = _fighters[i].Rank;
                _fighters[i].RewardGold = rank >= 1 && rank <= rewards.Length ? rewards[rank - 1] : 0;
            }
        }

        private void Bump(string kind, string userId, int value)
        {
            StateVersion++;
            _pendingEvents.Add(new PvpMatchEventDto
            {
                Version = StateVersion,
                Kind = kind,
                Round = Round,
                UserId = userId,
                Value = value
            });
        }

        /// <summary>只推进状态版本（不产生事件）：pick/rub/replace/peek/单方锁定/商店操作等
        /// 不触发阶段事件的变更也必须让版本号前进，否则客户端版本过滤会把这些快照当重复丢弃。</summary>
        private void Touch()
        {
            StateVersion++;
        }

        private PvpShopStateDto? ShopViewFor(string userId)
        {
            if (Phase != PhaseShop)
            {
                return null;
            }

            var fighter = FighterAt(userId);
            if (fighter == null || !fighter.Alive)
            {
                return null;
            }

            var offerPrices = new int[fighter.ShopOfferIds.Count];
            for (var i = 0; i < offerPrices.Length; i++)
            {
                offerPrices[i] = _tables.TryGetRelic(fighter.ShopOfferIds[i], out var relic)
                    ? PvpShopRules.BuyPrice(relic)
                    : 0;
            }

            var ownedSellPrices = new int[fighter.OwnedRelicIds.Count];
            for (var i = 0; i < ownedSellPrices.Length; i++)
            {
                ownedSellPrices[i] = _tables.TryGetRelic(fighter.OwnedRelicIds[i], out var relic)
                    ? PvpShopRules.SellPrice(relic)
                    : 0;
            }

            return new PvpShopStateDto
            {
                OfferIds = fighter.ShopOfferIds.ToArray(),
                OfferPrices = offerPrices,
                RefreshCost = PvpShopRules.RefreshCost(fighter, _tables.GameConst),
                FreeRefreshLeft = fighter.FreeShopRefreshLeft,
                OwnedRelicIds = fighter.OwnedRelicIds.ToArray(),
                OwnedSellPrices = ownedSellPrices,
                Done = fighter.ShopDone
            };
        }

        private bool AllResolved()
        {
            if (_duels.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < _duels.Count; i++)
            {
                if (!_duels[i].Resolved)
                {
                    return false;
                }
            }

            return true;
        }

        private List<int> AliveSeats()
        {
            var list = new List<int>(_fighters.Length);
            for (var i = 0; i < _fighters.Length; i++)
            {
                if (_fighters[i].Alive)
                {
                    list.Add(i);
                }
            }

            return list;
        }

        private PvpDuelTable? FindDuel(string userId)
        {
            for (var i = 0; i < _duels.Count; i++)
            {
                if (_duels[i].Involves(userId))
                {
                    return _duels[i];
                }
            }

            return null;
        }

        private PvpFighter FighterOf(string userId)
        {
            var index = SeatOf(userId);
            if (index < 0)
            {
                throw new InvalidOperationException("Not in a duel.");
            }

            return _fighters[index];
        }

        private void ResetSkills()
        {
            var rub = _tables.GameConst.DefaultSkillShuffleNum;
            var replace = _tables.GameConst.DefaultSkillReplaceNum;
            var peek = _tables.GameConst.DefaultSkillPerspectiveNum;
            if (rub <= 0)
            {
                rub = 3;
            }

            if (replace <= 0)
            {
                replace = 1;
            }

            if (peek <= 0)
            {
                peek = 1;
            }

            for (var i = 0; i < _fighters.Length; i++)
            {
                var fighter = _fighters[i];
                if (!fighter.Alive)
                {
                    continue;
                }

                // 词条枚举无换牌次数类型，替换只吃基础值；搓牌/透视叠加持有圣物 + 英雄 + 天赋加成。
                var combat = fighter.Combat;
                fighter.RubLeft = Math.Max(0, rub + CombatBonuses.SumSkillCountBonus(
                    _tables, fighter.OwnedRelicIds, combat.HeroId, combat.Talents, MechanismType.RubbingCardsNum));
                fighter.PeekLeft = Math.Max(0, peek + CombatBonuses.SumSkillCountBonus(
                    _tables, fighter.OwnedRelicIds, combat.HeroId, combat.Talents, MechanismType.PerspectiveNum));
                fighter.ReplaceLeft = replace;
            }
        }

        private static SeatSetup ToPlayerSeat(PvpFighter fighter, int seatId)
        {
            var combat = fighter.Combat;
            return new SeatSetup
            {
                SeatId = seatId,
                UserId = fighter.UserId,
                NickName = fighter.NickName,
                IsHuman = !fighter.IsBot && !fighter.Disconnected,
                Alive = true,
                Attack = combat.Attack,
                Hp = fighter.Hp,
                MaxHp = fighter.MaxHp,
                HeroId = combat.HeroId,
                Talents = CombatBonuses.CloneTalents(combat.Talents),
                RelicIds = CombatBonuses.CloneRelicIds(fighter.OwnedRelicIds)
            };
        }

        private SeatSetup ToMonsterSeat(int groupId)
        {
            if (_tables.TryGetMonsterGroup(groupId, out var group)
                && _tables.TryGetMonster(group.MonsterId, group.MonsterLevel, out var monster))
            {
                return new SeatSetup
                {
                    SeatId = 1,
                    UserId = string.Empty,
                    NickName = string.IsNullOrEmpty(monster.Name) ? "野怪" : monster.Name,
                    IsHuman = false,
                    Alive = true,
                    Attack = Math.Max(1, monster.MonsterDamage),
                    Hp = Math.Max(1, monster.MonsterHp),
                    MaxHp = Math.Max(1, monster.MonsterHp)
                };
            }

            return new SeatSetup
            {
                SeatId = 1,
                NickName = "野怪",
                IsHuman = false,
                Alive = true,
                Attack = 1,
                Hp = 1,
                MaxHp = 1
            };
        }
    }
}
