using System;
using CardShare.Contracts.Config;
using App.Talent;

namespace App.Game
{
    /// <summary>
    /// 按当前英雄 <see cref="HeroConfig.HeroEntryId"/> 结算局内技能；
    /// <see cref="HeroConfig.HeroEffects"/>（及弱/中/强三档）只解析出刀演出用。收藏禁用不禁用英雄词条。
    /// </summary>
    public static class HeroMechanics
    {
        /// <summary>
        /// 暗灵法师（HeroConfig.Id）。它的出刀是「齐射」，跟其他英雄的一条释放 + 一条命中不同，代码里特判：
        /// CastEffect=Aoshufeidan01 持续 1.5s、HeroEffects 主弹 02 蓄力 1s 后飞向主目标 0.3s、
        /// SpecialEffect 两侧各飞一发 03（0.4s DOJump 抛物线）、所有命中点都出 04。
        /// 时间与抛物线见 <c>AttackTuning.asset</c>「暗灵法师齐射」段。
        /// </summary>
        public const int ShadowMageHeroId = 3;

        /// <summary>圣骑士（HeroConfig.Id）。圣光燃烧读 <see cref="HeroConfig.SpecialEffect"/>；对子先圣光再近战，非对子只近战。</summary>
        public const int PaladinHeroId = 8;

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

        /// <summary>
        /// 当前这一发用发动前的层数：攻击 × (基础比例 + 已发动次数 × 每次加点)。
        /// 例：0.5 / 0.1 → 第 1 发 50%、第 2 发 60%、第 3 发 70%。
        /// </summary>
        public static int HolyLightDamage(int attack, float ratio, int casts, float perCast)
        {
            if (attack <= 0)
            {
                return 0;
            }

            var percent = ratio + Math.Max(0, casts) * perCast;
            if (percent <= 0f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(attack * percent));
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
        /// 按档位取这一刀用哪组英雄特效 Id：1 弱 / 2 中 / 3 强。
        /// 该档没配（空数组）时回退到 <see cref="HeroConfig.HeroEffects"/>，老数据不用改。
        /// </summary>
        public static int[] HeroEffectsForLevel(HeroConfig hero, int level)
        {
            if (hero == null)
            {
                return null;
            }

            var tier = level >= 3
                ? hero.HeroEffectsStrong
                : level == 2 ? hero.HeroEffectsMid : hero.HeroEffectsWeak;
            return tier != null && tier.Length > 0 ? tier : hero.HeroEffects;
        }

        /// <summary>
        /// 按目标类型取整套特效 Id：普通怪用 <see cref="HeroConfig.HeroEffectsCommon"/>，
        /// 精英/领主用 <see cref="HeroConfig.HeroEffectsElite"/>；没配返回 null，交给档位/默认档。
        /// </summary>
        public static int[] HeroEffectsForTarget(HeroConfig hero, bool eliteTarget)
        {
            if (hero == null)
            {
                return null;
            }

            var ids = eliteTarget ? hero.HeroEffectsElite : hero.HeroEffectsCommon;
            return ids != null && ids.Length > 0 ? ids : null;
        }

        /// <summary>
        /// 按目标类型 + 档位解析整套特效：目标类型（普通/精英）优先，其次弱/中/强档位，最后默认档。
        /// 任一层凑不齐 Attack + Hit 就往下落，全落空返回 false（出刀回退近战）。
        /// </summary>
        public static bool TryResolveEffects(
            HeroConfig hero,
            int level,
            bool eliteTarget,
            out HeroEffectsConfig attack,
            out HeroEffectsConfig hit)
        {
            if (TryResolveEffects(HeroEffectsForTarget(hero, eliteTarget), out attack, out hit))
            {
                return true;
            }

            return TryResolveEffects(hero, level, out attack, out hit);
        }

        /// <summary>
        /// 按档位解析释放（Attack）与命中（Hit）各一条；该档缺一条时退回默认 <see cref="HeroConfig.HeroEffects"/>。
        /// 仍缺任一类型、资源名为空或不存在时返回 false（出刀回退近战）。
        /// </summary>
        public static bool TryResolveEffects(
            HeroConfig hero,
            int level,
            out HeroEffectsConfig attack,
            out HeroEffectsConfig hit)
        {
            if (TryResolveEffects(HeroEffectsForLevel(hero, level), out attack, out hit))
            {
                return true;
            }

            return hero != null &&
                   !ReferenceEquals(HeroEffectsForLevel(hero, level), hero.HeroEffects) &&
                   TryResolveEffects(hero.HeroEffects, out attack, out hit);
        }

        /// <summary>目标类型、档位、默认档、SpecialEffect 里只要有一组能解析出成套特效就返回 true（进桌预载自检用）。</summary>
        public static bool TryResolveAnyEffects(
            HeroConfig hero,
            out HeroEffectsConfig attack,
            out HeroEffectsConfig hit)
        {
            for (var elite = 0; elite < 2; elite++)
            {
                for (var level = 1; level <= 3; level++)
                {
                    if (TryResolveEffects(hero, level, elite == 1, out attack, out hit))
                    {
                        return true;
                    }
                }
            }

            if (TryResolveSpecialEffects(hero, out attack, out hit))
            {
                return true;
            }

            attack = null;
            hit = null;
            return false;
        }

        /// <summary>
        /// 解析 <see cref="HeroConfig.SpecialEffect"/> 里第一条 Attack + 第一条 Hit。
        /// 圣骑士：圣光燃烧专用；暗灵法师的 SpecialEffect 仍只参与齐射解析。
        /// </summary>
        public static bool TryResolveSpecialEffects(
            HeroConfig hero,
            out HeroEffectsConfig attack,
            out HeroEffectsConfig hit)
        {
            return TryResolveEffects(hero?.SpecialEffect, out attack, out hit);
        }

        /// <summary>
        /// 暗灵法师的齐射套件（01 持续 / 02 主弹 / 03 溅射弹 / 04 受击）。
        /// 表结构：<see cref="HeroConfig.CastEffect"/> = 01 法阵，
        /// <see cref="HeroConfig.HeroEffects"/> = 02 主弹 + 04 受击，
        /// <see cref="HeroConfig.SpecialEffect"/> = 03 溅射弹（可再带一份 04）。
        /// 兼容旧数据：四个 Id 全塞在 <c>HeroEffects</c> 里也能凑齐。
        /// 别的英雄一律返回 false。档位（弱/中/强）对这套不生效。
        /// </summary>
        public static bool TryResolveVolleyEffects(
            HeroConfig hero,
            out HeroEffectsConfig channel,
            out HeroEffectsConfig main,
            out HeroEffectsConfig splash,
            out HeroEffectsConfig hit)
        {
            channel = null;
            main = null;
            splash = null;
            hit = null;
            if (hero == null || hero.Id != ShadowMageHeroId)
            {
                return false;
            }

            // CastEffect：施法法阵（非投射物 Attack）。
            AbsorbVolleyFx(HeroEffectsConfig.Get(hero.CastEffect), ref channel, ref main, ref splash, ref hit);
            // HeroEffects：主弹 + 受击（旧表也可能含 01/03）。
            AbsorbVolleyIds(hero.HeroEffects, ref channel, ref main, ref splash, ref hit);
            // SpecialEffect：溅射弹（及可选的受击）。
            AbsorbVolleyIds(hero.SpecialEffect, ref channel, ref main, ref splash, ref hit);

            return channel != null && main != null && splash != null && hit != null;
        }

        private static void AbsorbVolleyIds(
            int[] ids,
            ref HeroEffectsConfig channel,
            ref HeroEffectsConfig main,
            ref HeroEffectsConfig splash,
            ref HeroEffectsConfig hit)
        {
            if (ids == null)
            {
                return;
            }

            for (var i = 0; i < ids.Length; i++)
            {
                AbsorbVolleyFx(HeroEffectsConfig.Get(ids[i]), ref channel, ref main, ref splash, ref hit);
            }
        }

        private static void AbsorbVolleyFx(
            HeroEffectsConfig fx,
            ref HeroEffectsConfig channel,
            ref HeroEffectsConfig main,
            ref HeroEffectsConfig splash,
            ref HeroEffectsConfig hit)
        {
            if (fx == null || string.IsNullOrEmpty(fx.Effects))
            {
                return;
            }

            if (fx.Type == EffectsType.Hit)
            {
                if (hit == null)
                {
                    hit = fx;
                }

                return;
            }

            if (fx.Type != EffectsType.Attack)
            {
                return;
            }

            if (fx.IsProjectile == 0)
            {
                if (channel == null)
                {
                    channel = fx;
                }

                return;
            }

            // 投射物：先主弹、再溅射；SpecialEffect 里若主弹已有，自然落到溅射槽。
            if (main == null)
            {
                main = fx;
            }
            else if (splash == null)
            {
                splash = fx;
            }
        }

        /// <summary>从一组 HeroEffectsConfig Id 里取第一条 Attack 和第一条 Hit。</summary>
        private static bool TryResolveEffects(
            int[] effectIds,
            out HeroEffectsConfig attack,
            out HeroEffectsConfig hit)
        {
            attack = null;
            hit = null;
            if (effectIds == null)
            {
                return false;
            }

            for (var i = 0; i < effectIds.Length; i++)
            {
                var fx = HeroEffectsConfig.Get(effectIds[i]);
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
