using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 羁绊：表里所有 BindingID 含该羁绊的圣物都至少携带一份才生效。
    /// 纯计算与完成判定；持久状态由 RunState / PvpFighter 持有。
    /// </summary>
    public static class PvpRelicBindings
    {
        public static bool IsComplete(IGameTables tables, PvpFighter fighter, int bindingId)
            => IsComplete(tables, fighter != null ? fighter.OwnedRelicIds : null, bindingId);

        public static bool IsComplete(IGameTables tables, IReadOnlyList<int>? ownedRelicIds, int bindingId)
        {
            if (bindingId <= 0 || ownedRelicIds == null)
            {
                return false;
            }

            var required = 0;
            var relics = tables.PvpRelics;
            for (var i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null || !Contains(relic.BindingID, bindingId))
                {
                    continue;
                }

                required++;
                if (!Owns(ownedRelicIds, relic.Id))
                {
                    return false;
                }
            }

            return required > 0;
        }

        public static bool IsTypeComplete(IGameTables tables, IReadOnlyList<int>? ownedRelicIds, BindingType type)
        {
            if (ownedRelicIds == null)
            {
                return false;
            }

            var bindings = tables.Bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding != null && binding.Type == type && IsComplete(tables, ownedRelicIds, binding.Id))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>已持有至少一件成员圣物的羁绊。未凑齐仍返回，由界面置灰。</summary>
        public static void CollectHeld(
            IGameTables tables,
            IReadOnlyList<int>? ownedRelicIds,
            List<BindingHold> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();
            if (tables == null || ownedRelicIds == null || ownedRelicIds.Count == 0)
            {
                return;
            }

            var bindings = tables.Bindings;
            var relics = tables.PvpRelics;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null)
                {
                    continue;
                }

                var required = 0;
                var owned = 0;
                for (var r = 0; r < relics.Count; r++)
                {
                    var relic = relics[r];
                    if (relic == null || !Contains(relic.BindingID, binding.Id))
                    {
                        continue;
                    }

                    required++;
                    if (Owns(ownedRelicIds, relic.Id))
                    {
                        owned++;
                    }
                }

                if (required <= 0 || owned <= 0)
                {
                    continue;
                }

                into.Add(new BindingHold(binding, owned, required));
            }
        }

        public static void ForEachComplete(
            IGameTables tables,
            IReadOnlyList<int>? ownedRelicIds,
            Action<BindingConfig> action)
        {
            if (ownedRelicIds == null || action == null)
            {
                return;
            }

            var bindings = tables.Bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null || !IsComplete(tables, ownedRelicIds, binding.Id))
                {
                    continue;
                }

                action(binding);
            }
        }

        public static BindingConfig? FindComplete(IGameTables tables, IReadOnlyList<int>? ownedRelicIds, BindingType type)
        {
            if (ownedRelicIds == null)
            {
                return null;
            }

            var bindings = tables.Bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding != null && binding.Type == type && IsComplete(tables, ownedRelicIds, binding.Id))
                {
                    return binding;
                }
            }

            return null;
        }

        public static int BonusDamage(IGameTables tables, PvpFighter fighter, int damage, int attack, bool isCrit)
            => BonusDamage(tables, fighter != null ? fighter.OwnedRelicIds : null, damage, attack, isCrit);

        /// <summary>雷霆：仅暴击时，次数 × round(当前攻击 × Value[1])。Value[1]=5 → 攻击力 500%。</summary>
        public static int BonusDamage(
            IGameTables tables,
            IReadOnlyList<int>? ownedRelicIds,
            int damage,
            int attack,
            bool isCrit)
        {
            if (ownedRelicIds == null || !isCrit || attack <= 0)
            {
                return 0;
            }

            var bonus = 0;
            ForEachComplete(tables, ownedRelicIds, binding =>
            {
                if (binding.Type != BindingType.Leiting)
                {
                    return;
                }

                var times = IntAt(binding, 0);
                var ratio = FloatAt(binding, 1);
                if (times <= 0 || ratio <= 0f)
                {
                    return;
                }

                bonus += times * (int)Math.Round(attack * ratio);
            });
            return bonus;
        }

        public static int BronzeBookAttack(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var add = 0;
            ForEachComplete(tables, ownedRelicIds, binding =>
            {
                if (binding.Type == BindingType.BronzeBook)
                {
                    add += IntAt(binding, 0);
                }
            });
            return add;
        }

        public static float SilverBookMag(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var add = 0f;
            ForEachComplete(tables, ownedRelicIds, binding =>
            {
                if (binding.Type == BindingType.SilverBook)
                {
                    add += FloatAt(binding, 0);
                }
            });
            return add;
        }

        public static float MagicCoinSpendBonus(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var add = 0f;
            ForEachComplete(tables, ownedRelicIds, binding =>
            {
                if (binding.Type == BindingType.MagicCoin)
                {
                    add += FloatAt(binding, 0);
                }
            });
            return add;
        }

        public static int BloodCeremonyTimes(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var times = 0;
            ForEachComplete(tables, ownedRelicIds, binding =>
            {
                if (binding.Type == BindingType.BloodCeremony)
                {
                    times += Math.Max(1, IntAt(binding, 0));
                }
            });
            return times;
        }

        public static int DoubleAttackTimes(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var times = 0;
            ForEachComplete(tables, ownedRelicIds, binding =>
            {
                if (binding.Type == BindingType.DoubleAttack)
                {
                    times += Math.Max(1, IntAt(binding, 0));
                }
            });
            return times;
        }

        public static float DoubleAttackDamageScale(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var binding = FindComplete(tables, ownedRelicIds, BindingType.DoubleAttack);
            if (binding == null)
            {
                return 1f;
            }

            return 1f + FloatAt(binding, 1);
        }

        public static int CurseSilenceReviveTimes(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var binding = FindComplete(tables, ownedRelicIds, BindingType.CurseSilence);
            return binding == null ? 0 : Math.Max(0, IntAt(binding, 0));
        }

        public static bool CurseSilenceActive(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
            => FindComplete(tables, ownedRelicIds, BindingType.CurseSilence) != null;

        public static int LuckyNumberEnhanceCount(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var binding = FindComplete(tables, ownedRelicIds, BindingType.LuckyNumber);
            return binding == null ? 0 : Math.Max(0, IntAt(binding, 0));
        }

        public static float LuckyNumberWinChancePerCard(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var binding = FindComplete(tables, ownedRelicIds, BindingType.LuckyNumber);
            return binding == null ? 0f : FloatAt(binding, 1);
        }

        public static int NobilityStackPerFace(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var binding = FindComplete(tables, ownedRelicIds, BindingType.NobilityPower);
            return binding == null ? 0 : Math.Max(0, IntAt(binding, 0));
        }

        public static int NobilityThreshold(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var binding = FindComplete(tables, ownedRelicIds, BindingType.NobilityPower);
            return binding == null ? 0 : Math.Max(0, IntAt(binding, 1));
        }

        public static float NobilityRedirectChance(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
        {
            var binding = FindComplete(tables, ownedRelicIds, BindingType.NobilityPower);
            return binding == null ? 0f : FloatAt(binding, 2);
        }

        public static bool TryRollRandomAttribute(
            IGameTables tables,
            IReadOnlyList<int>? ownedRelicIds,
            Random rng,
            out int attackAdd,
            out int maxHpAdd,
            out float critAdd)
        {
            attackAdd = 0;
            maxHpAdd = 0;
            critAdd = 0f;
            var binding = FindComplete(tables, ownedRelicIds, BindingType.RandomAttribute);
            if (binding == null || rng == null)
            {
                return false;
            }

            var roll = rng.Next(3);
            switch (roll)
            {
                case 0:
                    attackAdd = IntAt(binding, 0);
                    break;
                case 1:
                    maxHpAdd = IntAt(binding, 1);
                    break;
                default:
                    critAdd = FloatAt(binding, 2);
                    break;
            }

            return attackAdd != 0 || maxHpAdd != 0 || critAdd != 0f;
        }

        public static bool DisableSkillActive(IGameTables tables, IReadOnlyList<int>? ownedRelicIds)
            => FindComplete(tables, ownedRelicIds, BindingType.DisableSkill) != null;

        public static int DisableSkillDamage(IGameTables tables, IReadOnlyList<int>? ownedRelicIds, int skillCharges, int attack)
        {
            if (!DisableSkillActive(tables, ownedRelicIds) || skillCharges <= 0 || attack <= 0)
            {
                return 0;
            }

            return skillCharges * attack;
        }

        public static bool TryLuckyForceWin(
            IGameTables tables,
            IReadOnlyList<int>? ownedRelicIds,
            IReadOnlyCollection<int> enhancedRanks,
            IReadOnlyList<Card>? shownCards,
            Random rng)
        {
            if (enhancedRanks == null || enhancedRanks.Count == 0 || shownCards == null || rng == null)
            {
                return false;
            }

            var per = LuckyNumberWinChancePerCard(tables, ownedRelicIds);
            if (per <= 0f)
            {
                return false;
            }

            var hits = 0;
            for (var i = 0; i < shownCards.Count; i++)
            {
                var card = shownCards[i];
                if (!card.IsValid || card.IsFace)
                {
                    continue;
                }

                var rank = (int)card.Rank;
                var found = false;
                foreach (var enhanced in enhancedRanks)
                {
                    if (enhanced == rank)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    hits++;
                }
            }

            for (var i = 0; i < hits; i++)
            {
                if (rng.NextDouble() < per)
                {
                    return true;
                }
            }

            return false;
        }

        public static int IntAt(BindingConfig binding, int index)
        {
            if (binding?.Value == null || index < 0 || index >= binding.Value.Length)
            {
                return 0;
            }

            return (int)Math.Round(binding.Value[index]);
        }

        public static float FloatAt(BindingConfig binding, int index)
        {
            if (binding?.Value == null || index < 0 || index >= binding.Value.Length)
            {
                return 0f;
            }

            return binding.Value[index];
        }

        public readonly struct BindingHold
        {
            public BindingHold(BindingConfig binding, int owned, int required)
            {
                Binding = binding;
                Owned = owned;
                Required = required;
            }

            public BindingConfig Binding { get; }

            public int Owned { get; }

            public int Required { get; }

            public bool Complete => Required > 0 && Owned >= Required;
        }

        private static bool Owns(IReadOnlyList<int>? ownedRelicIds, int relicId)
        {
            if (ownedRelicIds == null)
            {
                return false;
            }

            for (var i = 0; i < ownedRelicIds.Count; i++)
            {
                if (ownedRelicIds[i] == relicId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(int[] ids, int bindingId)
        {
            if (ids == null)
            {
                return false;
            }

            for (var i = 0; i < ids.Length; i++)
            {
                if (ids[i] == bindingId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
