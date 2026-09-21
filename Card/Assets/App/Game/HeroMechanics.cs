using System;
using App.Config;
using CardShare.Contracts.Config;

namespace App.Game
{
    /// <summary>
    /// 按当前英雄 <see cref="HeroConfig.HeroEntryId"/> 结算局内技能。收藏禁用不禁用英雄词条。
    /// </summary>
    public static class HeroMechanics
    {
        public static HeroConfig Resolve(RunState run)
        {
            return run != null && run.HeroId > 0 ? HeroConfig.Get(run.HeroId) : null;
        }

        public static float SumValue(RunState run, MechanismType type)
        {
            return SumValue(Resolve(run), type);
        }

        public static float SumValue(HeroConfig hero, MechanismType type)
        {
            var sum = 0f;
            ForEachEntry(hero, entry =>
            {
                if (entry.Type == type)
                {
                    sum += entry.Value;
                }
            });
            return sum;
        }

        public static bool HasMechanism(RunState run, MechanismType type)
        {
            return HasMechanism(Resolve(run), type);
        }

        public static bool HasMechanism(HeroConfig hero, MechanismType type)
        {
            var found = false;
            ForEachEntry(hero, entry =>
            {
                if (entry.Type == type)
                {
                    found = true;
                }
            });
            return found;
        }

        public static bool Roll(RunState run, MechanismType type, Random rng)
        {
            return Roll(Resolve(run), type, rng);
        }

        public static bool Roll(HeroConfig hero, MechanismType type, Random rng)
        {
            var chance = SumValue(hero, type);
            if (chance <= 0f || rng == null)
            {
                return false;
            }

            return rng.NextDouble() < chance;
        }

        public static int BuyPrice(RunState run, RelicConfig relic)
        {
            return BuyPrice(Resolve(run), relic);
        }

        public static int BuyPrice(HeroConfig hero, RelicConfig relic)
        {
            if (relic == null)
            {
                return 0;
            }

            var per = SumValue(hero, MechanismType.RelicPricePer);
            if (per == 0f)
            {
                return Math.Max(0, relic.Price);
            }

            return Math.Max(0, (int)Math.Round(relic.Price * (1f + per)));
        }

        public static float ShopWeight(RunState run, RelicConfig relic)
        {
            return ShopWeight(Resolve(run), relic);
        }

        public static float ShopWeight(HeroConfig hero, RelicConfig relic)
        {
            if (relic == null)
            {
                return 0f;
            }

            var weight = Math.Max(0f, relic.RefreshProbability);
            if (weight <= 0f)
            {
                return 0f;
            }

            if (relic.Type == QualityType.Epic || relic.Type == QualityType.Legend)
            {
                weight *= 1f + SumValue(hero, MechanismType.EpicLegendRelicProUp);
            }

            return Math.Max(0f, weight);
        }

        public static void ForEachEntry(HeroConfig hero, Action<HeroEntryConfig> action)
        {
            if (hero?.HeroEntryId == null || action == null)
            {
                return;
            }

            for (var i = 0; i < hero.HeroEntryId.Length; i++)
            {
                var entry = HeroEntryConfig.Get(hero.HeroEntryId[i]);
                if (entry != null)
                {
                    action(entry);
                }
            }
        }
    }
}
