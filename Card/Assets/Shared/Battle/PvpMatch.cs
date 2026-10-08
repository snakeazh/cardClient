using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    public sealed class PvpMatch
    {
        public const string PhaseFight = PvpPhases.Fight;
        public const string PhaseSettle = PvpPhases.Settle;
        public const string PhaseShop = PvpPhases.Shop;
        public const string PhaseFinished = PvpPhases.Finished;

        private readonly IGameTables _tables;
        private readonly PvpModeConfig _mode;
        private readonly IReadOnlyList<PvpRoundConfig> _rounds;
        private readonly PvpFighter[] _fighters;
        private readonly List<PvpDuelTable> _duels = new List<PvpDuelTable>();
        private readonly List<PvpMatchEventDto> _pendingEvents = new List<PvpMatchEventDto>();
        private readonly object _gate = new object();
        private readonly Random _shopRandom;
        private readonly PvpRelicStock _relicStock;
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
            _relicStock = PvpRelicStock.Create(tables);
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

        /// <summary>未派发的增量事件；投影只读，清空走 DrainEvents。</summary>
        internal IReadOnlyList<PvpMatchEventDto> PendingEvents => _pendingEvents;

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
                    // 初始金币 = 模式基础 + 英雄 InitialFunds（对齐 PVE StartNewRun；PVP 无天赋侧）。
                    Gold = mode.InitialGold + PvpHeroRuntime.InitialGold(tables, combat.HeroId),
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
            Act(userId, PvpActions.Showdown, 0, Array.Empty<int>());
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
                Bump(disconnected ? PvpEventKinds.PlayerOffline : PvpEventKinds.PlayerOnline, userId, 0);
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

        /// <summary>主动放弃本局：判负淘汰拿当前最差名次，座位按断线同款兜底不再被等待。返回 true = 有状态变化需广播。</summary>
        public bool Abandon(string userId)
        {
            lock (_gate)
            {
                if (Phase == PhaseFinished)
                {
                    return false;
                }

                var index = SeatOf(userId);
                if (index < 0)
                {
                    return false;
                }

                var fighter = _fighters[index];
                if (!fighter.Alive || fighter.Rank > 0)
                {
                    return false;
                }

                fighter.Hp = 0;
                fighter.Alive = false;
                fighter.Disconnected = true;
                ReleaseRelics(fighter);
                if (Phase == PhaseShop)
                {
                    fighter.ShopDone = true;
                }

                Bump(PvpEventKinds.PlayerOffline, userId, 0);
                RankDead(new Dictionary<int, int> { [index] = 0 });

                if (Phase == PhaseFight)
                {
                    // 掉线座位当轮自动锁定（超时同款兜底），保证对局不被卡住。
                    var duel = FindDuel(userId);
                    if (duel != null && !duel.Resolved)
                    {
                        var seat = duel.ViewerSeat(userId);
                        if (!duel.IsLocked(seat))
                        {
                            duel.LockSeat(seat);
                        }
                    }

                    SettleResolvedDuels();
                    TryAdvanceRound();
                }
                else if (Phase == PhaseShop && AllShopDone())
                {
                    AdvanceFromShop();
                }

                return true;
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
                // 编辑器测试：任意阶段可塞圣物（不扣金、不占货架上限校验外的重复件限制仍保留）。
                if (string.Equals(action, PvpActions.DebugGrant, StringComparison.OrdinalIgnoreCase))
                {
                    var grantee = FighterOf(userId);
                    DebugGrantRelic(grantee, index);
                    Touch();
                    return;
                }

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
                    case PvpActions.Pick:
                        duel.Pick(seat, indexes ?? Array.Empty<int>());
                        break;
                    case PvpActions.Rub:
                        if (fighter.RubLeft <= 0)
                        {
                            throw new InvalidOperationException("No rub left.");
                        }

                        duel.Rub(seat, index);
                        fighter.RubLeft--;
                        break;
                    case PvpActions.Replace:
                        if (fighter.ReplaceLeft <= 0)
                        {
                            throw new InvalidOperationException("No replace left.");
                        }

                        duel.Replace(seat);
                        fighter.ReplaceLeft--;
                        break;
                    case PvpActions.Peek:
                        if (fighter.PeekLeft <= 0)
                        {
                            throw new InvalidOperationException("No peek left.");
                        }

                        duel.Peek(seat);
                        fighter.PeekLeft--;
                        if (PvpRelicRuntime.HasMechanism(_tables, fighter, MechanismType.PeekSteal))
                        {
                            duel.TryPeekSteal(seat, _shopRandom);
                        }

                        break;
                    case PvpActions.Showdown:
                    case PvpActions.Open:
                        duel.LockSeat(seat);
                        SettleResolvedDuels();
                        break;
                    case PvpActions.Use:
                        UseRelic(fighter, index);
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
                return PvpMatchViewProjector.Project(this, _tables, userId);
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
            for (var f = 0; f < _fighters.Length; f++)
            {
                if (_fighters[f].Alive)
                {
                    // 凤凰羽毛标记跨回合作废；回合开始治疗/上限成长（绷带/永恒之心/保温杯）。
                    _fighters[f].NullifyDamageNextHit = false;
                    PvpRelicRuntime.RefreshCopiedRelic(_tables, _fighters[f], _shopRandom);
                    PvpRelicRuntime.ApplyRoundStart(_tables, _fighters[f]);
                }
            }

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
                    PvpRelicRuntime.ApplyTrackersToSeat(_tables, fighter, setup);
                };
                _duels.Add(duel);
            }

            Phase = PhaseFight;
            PhaseDeadlineUtcMs = _mode.OpenPhaseSeconds > 0
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PvpTiming.DealAnimMs + _mode.OpenPhaseSeconds * 1000L
                : 0;
            Bump(PvpEventKinds.RoundStart, string.Empty, Round);
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
                ConsumeNullify(duel);
                ApplyShowdownTrackers(duel, snap, winner: null);
                duel.MarkHpApplied(-1, 0);
                BumpDuelResolved(duel, 0);
                return;
            }

            var win = snap.Winners[0];
            if (win != 0 && win != 1)
            {
                ConsumeNullify(duel);
                ApplyShowdownTrackers(duel, snap, winner: null);
                duel.MarkHpApplied(-1, 0);
                BumpDuelResolved(duel, 0);
                return;
            }

            // 第六感：败方持有时有概率反转胜负（伤害仍按新胜方座位的 Damages）。
            win = MaybeReverseWinner(duel, snap, win);

            var damage = PvpSettlementRules.ScaleDamage(snap.Damages[win], _mode.DamageRoundScale, Round);

            // 凤凰羽毛：败方激活则本次比牌伤害归零；标记随本桌结算消费（未挡到也算用掉）。
            var victim = FighterOfDuelSeat(duel, 1 - win);
            var nullified = victim != null && victim.NullifyDamageNextHit;
            if (nullified)
            {
                damage = 0;
            }

            ConsumeNullify(duel);

            var deathHp = new Dictionary<int, int>();
            if (damage > 0 && victim != null)
            {
                var victimHeroId = victim.Combat.HeroId;
                // 英雄闪避（MissDamagePer）：掷中本次伤害为 0，并按 MissGetDamage 反击攻击方。
                if (PvpHeroRuntime.RollDodge(_tables, victimHeroId, _shopRandom))
                {
                    var counter = PvpHeroRuntime.DodgeCounterDamage(_tables, victimHeroId, Math.Max(0, victim.Combat.Attack));
                    if (counter > 0)
                    {
                        ApplyToSeat(duel, win, counter, deathHp);
                    }

                    damage = 0;
                }
                else
                {
                    // 英雄承伤减免（HeroTakeDamagePer）：对齐 PVE IncomingDamageAfterMitigation 的乘区与保底。
                    damage = PvpHeroRuntime.MitigateIncomingDamage(_tables, victimHeroId, damage);
                }
            }

            // 英雄吸血（BloodSucking）：胜方按实际造成伤害回血，夹到血量上限（PVE 只有圣物侧，英雄侧此处补上）。
            var winnerFighter = FighterOfDuelSeat(duel, win);
            if (damage > 0 && winnerFighter != null && winnerFighter.Alive)
            {
                var dealt = victim != null ? Math.Min(victim.Hp, damage) : damage;
                var heal = PvpHeroRuntime.BloodSuckingHeal(_tables, winnerFighter.Combat.HeroId, dealt);
                if (heal > 0)
                {
                    winnerFighter.Hp = Math.Min(winnerFighter.MaxHp, winnerFighter.Hp + heal);
                }
            }

            ApplyToSeat(duel, 1 - win, damage, deathHp);
            duel.MarkHpApplied(win, damage);
            ApplyDuelGold(duel, win);
            ApplyMonsterWinHeal(duel, win);
            ApplyShowdownTrackers(duel, snap, win);
            RankDead(deathHp);
            BumpDuelResolved(duel, damage);
        }

        private int MaybeReverseWinner(PvpDuelTable duel, BattleSnapshot snap, int win)
        {
            var loser = 1 - win;
            var loserFighter = FighterOfDuelSeat(duel, loser);
            if (loserFighter == null)
            {
                return win;
            }

            if (!PvpRelicRuntime.RollReverse(_tables, loserFighter, _shopRandom))
            {
                return win;
            }

            // 反转后用原败方伤害；若该座位伤害为 0 则至少按对方伤害对调语义仍成立。
            return loser;
        }

        private void ApplyShowdownTrackers(PvpDuelTable duel, BattleSnapshot snap, int? winner)
        {
            for (var seat = 0; seat <= 1; seat++)
            {
                var fighter = FighterOfDuelSeat(duel, seat);
                if (fighter == null)
                {
                    continue;
                }

                var type = seat < snap.Scores.Length ? snap.Scores[seat].Type : HandType.HighCard;
                bool? won = null;
                if (winner.HasValue)
                {
                    won = seat == winner.Value;
                }

                PvpRelicRuntime.AfterShowdown(_tables, fighter, type, won, _shopRandom);
                if (won == true)
                {
                    // 月光酒回血 / 激励徽章上限成长（比牌获胜触发）。
                    PvpRelicRuntime.ApplyWinRewards(_tables, fighter);
                }

                PvpRelicRuntime.ApplySelfDestroy(_tables, fighter, _shopRandom, _relicStock.Return);
            }
        }

        /// <summary>清掉本桌双方的凤凰羽毛标记：该道具保护"这一桌这一次比牌"，结算即消费。</summary>
        private void ConsumeNullify(PvpDuelTable duel)
        {
            for (var seat = 0; seat <= 1; seat++)
            {
                var fighter = FighterOfDuelSeat(duel, seat);
                if (fighter != null)
                {
                    fighter.NullifyDamageNextHit = false;
                }
            }
        }

        private PvpFighter? FighterOfDuelSeat(PvpDuelTable duel, int duelSeat)
        {
            if (duelSeat == 0)
            {
                return FighterAt(duel.LeftUserId);
            }

            if (duel.VsMonster)
            {
                return null;
            }

            return FighterAt(duel.RightUserId);
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

            winner.Hp = Math.Min(winner.MaxHp, winner.Hp + PvpSettlementRules.MonsterWinHeal(winner.MaxHp));
        }

        /// <summary>
        /// 胜 = 本轮基础（英雄 GetGoldAfterLevel 只放大这一项）+ 未用技能×单价 + 连胜 + 利息。
        /// 负 =（本人基础 + 本人未用技能×单价）/ 2 + 连败 + 利息。平局不结算。
        /// 野怪轮照发基础/技能/利息，但不改连胜连败、不加连胜连败奖励。
        /// </summary>
        private void ApplyDuelGold(PvpDuelTable duel, int win)
        {
            var unit = PvpSettlementRules.SkillGoldUnit(_tables.GameConst);
            var countStreak = !duel.VsMonster;

            var winner = win == 0 ? FighterAt(duel.LeftUserId) : duel.VsMonster ? null : FighterAt(duel.RightUserId);
            if (winner != null)
            {
                PayRoundGold(winner, unit, countStreak, won: true);
            }

            var loser = win == 1 ? FighterAt(duel.LeftUserId) : duel.VsMonster ? null : FighterAt(duel.RightUserId);
            if (loser != null)
            {
                PayRoundGold(loser, unit, countStreak, won: false);
            }
        }

        private void PayRoundGold(PvpFighter fighter, int skillGoldUnit, bool countStreak, bool won)
        {
            if (countStreak)
            {
                if (won)
                {
                    fighter.WinStreak++;
                    fighter.LoseStreak = 0;
                }
                else
                {
                    fighter.LoseStreak++;
                    fighter.WinStreak = 0;
                }
            }

            var streak = countStreak ? (won ? fighter.WinStreak : fighter.LoseStreak) : 0;
            var unused = fighter.RubLeft + fighter.ReplaceLeft + fighter.PeekLeft;
            var baseGold = PvpHeroRuntime.SettlementGold(_tables, fighter.Combat.HeroId, _row.GoldBase);
            fighter.Gold += PvpSettlementRules.RoundGold(baseGold, skillGoldUnit, unused, streak, fighter.Gold, won);
        }

        private void BumpDuelResolved(PvpDuelTable duel, int damage)
        {
            Bump(PvpEventKinds.DuelResolved, duel.LeftUserId, damage);
            if (!duel.VsMonster)
            {
                Bump(PvpEventKinds.DuelResolved, duel.RightUserId, damage);
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
            Bump(PvpEventKinds.SettleStart, string.Empty, 0);
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
                _relicStock.ReturnShelf(fighter);
                if (!fighter.ShopDone)
                {
                    PvpShopRules.FillOffers(fighter, _tables, _shopRandom, _relicStock);
                }
            }

            Bump(PvpEventKinds.ShopStart, string.Empty, 0);
            if (AllShopDone())
            {
                AdvanceFromShop();
            }
        }

        private void AdvanceFromShop()
        {
            for (var i = 0; i < _fighters.Length; i++)
            {
                _relicStock.ReturnShelf(_fighters[i]);
            }

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
                case PvpActions.Buy:
                    BuyRelic(fighter, index);
                    break;
                case PvpActions.Sell:
                    SellRelic(fighter, index);
                    break;
                case PvpActions.Refresh:
                    RefreshShop(fighter);
                    break;
                case PvpActions.UnlockSlot:
                    UnlockRelicSlot(fighter);
                    break;
                case PvpActions.ShopDone:
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

        /// <summary>使用消耗型圣物（UseType 1/2）：结算即时效果后移出持有（UseConsumable 计数 +1）。
        /// 含 PVP 未支持消耗类型的圣物拒绝使用（商店也不会上架）。</summary>
        private void UseRelic(PvpFighter fighter, int relicId)
        {
            if (!_tables.TryGetRelic(relicId, out var relic) || !fighter.OwnedRelicIds.Contains(relicId))
            {
                throw new InvalidOperationException("Relic not owned.");
            }

            if (relic.UseType != 1 && relic.UseType != 2)
            {
                throw new InvalidOperationException("Relic is not consumable.");
            }

            if (!PvpRelicRuntime.TryApplyConsumable(_tables, fighter, relic))
            {
                throw new InvalidOperationException("Relic has no pvp effect.");
            }

            var spent = PvpRelicBag.RemoveOne(fighter, relicId);
            for (var i = 1; i < spent; i++)
            {
                PvpRelicRuntime.TryApplyConsumable(_tables, fighter, relic);
            }

            fighter.ConsumableUsesThisRun++;
            PvpRelicRuntime.CleanupTrackers(fighter);
        }

        /// <summary>编辑器测试加圣物：写入 OwnedRelicIds，下一手比牌 / BeforeCompare 生效。</summary>
        private void DebugGrantRelic(PvpFighter fighter, int relicId)
        {
            if (relicId <= 0 || !_tables.TryGetRelic(relicId, out _))
            {
                throw new InvalidOperationException("Unknown relic.");
            }

            if (fighter.OwnedRelicIds.Contains(relicId))
            {
                throw new InvalidOperationException("Relic already owned.");
            }

            PvpRelicBag.Add(fighter, relicId);
            // 血量上限类与购买同口径（编辑器测试能直接看到血条变化）。
            PvpRelicRuntime.ApplyAcquireStats(_tables, fighter, relicId);
            // 技能次数类词条：立刻补进当前剩余次数，方便当手测试。
            var combat = fighter.Combat;
            fighter.RubLeft += CombatBonuses.SumSkillCountBonus(
                _tables, new[] { relicId }, combat.HeroId, Array.Empty<CombatTalentCount>(), MechanismType.RubbingCardsNum);
            fighter.PeekLeft += CombatBonuses.SumSkillCountBonus(
                _tables, new[] { relicId }, combat.HeroId, Array.Empty<CombatTalentCount>(), MechanismType.PerspectiveNum);
        }

        private void BuyRelic(PvpFighter fighter, int relicId)
        {
            if (!fighter.ShopOfferIds.Contains(relicId) || !_tables.TryGetRelic(relicId, out var relic))
            {
                throw new InvalidOperationException("Relic is not on sale.");
            }

            if (fighter.OwnedRelicIds.Count >= fighter.RelicSlots)
            {
                throw new InvalidOperationException("Relic bag is full.");
            }

            var price = PvpShopRules.BuyPrice(_tables, fighter.Combat.HeroId, relic);
            if (fighter.Gold < price)
            {
                throw new InvalidOperationException("Not enough gold.");
            }

            fighter.Gold -= price;
            fighter.ShopOfferIds.Remove(relicId);
            PvpRelicBag.Add(fighter, relicId);
            // 血量上限类（小精灵/奢华沙发）：购买即上限+X 且当前血同步+X。
            PvpRelicRuntime.ApplyAcquireStats(_tables, fighter, relicId);
            // 懒初始化甜品/饮品满值：下次 ToPlayerSeat / Tick 时写入。
        }

        private void SellRelic(PvpFighter fighter, int relicId)
        {
            if (!fighter.OwnedRelicIds.Contains(relicId) || !_tables.TryGetRelic(relicId, out var relic))
            {
                throw new InvalidOperationException("Relic not owned.");
            }

            var copies = PvpRelicBag.RemoveOne(fighter, relicId);
            if (copies <= 0)
            {
                throw new InvalidOperationException("Relic not owned.");
            }

            fighter.Gold += PvpShopRules.SellPrice(relic) * copies;
            _relicStock.Return(relicId, copies);
            for (var i = 1; i < copies; i++)
            {
                PvpRelicRuntime.ApplySellStats(_tables, fighter, relicId);
            }
            // 血量上限类反向回收（当前血夹到新上限，不低于 1）。
            PvpRelicRuntime.ApplySellStats(_tables, fighter, relicId);
            PvpRelicRuntime.CleanupTrackers(fighter);
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

            PvpRelicRuntime.OnShopRefreshed(_tables, fighter);
            PvpShopRules.RerollOffers(fighter, _tables, _shopRandom, _relicStock);
        }

        private void UnlockRelicSlot(PvpFighter fighter)
        {
            if (fighter.RelicSlots >= PvpRelicBag.MaxSlots)
            {
                throw new InvalidOperationException("Relic slots are maxed.");
            }

            var cost = PvpRelicBag.NextSlotCost(fighter.RelicSlots);
            if (fighter.Gold < cost)
            {
                throw new InvalidOperationException("Not enough gold.");
            }

            fighter.Gold -= cost;
            fighter.RelicSlots++;
        }

        private void ReleaseRelics(PvpFighter fighter)
        {
            _relicStock.ReturnShelf(fighter);
            _relicStock.ReturnCarried(fighter);
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
                ReleaseRelics(fighter);
            }
        }

        private void RankDead(Dictionary<int, int> deathHp)
        {
            if (deathHp.Count == 0)
            {
                return;
            }

            var aliveAfter = 0;
            for (var i = 0; i < _fighters.Length; i++)
            {
                if (_fighters[i].Alive)
                {
                    aliveAfter++;
                }
            }

            foreach (var pair in PvpSettlementRules.RankDeadOrder(deathHp, aliveAfter))
            {
                var fighter = _fighters[pair.Key];
                fighter.Rank = pair.Value;
                if (fighter.Hp < 0)
                {
                    fighter.Hp = 0;
                }

                Bump(PvpEventKinds.PlayerEliminated, fighter.UserId, fighter.Rank);
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
            Bump(PvpEventKinds.MatchFinished, string.Empty, 0);
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
            Bump(PvpEventKinds.MatchFinished, string.Empty, 0);
        }

        private void FillRankRewards()
        {
            for (var i = 0; i < _fighters.Length; i++)
            {
                _fighters[i].RewardGold = PvpSettlementRules.RewardForRank(_fighters[i].Rank, _mode.RankReward);
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
                var carried = PvpRelicBag.Expand(fighter);
                fighter.RubLeft = Math.Max(0, rub + CombatBonuses.SumSkillCountBonus(
                    _tables, carried, combat.HeroId, combat.Talents, MechanismType.RubbingCardsNum));
                fighter.PeekLeft = Math.Max(0, peek + CombatBonuses.SumSkillCountBonus(
                    _tables, carried, combat.HeroId, combat.Talents, MechanismType.PerspectiveNum));
                fighter.ReplaceLeft = replace;
            }
        }

        private SeatSetup ToPlayerSeat(PvpFighter fighter, int seatId)
        {
            var combat = fighter.Combat;
            var setup = new SeatSetup
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
                RelicIds = CombatBonuses.CloneRelicIds(fighter.OwnedRelicIds),
                RubLeft = fighter.RubLeft,
                PeekLeft = fighter.PeekLeft,
                ReplaceLeft = fighter.ReplaceLeft
            };
            PvpRelicRuntime.ApplyTrackersToSeat(_tables, fighter, setup);
            return setup;
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
