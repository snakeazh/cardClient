using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 羁绊：表里所有 BindingID 含该羁绊的圣物都至少携带一份才生效。
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
            var relics = tables.Relics;
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

        public static int BonusDamage(IGameTables tables, PvpFighter fighter, int damage)
            => BonusDamage(tables, fighter != null ? fighter.OwnedRelicIds : null, damage);

        /// <summary>已达成羁绊的附加伤害。雷霆：次数 × round(原伤害 × 比例)。</summary>
        public static int BonusDamage(IGameTables tables, IReadOnlyList<int>? ownedRelicIds, int damage)
        {
            if (ownedRelicIds == null || damage <= 0)
            {
                return 0;
            }

            var bonus = 0;
            var bindings = tables.Bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null || binding.Type != BindingType.Leiting || !IsComplete(tables, ownedRelicIds, binding.Id))
                {
                    continue;
                }

                var times = Times(binding);
                var ratio = Ratio(binding);
                if (times <= 0 || ratio <= 0f)
                {
                    continue;
                }

                bonus += times * (int)Math.Round(damage * ratio);
            }

            return bonus;
        }

        private static int Times(BindingConfig binding)
        {
            if (binding.Value == null || binding.Value.Length < 1)
            {
                return 0;
            }

            return (int)Math.Round(binding.Value[0]);
        }

        private static float Ratio(BindingConfig binding)
        {
            if (binding.Value == null || binding.Value.Length < 2)
            {
                return 0f;
            }

            return binding.Value[1];
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
