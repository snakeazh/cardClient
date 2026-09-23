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
        public static void ApplySelfDestroy(IGameTables tables, PvpFighter fighter, Random rng)
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
                fighter.OwnedRelicIds.Remove(destroyed[i]);
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
            setup.RelicIds = CombatBonuses.CloneRelicIds(fighter.OwnedRelicIds);
            setup.HandTypeShowCounts = (int[])fighter.HandTypeShowCounts.Clone();
            setup.RelicSelfDecayMag = CloneFloatDict(fighter.RelicSelfDecayMag);
            setup.RelicShopRefreshCounts = CloneIntDict(fighter.RelicShopRefreshCounts);
            setup.RelicWinLoseMag = CloneFloatDict(fighter.RelicWinLoseMag);
            setup.ConsumableUsesThisRun = fighter.ConsumableUsesThisRun;
            setup.CopiedRelicId = fighter.CopiedRelicId;
            setup.Attack = Math.Max(0, fighter.Combat.Attack + SelfDecayAttackTotal(tables, fighter));
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

            var max = tables.GameConst.DefaultRelicNumMax > 0 ? tables.GameConst.DefaultRelicNumMax : 3;
            if (fighter.OwnedRelicIds.Count >= max)
            {
                return;
            }

            var pool = new List<int>();
            for (var i = 0; i < tables.Relics.Count; i++)
            {
                var relic = tables.Relics[i];
                if (relic == null || !IsConsumable(relic) || fighter.OwnedRelicIds.Contains(relic.Id))
                {
                    continue;
                }

                pool.Add(relic.Id);
            }

            if (pool.Count == 0)
            {
                return;
            }

            fighter.OwnedRelicIds.Add(pool[rng.Next(pool.Count)]);
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

                ForEachRelicEntry(tables, relic, entry => action(relic, entry));
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
