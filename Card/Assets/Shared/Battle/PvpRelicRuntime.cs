using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// PVP 局内圣物追踪：与 PVE <c>RelicMechanics</c> 同语义，状态挂在 <see cref="PvpFighter"/> 上，
    /// 经 <see cref="SeatSetup"/> 进 <see cref="RelicCombat"/>。不含消耗品使用（PVP 无消耗入口）。
    /// </summary>
    public static class PvpRelicRuntime
    {
        public const int HandTypeCountSlots = 6;

        public static int SelfDecayAttackTotal(IGameTables tables, PvpFighter fighter)
        {
            var total = 0;
            ForEachEntry(tables, fighter, (relic, entry) =>
            {
                if (entry.Type == MechanismType.SelfDecayAttack)
                {
                    total += SelfDecayAttackLeft(tables, fighter, relic, entry);
                }
            });
            return total;
        }

        /// <summary>亮牌结算后：牌型计数、贪婪叠层、自衰减、同花顺召唤。</summary>
        public static void AfterShowdown(
            IGameTables tables,
            PvpFighter fighter,
            HandType shownType,
            bool? won,
            Random rng)
        {
            AddHandTypeShowCount(fighter, shownType);
            if (won.HasValue)
            {
                OnCompareResult(tables, fighter, won.Value);
            }

            TickSelfDecay(tables, fighter);
            TrySummonConsumable(tables, fighter, shownType, rng);
        }

        public static void OnShopRefreshed(IGameTables tables, PvpFighter fighter)
        {
            ForEachEntry(tables, fighter, (relic, entry) =>
            {
                if (entry.Type != MechanismType.ShopRefreshGetMult)
                {
                    return;
                }

                fighter.RelicShopRefreshCounts.TryGetValue(relic.Id, out var count);
                fighter.RelicShopRefreshCounts[relic.Id] = count + 1;
            });
        }

        public static void RefreshCopiedRelic(IGameTables tables, PvpFighter fighter, Random rng)
        {
            fighter.CopiedRelicId = 0;
            if (rng == null || !HasMechanism(tables, fighter, MechanismType.CopyRandomRelic))
            {
                return;
            }

            var candidates = new List<int>();
            for (var i = 0; i < fighter.OwnedRelicIds.Count; i++)
            {
                var id = fighter.OwnedRelicIds[i];
                if (id <= 0 || !tables.TryGetRelic(id, out var relic) || IsConsumable(relic))
                {
                    continue;
                }

                var isCopy = false;
                ForEachRelicEntry(tables, relic, entry =>
                {
                    if (entry.Type == MechanismType.CopyRandomRelic)
                    {
                        isCopy = true;
                    }
                });
                if (isCopy)
                {
                    continue;
                }

                candidates.Add(id);
            }

            if (candidates.Count == 0)
            {
                return;
            }

            fighter.CopiedRelicId = candidates[rng.Next(candidates.Count)];
        }

        /// <summary>第六感：逐条独立掷骰；持有幸运之子时概率翻倍。命中则反转胜负。</summary>
        public static bool RollReverse(IGameTables tables, PvpFighter fighter, Random rng)
        {
            if (rng == null)
            {
                return false;
            }

            var hit = false;
            ForEachEntry(tables, fighter, (_, entry) =>
            {
                if (hit || entry.Type != MechanismType.ReverseResult)
                {
                    return;
                }

                var chance = ProbabilityValue(tables, fighter, entry);
                if (chance > 0f && rng.NextDouble() < chance)
                {
                    hit = true;
                }
            });
            return hit;
        }

        /// <summary>疯狂猴子：回合/子桌结算后按概率毁掉自身。</summary>
        public static void ApplySelfDestroy(IGameTables tables, PvpFighter fighter, Random rng, Action<int, int>? returned = null)
        {
            if (rng == null)
            {
                return;
            }

            List<int>? destroyed = null;
            ForEachEntry(tables, fighter, (relic, entry) =>
            {
                if (entry.Type != MechanismType.SelfDestroyPerRound)
                {
                    return;
                }

                var chance = ProbabilityValue(tables, fighter, entry);
                if (chance <= 0f || rng.NextDouble() >= chance)
                {
                    return;
                }

                destroyed ??= new List<int>();
                if (!destroyed.Contains(relic.Id))
                {
                    destroyed.Add(relic.Id);
                }
            });

            if (destroyed == null)
            {
                return;
            }

            for (var i = 0; i < destroyed.Count; i++)
            {
                var copies = PvpRelicBag.RemoveOne(fighter, destroyed[i]);
                if (copies > 0)
                {
                    returned?.Invoke(destroyed[i], copies);
                }
            }

            CleanupTrackers(fighter);
        }

        /// <summary>卖掉/自毁后清掉孤儿追踪键。</summary>
        public static void CleanupTrackers(PvpFighter fighter)
        {
            PruneUnowned(fighter.SelfDecayAttackLeft, fighter);
            PruneUnowned(fighter.RelicSelfDecayMag, fighter);
            PruneUnowned(fighter.RelicShopRefreshCounts, fighter);
            PruneUnowned(fighter.RelicWinLoseMag, fighter);
            if (fighter.CopiedRelicId > 0 && !fighter.OwnedRelicIds.Contains(fighter.CopiedRelicId))
            {
                // 复制目标已不在栏：本回合复制倍率失效，下轮 RefreshCopiedRelic 会重抽。
                fighter.CopiedRelicId = 0;
            }
        }

        public static bool HasMechanism(IGameTables tables, PvpFighter fighter, MechanismType type)
        {
            var found = false;
            ForEachEntry(tables, fighter, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    found = true;
                }
            });
            return found;
        }

        /// <summary>写入比牌用 SeatSetup 追踪字段（含甜品加攻与当前持有圣物列表）。</summary>
        public static void ApplyTrackersToSeat(IGameTables tables, PvpFighter fighter, SeatSetup setup)
        {
            setup.RelicIds = PvpRelicBag.Expand(fighter);
            setup.HandTypeShowCounts = (int[])fighter.HandTypeShowCounts.Clone();
            setup.RelicSelfDecayMag = CloneFloatDict(fighter.RelicSelfDecayMag);
            setup.RelicShopRefreshCounts = CloneIntDict(fighter.RelicShopRefreshCounts);
            setup.RelicWinLoseMag = CloneFloatDict(fighter.RelicWinLoseMag);
            setup.ConsumableUsesThisRun = fighter.ConsumableUsesThisRun;
            setup.CopiedRelicId = fighter.CopiedRelicId;
            setup.Attack = Math.Max(0, fighter.Combat.Attack + SelfDecayAttackTotal(tables, fighter));
        }

        // ——— 治疗与血量上限：PVP 服务端权威结算，口径对齐 PVE GameSession（ApplyRelicMaxHpDelta / HealPlayer） ———

        /// <summary>获得圣物（商店购买/调试发放）时的面板结算：血量上限类（HeroHpMax）上限+X 且当前血同步+X。</summary>
        public static void ApplyAcquireStats(IGameTables tables, PvpFighter fighter, int relicId)
        {
            ApplyMaxHpDelta(fighter, SumRelicInt(tables, relicId, MechanismType.HeroHpMax));
        }

        /// <summary>出售圣物：血量上限反向回收（上限-X、当前血夹到新上限，血不低于 1）。</summary>
        public static void ApplySellStats(IGameTables tables, PvpFighter fighter, int relicId)
        {
            ApplyMaxHpDelta(fighter, -SumRelicInt(tables, relicId, MechanismType.HeroHpMax));
        }

        /// <summary>回合开始：绷带/小精灵回血（HeroHpReplyEveryRoundEnding）、永恒之心上限成长（EveryRoundGetHpMax）、
        /// 保温杯低血回复（ThermosCup，语义"回合结束后"，在此按结算窗口等效判定）。</summary>
        public static void ApplyRoundStart(IGameTables tables, PvpFighter fighter)
        {
            HealFromRelic(tables, fighter, SumOwnedInt(tables, fighter, MechanismType.HeroHpReplyEveryRoundEnding));
            ApplyMaxHpDelta(fighter, SumOwnedInt(tables, fighter, MechanismType.EveryRoundGetHpMax));
            ForEachEntry(tables, fighter, (_, entry) =>
            {
                if (entry.Type != MechanismType.ThermosCup)
                {
                    return;
                }

                var threshold = ValueAt(entry);
                if (threshold > 0f && fighter.MaxHp > 0 && fighter.Hp < fighter.MaxHp * threshold)
                {
                    HealFromRelic(tables, fighter, (int)Math.Round(ValueAt(entry, 1)));
                }
            });
        }

        /// <summary>使用消耗型圣物：结算 PVP 支持的即时效果。返回 false 表示该圣物含 PVP 未支持的
        /// 消耗类型（如 PVE 专属的改牌型/商店折扣），调用方应拒绝使用，防止花钱白用。</summary>
        public static bool TryApplyConsumable(IGameTables tables, PvpFighter fighter, RelicConfig relic)
        {
            if (relic?.MechanismId == null || relic.MechanismId.Length == 0)
            {
                return false;
            }

            var anyApplied = false;
            var allSupported = true;
            ForEachRelicEntry(tables, relic, entry =>
            {
                switch (entry.Type)
                {
                    case MechanismType.HealHpPercent:
                        HealFromRelic(tables, fighter, (int)Math.Round(fighter.MaxHp * ValueAt(entry)));
                        anyApplied = true;
                        break;
                    case MechanismType.MaxHpUpAndHeal:
                        // 配表 Value=[X, X]：上限+X 且当前血同步+X（等效"立即恢复 X"，同 PVE 只取 value[0] 的口径）。
                        ApplyMaxHpDelta(fighter, (int)Math.Round(ValueAt(entry)));
                        anyApplied = true;
                        break;
                    case MechanismType.UseRoundNullify:
                        fighter.NullifyDamageNextHit = true;
                        HealFromRelic(tables, fighter, (int)Math.Round(fighter.MaxHp * ValueAt(entry, 1)));
                        anyApplied = true;
                        break;
                    default:
                        allSupported = false;
                        break;
                }
            });
            return anyApplied && allSupported;
        }

        /// <summary>圣物全部机制条目都被 PVP 结算消费才允许上架/使用；白名单外的圣物不上 PVP 商店货架。
        /// 空机制（测试占位/纯收藏品）放行——没有效果也就没有"不支持的效果"。
        /// 新增 PVP 机制（RelicCombat case / 本文件处理）时必须同步维护此集合，漏加只会少上架、不会卖废品。</summary>
        public static bool IsPvpSupported(IGameTables tables, RelicConfig relic)
        {
            // 配置缺失时保守不上架（两个调用方实际都已判空，此处仅兜底防 NRE）。
            if (relic == null)
            {
                return false;
            }

            if (relic.MechanismId == null || relic.MechanismId.Length == 0)
            {
                // 空机制消耗品用了没有任何效果，同样不允许上架。
                return relic.UseType != 1 && relic.UseType != 2;
            }

            var supported = true;
            ForEachRelicEntry(tables, relic, entry =>
            {
                if (!SupportedMechanisms.Contains(entry.Type))
                {
                    supported = false;
                }
            });
            return supported;
        }

        /// <summary>PVP 结算支持的机制全集 = RelicCombat 的出伤/倍率/攻击 case + 本文件的回合/概率/治疗 + 技能次数注入。</summary>
        private static readonly HashSet<MechanismType> SupportedMechanisms = new HashSet<MechanismType>
        {
            // RelicCombat：倍率
            MechanismType.CardMagnification,
            MechanismType.SquarePlate,
            MechanismType.Spades,
            MechanismType.RedHeart,
            MechanismType.PlumBlossom,
            MechanismType.Couplet,
            MechanismType.Flush,
            MechanismType.Straight,
            MechanismType.StraightFlush,
            MechanismType.Leopard,
            MechanismType.EvenNumberCard,
            MechanismType.OddNumberCard,
            MechanismType.HeadCard,
            MechanismType.SpecialACard,
            MechanismType.Camera,
            MechanismType.Cupid,
            MechanismType.EveryUseRubbingNum,
            MechanismType.NoSkill,
            MechanismType.EveryRelic,
            MechanismType.NoUseRubbingEveryRubbingNum,
            MechanismType.AccumulatedNumOfCardType,
            MechanismType.RubbingCardRelic,
            MechanismType.ProOfUpCardType,
            MechanismType.SpecialSevenCard,
            MechanismType.DefeatGetMagnification,
            MechanismType.NoKillMonsterGetMagnification,
            MechanismType.FixedTypeCountMult,
            MechanismType.ShopRefreshGetMult,
            MechanismType.SelfDecayMult,
            MechanismType.SelfMultWinLose,
            MechanismType.UseConsumableGetMult,
            MechanismType.UnshownRankMult,
            MechanismType.CopyRandomRelic,
            // RelicCombat：攻击
            MechanismType.SquarePlateAttack,
            MechanismType.SpadesAttack,
            MechanismType.RedHeartAttack,
            MechanismType.PlumBlossomAttack,
            MechanismType.CoupletAttack,
            MechanismType.StraightAttack,
            MechanismType.FlushAttack,
            MechanismType.StraightFlushAttack,
            MechanismType.LeopardAttack,
            MechanismType.SpecialEightCard,
            MechanismType.DoubleCardAttack,
            MechanismType.HeadCardAttack,
            MechanismType.ACardAttack,
            MechanismType.TheSwordOfVictory,
            MechanismType.ConsumeFundsGetAttack,
            MechanismType.SpecialSevenCardAttack,
            MechanismType.CardProvideAttack,
            MechanismType.EveryRubbingNum,
            MechanismType.UseConsumableGetAttack,
            MechanismType.SelfDecayAttack,
            // 技能次数（CombatBonuses / BeforeCompare 注入）
            MechanismType.RubbingCardsNum,
            MechanismType.PerspectiveNum,
            // 本文件：回合/概率
            MechanismType.ReverseResult,
            MechanismType.SelfDestroyPerRound,
            MechanismType.PeekSteal,
            MechanismType.SummonConsumable,
            // 本文件：治疗/血量上限（回合与购买结算）
            MechanismType.HeroHpMax,
            MechanismType.HeroHpReplyEveryRoundEnding,
            MechanismType.EveryRoundGetHpMax,
            MechanismType.ThermosCup,
            MechanismType.WinHeal,
            MechanismType.DefeatGetHpMax,
            MechanismType.HealHpPercent,
            MechanismType.MaxHpUpAndHeal,
            MechanismType.UseRoundNullify,
        };

        /// <summary>比牌获胜：月光酒回血（WinHeal）+ 激励徽章上限成长（DefeatGetHpMax）。</summary>
        public static void ApplyWinRewards(IGameTables tables, PvpFighter fighter)
        {
            HealFromRelic(tables, fighter, SumOwnedInt(tables, fighter, MechanismType.WinHeal));
            ApplyMaxHpDelta(fighter, SumOwnedInt(tables, fighter, MechanismType.DefeatGetHpMax));
        }

        private static void ApplyMaxHpDelta(PvpFighter fighter, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            var maxHp = Math.Max(1, fighter.MaxHp + delta);
            var hp = delta > 0 ? fighter.Hp + delta : Math.Min(fighter.Hp, maxHp);
            if (hp < 1)
            {
                hp = 1;
            }

            fighter.MaxHp = maxHp;
            fighter.Hp = hp;
        }

        private static int HealFromRelic(IGameTables tables, PvpFighter fighter, int amount)
        {
            return Heal(fighter, PvpHeroRuntime.ScaleRelicHeal(tables, fighter.Combat.HeroId, amount));
        }

        private static int Heal(PvpFighter fighter, int amount)
        {
            if (amount <= 0 || fighter.Hp >= fighter.MaxHp)
            {
                return 0;
            }

            var healed = Math.Min(amount, fighter.MaxHp - fighter.Hp);
            fighter.Hp += healed;
            return healed;
        }

        /// <summary>按机制汇总已持有圣物的 value[0]（取整）。</summary>
        private static int SumOwnedInt(IGameTables tables, PvpFighter fighter, MechanismType type)
        {
            var total = 0f;
            ForEachEntry(tables, fighter, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    total += ValueAt(entry);
                }
            });
            return (int)Math.Round(total);
        }

        /// <summary>按机制汇总单个圣物的 value[0]（取整，不要求已持有）。</summary>
        private static int SumRelicInt(IGameTables tables, int relicId, MechanismType type)
        {
            var total = 0f;
            if (tables.TryGetRelic(relicId, out var relic))
            {
                ForEachRelicEntry(tables, relic, entry =>
                {
                    if (entry.Type == type)
                    {
                        total += ValueAt(entry);
                    }
                });
            }

            return (int)Math.Round(total);
        }

        private static void OnCompareResult(IGameTables tables, PvpFighter fighter, bool won)
        {
            ForEachEntry(tables, fighter, (relic, entry) =>
            {
                if (entry.Type != MechanismType.SelfMultWinLose)
                {
                    return;
                }

                fighter.RelicWinLoseMag.TryGetValue(relic.Id, out var current);
                var delta = won ? ValueAt(entry) : -ValueAt(entry, 1);
                fighter.RelicWinLoseMag[relic.Id] = Math.Max(0f, current + delta);
            });
        }

        private static void TickSelfDecay(IGameTables tables, PvpFighter fighter)
        {
            ForEachEntry(tables, fighter, (relic, entry) =>
            {
                if (entry.Type == MechanismType.SelfDecayAttack)
                {
                    var left = SelfDecayAttackLeft(tables, fighter, relic, entry);
                    var decay = Math.Max(0, (int)Math.Round(ValueAt(entry, 1)));
                    var loss = Math.Min(left, decay);
                    if (loss > 0)
                    {
                        fighter.SelfDecayAttackLeft[relic.Id] = left - loss;
                    }
                }
                else if (entry.Type == MechanismType.SelfDecayMult)
                {
                    var left = SelfDecayMagLeft(tables, fighter, relic, entry);
                    var decay = Math.Max(0f, ValueAt(entry, 1));
                    var loss = Math.Min(left, decay);
                    if (loss > 0f)
                    {
                        fighter.RelicSelfDecayMag[relic.Id] = left - loss;
                    }
                }
            });
        }

        private static void TrySummonConsumable(
            IGameTables tables,
            PvpFighter fighter,
            HandType shownType,
            Random rng)
        {
            if (rng == null || shownType != HandType.StraightFlush)
            {
                return;
            }

            if (!HasMechanism(tables, fighter, MechanismType.SummonConsumable))
            {
                return;
            }

            if (fighter.OwnedRelicIds.Count >= fighter.RelicSlots)
            {
                return;
            }

            var pool = new List<int>();
            for (var i = 0; i < tables.Relics.Count; i++)
            {
                var relic = tables.Relics[i];
                if (relic == null || !IsConsumable(relic))
                {
                    continue;
                }

                pool.Add(relic.Id);
            }

            if (pool.Count == 0)
            {
                return;
            }

            PvpRelicBag.Add(fighter, pool[rng.Next(pool.Count)]);
        }

        private static void AddHandTypeShowCount(PvpFighter fighter, HandType type)
        {
            var key = (int)type;
            if (key >= 0 && key < fighter.HandTypeShowCounts.Length)
            {
                fighter.HandTypeShowCounts[key]++;
            }
        }

        private static int SelfDecayAttackLeft(
            IGameTables tables,
            PvpFighter fighter,
            RelicConfig relic,
            RelicEntryConfig entry)
        {
            if (fighter.SelfDecayAttackLeft.TryGetValue(relic.Id, out var left))
            {
                return left;
            }

            left = Math.Max(0, (int)Math.Round(ValueAt(entry)));
            fighter.SelfDecayAttackLeft[relic.Id] = left;
            return left;
        }

        private static float SelfDecayMagLeft(
            IGameTables tables,
            PvpFighter fighter,
            RelicConfig relic,
            RelicEntryConfig entry)
        {
            if (fighter.RelicSelfDecayMag.TryGetValue(relic.Id, out var left))
            {
                return left;
            }

            left = Math.Max(0f, ValueAt(entry));
            fighter.RelicSelfDecayMag[relic.Id] = left;
            return left;
        }

        private static float ProbabilityValue(IGameTables tables, PvpFighter fighter, RelicEntryConfig entry)
        {
            var value = ValueAt(entry);
            if (value > 0f && IsProbabilityType(entry.Type) && HasMechanism(tables, fighter, MechanismType.LuckyDouble))
            {
                value *= 2f;
            }

            return value;
        }

        private static bool IsProbabilityType(MechanismType type)
        {
            switch (type)
            {
                case MechanismType.ReverseResult:
                case MechanismType.SelfDestroyPerRound:
                    return true;
                default:
                    return false;
            }
        }

        private static float ValueAt(RelicEntryConfig entry, int index = 0)
        {
            if (entry.Value == null || index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
        }

        private static bool IsConsumable(RelicConfig relic)
        {
            // RelicUseType：1 InstantConsume / 2 PermanentConsume（与 App.Game.RelicUseType 对齐）。
            return relic.UseType == 1 || relic.UseType == 2;
        }

        private static void ForEachEntry(IGameTables tables, PvpFighter fighter, Action<RelicConfig, RelicEntryConfig> action)
        {
            for (var i = 0; i < fighter.OwnedRelicIds.Count; i++)
            {
                if (!tables.TryGetRelic(fighter.OwnedRelicIds[i], out var relic))
                {
                    continue;
                }

                var power = PvpRelicBag.PowerAt(fighter, i);
                for (var n = 0; n < power; n++)
                {
                    ForEachRelicEntry(tables, relic, entry => action(relic, entry));
                }
            }
        }

        private static void ForEachRelicEntry(IGameTables tables, RelicConfig relic, Action<RelicEntryConfig> action)
        {
            if (relic.MechanismId == null)
            {
                return;
            }

            for (var i = 0; i < relic.MechanismId.Length; i++)
            {
                if (tables.TryGetRelicEntry(relic.MechanismId[i], out var entry))
                {
                    action(entry);
                }
            }
        }

        private static void PruneUnowned<T>(Dictionary<int, T> dict, PvpFighter fighter)
        {
            if (dict.Count == 0)
            {
                return;
            }

            List<int>? stale = null;
            foreach (var key in dict.Keys)
            {
                if (!fighter.OwnedRelicIds.Contains(key))
                {
                    (stale ??= new List<int>()).Add(key);
                }
            }

            if (stale == null)
            {
                return;
            }

            for (var i = 0; i < stale.Count; i++)
            {
                dict.Remove(stale[i]);
            }
        }

        private static IReadOnlyDictionary<int, float>? CloneFloatDict(Dictionary<int, float> source)
        {
            if (source.Count == 0)
            {
                return null;
            }

            return new Dictionary<int, float>(source);
        }

        private static IReadOnlyDictionary<int, int>? CloneIntDict(Dictionary<int, int> source)
        {
            if (source.Count == 0)
            {
                return null;
            }

            return new Dictionary<int, int>(source);
        }
    }
}
