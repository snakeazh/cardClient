using System;
using CardShare.Contracts.Config;
using App.Talent;

namespace App.Game
{
    /// <summary>
    /// 按当前英雄 <see cref="HeroConfig.HeroEntryId"/> 结算局内技能；
    /// <see cref="HeroConfig.HeroEffects"/> 只解析出刀演出用。收藏禁用不禁用英雄词条。
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
                    sum += ValueAt(entry);
                }
            });
            return sum;
        }

        /// <summary>
        /// 英雄侧条件伤害：<see cref="MechanismType.AttackBossDamage"/> 对精英和领主都生效。
        /// 与天赋同 Type 在 <c>ComputeAttackDamage</c> 相加；天赋仍只吃领主。
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

        /// <summary>
        /// 掉血加伤：floor(已损失生命 / Value[0]) 层，每层 Value[1]，层数上限 Value[2]。
        /// </summary>
        public static float LostHpDamagePercent(HeroConfig hero, int hp, int maxHp)
        {
            var lost = Math.Max(0, maxHp - hp);
            var percent = 0f;
            ForEachEntry(hero, entry =>
            {
                if (entry.Type != MechanismType.GainDamageUpWhenHpDecreases)
                {
                    return;
                }

                var step = ValueAt(entry);
                if (step <= 0f)
                {
                    return;
                }

                var stacks = (int)Math.Floor(lost / step);
                var cap = (int)Math.Round(ValueAt(entry, 2));
                if (cap > 0)
                {
                    stacks = Math.Min(stacks, cap);
                }

                percent += stacks * ValueAt(entry, 1);
            });
            return percent;
        }

        /// <summary>
        /// 分裂成长：每累计 Value[0] 次击杀（缺省 1）增加一档。
        /// 精英/领主用 Value[2]，其余用 Value[1]。进度由调用方按本局保存。
        /// </summary>
        public static float VersatilePersonKillGain(HeroConfig hero, bool eliteOrBoss, ref float progress)
        {
            var gain = 0f;
            var next = progress;
            ForEachEntry(hero, entry =>
            {
                if (entry.Type != MechanismType.VersatilePersonUp)
                {
                    return;
                }

                var unit = ValueAt(entry);
                if (unit <= 0f)
                {
                    unit = 1f;
                }

                next += 1f;
                if (next + 0.0001f < unit)
                {
                    return;
                }

                next -= unit;
                gain += eliteOrBoss ? ValueAt(entry, 2) : ValueAt(entry, 1);
            });
            progress = next;
            return gain;
        }

        /// <summary>攻击力达到词条阈值时，本次出手改为 100% 群体伤害。</summary>
        public static bool AttackBecomesFullAoe(HeroConfig hero, int attack)
        {
            var becomes = false;
            ForEachEntry(hero, entry =>
            {
                if (entry.Type == MechanismType.AoeDamageWhenAttack && attack >= ValueAt(entry))
                {
                    becomes = true;
                }
            });
            return becomes;
        }

        /// <summary>回合开始受到的伤害 = 存活敌人数 × Value[0]。没有该词条或没有敌人时为 0。</summary>
        public static int MonsterNumDamage(HeroConfig hero, int aliveEnemies)
        {
            if (aliveEnemies <= 0)
            {
                return 0;
            }

            var per = SumValue(hero, MechanismType.MonsterNumDamage);
            if (per == 0f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(aliveEnemies * per));
        }

        /// <summary>圣光燃烧在每手开始发动的次数（Value[0]）。</summary>
        public static int HolyLightRoundCasts(HeroConfig hero)
        {
            var times = 0;
            ForEachEntry(hero, entry =>
            {
                if (entry.Type != MechanismType.HolyLightBurning)
                {
                    return;
                }

                times += Math.Max(0, (int)Math.Round(ValueAt(entry)));
            });
            return times;
        }

        /// <summary>圣光燃烧：伤害比例 Value[1]，每次发动后后续伤害加成 Value[2]。</summary>
        public static bool TryHolyLight(HeroConfig hero, out float ratio, out float perCast)
        {
            var ratioValue = 0f;
            var perCastValue = 0f;
            var found = false;
            ForEachEntry(hero, entry =>
            {
                if (found || entry.Type != MechanismType.HolyLightBurning)
                {
                    return;
                }

                found = true;
                ratioValue = ValueAt(entry, 1);
                perCastValue = ValueAt(entry, 2);
            });
            ratio = ratioValue;
            perCast = perCastValue;
            return found;
        }

        /// <summary>当前这一发用发动前的层数：攻击 × 比例 × (1 + 已发动次数 × 每层加成)。</summary>
        public static int HolyLightDamage(int attack, float ratio, int casts, float perCast)
        {
            if (attack <= 0 || ratio == 0f)
            {
                return 0;
            }

            var mul = 1f + Math.Max(0, casts) * perCast;
            return Math.Max(0, (int)Math.Round(attack * ratio * mul));
        }

        /// <summary>已发动次数带来的后续出手加成。</summary>
        public static float HolyLightOutgoingPercent(HeroConfig hero, int casts)
        {
            if (casts <= 0 || !TryHolyLight(hero, out _, out var perCast))
            {
                return 0f;
            }

            return casts * perCast;
        }

        /// <summary>与遗物 <c>AttackPowerDamage</c> 相同：次数 Value[0]，倍率缺省 1、取 Value[1]。</summary>
        public static int AttackPowerDamage(HeroEntryConfig entry, int attack)
        {
            var hits = Math.Max(0, (int)Math.Round(ValueAt(entry)));
            var factor = entry?.Value != null && entry.Value.Length > 1 ? ValueAt(entry, 1) : 1f;
            if (hits <= 0 || attack <= 0)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(attack * factor * hits));
        }

        public static float SumMultiplierExtra(HeroConfig hero, bool firstShowThisStage)
        {
            return firstShowThisStage
                ? SumValue(hero, MechanismType.FirstShowCardEveryLevel)
                : 0f;
        }

        /// <summary>词条 <c>Value</c> 已是数组；缺项或越界返回 0。</summary>
        public static float ValueAt(HeroEntryConfig entry, int index = 0)
        {
            if (entry?.Value == null || index < 0 || index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
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

        /// <summary>
        /// 从 <see cref="HeroConfig.HeroEffects"/> 解析释放（Attack）与命中（Hit）各一条。
        /// 缺任一类型、资源名为空或不存在时返回 false（出刀回退近战）。
        /// </summary>
        public static bool TryResolveEffects(
            HeroConfig hero,
            out HeroEffectsConfig attack,
            out HeroEffectsConfig hit)
        {
            attack = null;
            hit = null;
            if (hero?.HeroEffects == null)
            {
                return false;
            }

            for (var i = 0; i < hero.HeroEffects.Length; i++)
            {
                var fx = HeroEffectsConfig.Get(hero.HeroEffects[i]);
                if (fx == null || string.IsNullOrEmpty(fx.Effects))
                {
                    continue;
                }

                if (fx.Type == EffectsType.Attack && attack == null)
                {
                    attack = fx;
                }
                else if (fx.Type == EffectsType.Hit && hit == null)
                {
                    hit = fx;
                }
            }

            return attack != null && hit != null;
        }
    }
}
