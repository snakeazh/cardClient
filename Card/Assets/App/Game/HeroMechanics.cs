using System;
using CardShare.Contracts.Config;
using App.Talent;

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

        /// <summary>
        /// 条件伤害百分比：死灵法师对精英/领主；决斗大师/孩子王按存活敌人数；亡命徒低血。
        /// 与天赋同 Type 在 <c>ComputeAttackDamage</c> 相加；精英判定仅英雄侧生效。
        /// </summary>
        public static float SumDamagePercent(
            HeroConfig hero,
            SeatState defender,
            SeatState player,
            int aliveEnemies)
        {
            var percent = 0f;
            if (defender != null &&
                (defender.MonsterType == MonsterType.Elite || defender.MonsterType == MonsterType.Boss))
            {
                percent += SumValue(hero, MechanismType.AttackBossDamage);
            }

            if (aliveEnemies > 1)
            {
                percent += SumValue(hero, MechanismType.ManyMonsterDamage);
            }
            else if (aliveEnemies == 1)
            {
                percent += SumValue(hero, MechanismType.OneMonsterDamage);
            }

            if (TalentMechanics.IsBelowHpRatio(player, TalentBalance.LowHpRatio))
            {
                percent += SumValue(hero, MechanismType.HpUnderDamage);
            }

            return percent;
        }

        public static float SumMultiplierExtra(HeroConfig hero, bool firstShowThisStage)
        {
            return firstShowThisStage
                ? SumValue(hero, MechanismType.FirstShowCardEveryLevel)
                : 0f;
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

        /// <summary>天赋 + 英雄同 Type 概率求和后掷一次。</summary>
        public static bool RollCombined(float talentChance, float heroChance, Random rng)
        {
            var chance = talentChance + heroChance;
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
