using System;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// PVP 英雄词条钩子层：集中读取 <see cref="HeroConfig.HeroEntryId"/> 词条并提供各结算口径的钩子方法，
    /// 命名与语义对齐 PVE <c>App.Game.HeroMechanics</c>（SumValue / ValueAt / HasMechanism / ForEachEntry / BuyPrice / Roll），
    /// 配表只走 <see cref="IGameTables"/>。门控/集中读取风格对齐 <see cref="PvpRelicRuntime"/>。
    ///
    /// PVP 支持的英雄词条（MechanismType）：
    /// - Damage、掉血加伤、圣光层数、精英/领主 AttackBossDamage：出牌伤害百分比，挂在 <see cref="CombatBonuses.BuildPlayerInput"/>。
    /// - HeroCritical / UnableCritical：面板暴击率，挂在 <see cref="CombatBonuses.EvaluatePanel"/>。无法暴击在天赋之后归零。
    /// - RubbingCardsNum / PerspectiveNum：技能次数，挂在 <see cref="CombatBonuses.SumSkillCountBonus"/>。
    /// - ExtraAttackOneTime：追击概率，与天赋 ProOfExtraAttack 求和后进 <see cref="CombatDamageInput.ExtraAttackChance"/> 掷一次。
    /// - InitialFunds：开局金币，<see cref="InitialGold"/>（PVP 无天赋侧，基础 + 英雄）。
    /// - RelicPricePer：商店购买折扣，<see cref="BuyPrice"/>。
    /// - EpicLegendRelicProUp：史诗/传说上架权重，<see cref="ShopWeight"/>。
    /// - GetGoldAfterLevel：双方轮次基础金币加成，<see cref="SettlementGold"/>。
    /// - HeroTakeDamagePer：承伤百分比，<see cref="MitigateIncomingDamage"/>。
    /// - MissDamagePer / UnableMissing：闪避，<see cref="RollDodge"/>。无法闪避时不掷骰。
    /// - MissGetDamage：闪避反击，<see cref="DodgeCounterDamage"/>。次数 Value[0] × 倍率 Value[1]。
    /// - BloodSucking：胜方按实际伤害回血，<see cref="BloodSuckingHeal"/>。不受无法圣物回血影响。
    /// - UnableReplyHp / RelicReplyHp：圣物治疗走 <see cref="ScaleRelicHeal"/>。
    /// - KillingGetGold / KillingGetAttack：击杀玩家或把野怪血量打到 0。
    /// - HolyLightBurning：回合开打前按次数打本轮对手；对子获胜后再打一次。
    /// - MonsterNumDamage：开打前按本轮对手数受伤，1v1 算 1 个。走承伤，不闪避。
    /// - AoeDamage：唯一目标改为 round(伤害 × 比例)。攻击达阈值变群体时 1v1 仍是 100%。
    ///
    /// 1v1 没有第二个目标，分裂和分裂成长不改变主目标伤害。
    /// PVP 没有圣物加金币结算，UnableReplyGetGold 只留 <see cref="BlocksRelicGold"/>，不拦轮次金币和卖圣物。
    /// </summary>
    public static class PvpHeroRuntime
    {
        /// <summary>按 heroId 解析英雄配置；未配置返回 false（不做默认英雄兜底，词条只认真实佩戴的英雄）。</summary>
        public static bool TryResolve(IGameTables tables, int heroId, out HeroConfig hero)
        {
            return tables.TryGetHero(heroId, out hero);
        }

        public static float SumValue(IGameTables tables, int heroId, MechanismType type)
        {
            return TryResolve(tables, heroId, out var hero) ? SumValue(tables, hero, type) : 0f;
        }

        /// <summary>同类型词条 Value[0] 求和，同 PVE HeroMechanics.SumValue。</summary>
        public static float SumValue(IGameTables tables, HeroConfig hero, MechanismType type)
        {
            var sum = 0f;
            ForEachEntry(tables, hero, entry =>
            {
                if (entry.Type == type)
                {
                    sum += ValueAt(entry);
                }
            });
            return sum;
        }

        /// <summary>词条 <c>Value</c> 已是数组；缺项或越界返回 0，同 PVE HeroMechanics.ValueAt。</summary>
        public static float ValueAt(HeroEntryConfig entry, int index = 0)
        {
            if (entry.Value == null || index < 0 || index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
        }

        public static bool HasMechanism(IGameTables tables, int heroId, MechanismType type)
        {
            var found = false;
            if (TryResolve(tables, heroId, out var hero))
            {
                ForEachEntry(tables, hero, entry =>
                {
                    if (entry.Type == type)
                    {
                        found = true;
                    }
                });
            }

            return found;
        }

        public static void ForEachEntry(IGameTables tables, HeroConfig hero, Action<HeroEntryConfig> action)
        {
            if (hero.HeroEntryId == null)
            {
                return;
            }

            for (var i = 0; i < hero.HeroEntryId.Length; i++)
            {
                if (tables.TryGetHeroEntry(hero.HeroEntryId[i], out var entry))
                {
                    action(entry);
                }
            }
        }

        /// <summary>开局金币加成（取整，负值夹 0）：对齐 PVE GameSession.StartNewRun 的英雄侧（PVP 无天赋侧）。</summary>
        public static int InitialGold(IGameTables tables, int heroId)
        {
            return Math.Max(0, (int)Math.Round(SumValue(tables, heroId, MechanismType.InitialFunds)));
        }

        /// <summary>购买价 = round(Price × (1 + RelicPricePer))，下限 0；不改售价。对齐 PVE HeroMechanics.BuyPrice。</summary>
        public static int BuyPrice(IGameTables tables, int heroId, RelicConfig relic)
        {
            var per = SumValue(tables, heroId, MechanismType.RelicPricePer);
            if (per == 0f)
            {
                return Math.Max(0, relic.Price);
            }

            return Math.Max(0, (int)Math.Round(relic.Price * (1f + per)));
        }

        /// <summary>商店上架权重：史诗/传说 × (1 + EpicLegendRelicProUp)，对齐 PVE HeroMechanics.ShopWeight。</summary>
        public static float ShopWeight(IGameTables tables, int heroId, RelicConfig relic)
        {
            var weight = Math.Max(0f, relic.RefreshProbability);
            if (weight <= 0f)
            {
                return 0f;
            }

            if (relic.Type == QualityType.Epic || relic.Type == QualityType.Legend)
            {
                weight *= 1f + SumValue(tables, heroId, MechanismType.EpicLegendRelicProUp);
            }

            return Math.Max(0f, weight);
        }

        /// <summary>英雄追击概率（ExtraAttackOneTime 求和）。调用方与天赋 ProOfExtraAttack 相加后掷一次，
        /// 对齐 PVE HeroMechanics.RollCombined 的"求和后掷一次"语义。</summary>
        public static float ExtraAttackChance(IGameTables tables, HeroConfig hero)
        {
            return SumValue(tables, hero, MechanismType.ExtraAttackOneTime);
        }

        /// <summary>承伤减免：damage × (1 + HeroTakeDamagePer) 四舍五入、下限 0；大于 0 时保底 1。
        /// 对齐 PVE GameSession.IncomingDamageAfterMitigation 的英雄侧（PVP 无天赋/圣物承伤通道）。</summary>
        public static int MitigateIncomingDamage(IGameTables tables, int heroId, int damage)
        {
            var per = SumValue(tables, heroId, MechanismType.HeroTakeDamagePer);
            if (per != 0f)
            {
                damage = Math.Max(0, (int)Math.Round(damage * (1f + per)));
            }

            return damage <= 0 ? 0 : Math.Max(1, damage);
        }

        /// <summary>闪避掷骰：无法闪避时直接失败。否则 MissDamagePer 求和后一次判定，命中则本次伤害为 0。</summary>
        public static bool RollDodge(IGameTables tables, int heroId, Random rng)
        {
            if (HasMechanism(tables, heroId, MechanismType.UnableMissing))
            {
                return false;
            }

            var chance = SumValue(tables, heroId, MechanismType.MissDamagePer);
            if (chance <= 0f)
            {
                return false;
            }

            return rng.NextDouble() < chance;
        }

        /// <summary>闪避反击：次数 Value[0]，倍率 Value[1]（缺省 1）。伤害 = round(攻击 × 倍率 × 次数)。</summary>
        public static int DodgeCounterDamage(IGameTables tables, int heroId, int attack)
        {
            var total = 0;
            if (!TryResolve(tables, heroId, out var hero))
            {
                return 0;
            }

            ForEachEntry(tables, hero, entry =>
            {
                if (entry.Type != MechanismType.MissGetDamage)
                {
                    return;
                }

                var hits = Math.Max(0, (int)Math.Round(ValueAt(entry)));
                var factor = entry.Value != null && entry.Value.Length > 1 ? ValueAt(entry, 1) : 1f;
                if (hits <= 0 || attack <= 0)
                {
                    return;
                }

                var dmg = (int)Math.Round(attack * factor * hits);
                if (dmg > 0)
                {
                    total += dmg;
                }
            });
            return total;
        }

        /// <summary>吸血回复量 = round(实际伤害 × BloodSucking)，下限 0；夹到血量上限由调用方做。
        /// PVE ApplyBloodSucking 只算圣物侧，英雄侧由 PVP 补上。</summary>
        public static int BloodSuckingHeal(IGameTables tables, int heroId, int dealt)
        {
            if (dealt <= 0)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(dealt * SumValue(tables, heroId, MechanismType.BloodSucking)));
        }

        /// <summary>回合结算基础金币 = round(baseGold × (1 + GetGoldAfterLevel))，下限 0。
        /// 只乘配表基础金；未用技能、连胜/连败、利息由 PvpSettlementRules.RoundGold 另行相加。</summary>
        public static int SettlementGold(IGameTables tables, int heroId, int baseGold)
        {
            var per = SumValue(tables, heroId, MechanismType.GetGoldAfterLevel);
            if (per == 0f)
            {
                return Math.Max(0, baseGold);
            }

            return Math.Max(0, (int)Math.Round(baseGold * (1f + per)));
        }

        /// <summary>掉血加伤：floor(已损失生命 / Value[0]) 层，每层 Value[1]，上限 Value[2]。</summary>
        public static float LostHpDamagePercent(IGameTables tables, int heroId, int hp, int maxHp)
        {
            if (!TryResolve(tables, heroId, out var hero))
            {
                return 0f;
            }

            var lost = Math.Max(0, maxHp - hp);
            var percent = 0f;
            ForEachEntry(tables, hero, entry =>
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

        /// <summary>英雄 AttackBossDamage：精英和领主都生效。天赋侧仍只看领主。</summary>
        public static float AttackBossDamagePercent(IGameTables tables, int heroId, MonsterType monsterType)
        {
            if (monsterType != MonsterType.Elite && monsterType != MonsterType.Boss)
            {
                return 0f;
            }

            return SumValue(tables, heroId, MechanismType.AttackBossDamage);
        }

        /// <summary>
        /// 群体伤害替换本次出手。有 AoeDamage 时按该比例打唯一目标；
        /// 攻击达到阈值变群体时 1v1 仍是 100%，比例为 0 表示不替换。
        /// </summary>
        public static float AoeDamageRatio(IGameTables tables, int heroId, int attack)
        {
            var ratio = SumValue(tables, heroId, MechanismType.AoeDamage);
            if (ratio > 0f)
            {
                return ratio;
            }

            return AttackBecomesFullAoe(tables, heroId, attack) ? 1f : 0f;
        }

        public static int ScaleAoeDamage(IGameTables tables, int heroId, int attack, int damage)
        {
            if (damage <= 0)
            {
                return damage;
            }

            var ratio = AoeDamageRatio(tables, heroId, attack);
            if (ratio <= 0f || ratio == 1f)
            {
                return damage;
            }

            return Math.Max(0, (int)Math.Round(damage * ratio));
        }

        /// <summary>圣物治疗：无法通过圣物回血则为 0，否则乘 (1 + RelicReplyHp)。</summary>
        public static int ScaleRelicHeal(IGameTables tables, int heroId, int amount)
        {
            if (amount <= 0 || HasMechanism(tables, heroId, MechanismType.UnableReplyHp))
            {
                return 0;
            }

            var per = SumValue(tables, heroId, MechanismType.RelicReplyHp);
            if (per != 0f)
            {
                amount = (int)Math.Round(amount * (1f + per));
            }

            return Math.Max(0, amount);
        }

        /// <summary>PVP 目前没有圣物加金币的结算。轮次金币和卖圣物不走这里。</summary>
        public static bool BlocksRelicGold(IGameTables tables, int heroId)
        {
            return HasMechanism(tables, heroId, MechanismType.UnableReplyGetGold);
        }

        public static int KillGold(IGameTables tables, int heroId)
        {
            return (int)Math.Round(SumValue(tables, heroId, MechanismType.KillingGetGold));
        }

        public static int KillAttack(IGameTables tables, int heroId)
        {
            return (int)Math.Round(SumValue(tables, heroId, MechanismType.KillingGetAttack));
        }

        /// <summary>回合开始受伤 = 存活敌人数 × Value[0]。1v1 的本轮对手算 1 个。</summary>
        public static int MonsterNumChip(IGameTables tables, int heroId, int aliveEnemies)
        {
            if (aliveEnemies <= 0)
            {
                return 0;
            }

            var per = SumValue(tables, heroId, MechanismType.MonsterNumDamage);
            if (per == 0f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(aliveEnemies * per));
        }

        public static int HolyLightRoundCasts(IGameTables tables, int heroId)
        {
            if (!TryResolve(tables, heroId, out var hero))
            {
                return 0;
            }

            var times = 0;
            ForEachEntry(tables, hero, entry =>
            {
                if (entry.Type != MechanismType.HolyLightBurning)
                {
                    return;
                }

                times += Math.Max(0, (int)Math.Round(ValueAt(entry)));
            });
            return times;
        }

        public static bool TryHolyLight(IGameTables tables, int heroId, out float ratio, out float perCast)
        {
            ratio = 0f;
            perCast = 0f;
            if (!TryResolve(tables, heroId, out var hero))
            {
                return false;
            }

            var ratioValue = 0f;
            var perCastValue = 0f;
            var found = false;
            ForEachEntry(tables, hero, entry =>
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

        public static int HolyLightDamage(int attack, float ratio, int casts, float perCast)
        {
            if (attack <= 0 || ratio == 0f)
            {
                return 0;
            }

            var mul = 1f + Math.Max(0, casts) * perCast;
            return Math.Max(0, (int)Math.Round(attack * ratio * mul));
        }

        public static float HolyLightOutgoingPercent(IGameTables tables, int heroId, int casts)
        {
            if (casts <= 0 || !TryHolyLight(tables, heroId, out _, out var perCast))
            {
                return 0f;
            }

            return casts * perCast;
        }

        private static bool AttackBecomesFullAoe(IGameTables tables, int heroId, int attack)
        {
            if (!TryResolve(tables, heroId, out var hero))
            {
                return false;
            }

            var becomes = false;
            ForEachEntry(tables, hero, entry =>
            {
                if (entry.Type == MechanismType.AoeDamageWhenAttack && attack >= ValueAt(entry))
                {
                    becomes = true;
                }
            });
            return becomes;
        }
    }
}
