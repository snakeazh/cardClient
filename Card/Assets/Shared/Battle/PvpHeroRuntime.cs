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
    /// - Damage：出牌伤害百分比，挂在 <see cref="CombatBonuses.BuildPlayerInput"/>（PVE GameSession.ComputeAttackDamage）。
    /// - HeroCritical：面板暴击率，挂在 <see cref="CombatBonuses.EvaluatePanel"/>。
    /// - RubbingCardsNum / PerspectiveNum：技能次数，挂在 <see cref="CombatBonuses.SumSkillCountBonus"/>。
    /// - ExtraAttackOneTime：追击概率，与天赋 ProOfExtraAttack 求和后进 <see cref="CombatDamageInput.ExtraAttackChance"/> 掷一次。
    /// - InitialFunds：开局金币，<see cref="InitialGold"/>（PVP 无天赋侧，基础 + 英雄）。
    /// - RelicPricePer：商店购买折扣，<see cref="BuyPrice"/>（PvpShopRules.BuyPrice / 货架投影同源）。
    /// - EpicLegendRelicProUp：史诗/传说上架权重，<see cref="ShopWeight"/>（PVE PickWeightedRelic 同源口径）。
    /// - GetGoldAfterLevel：回合胜方基础金币加成，<see cref="SettlementGold"/>（PVE GrantStageGold 只乘配表基础金）。
    /// - HeroTakeDamagePer：承伤百分比减免，<see cref="MitigateIncomingDamage"/>。
    /// - MissDamagePer：闪避，<see cref="RollDodge"/>；MissGetDamage：闪避反击，<see cref="DodgeCounterDamage"/>。
    /// - BloodSucking：胜方按实际伤害回血，<see cref="BloodSuckingHeal"/>（PVE 只实现圣物侧，英雄侧由 PVP 补上）。
    ///
    /// PVP 不支持的词条（配了也不生效，接入前先确认 PVE 口径）：
    /// - PVE 英雄侧本就是死代码/未实现：AttackBossDamage、ManyMonsterDamage、OneMonsterDamage、HpUnderDamage
    ///   （HeroMechanics.SumDamagePercent 无调用方）、FirstShowCardEveryLevel（PVE 伤害公式只加天赋侧，英雄侧仅进预览倍率）、
    ///   KillingGetGold、KillingGetAttack、UnableReplyHp、GainDamageUpWhenHpDecreases、UnableCritical、
    ///   RelicReplyHp、HolyLightBurning、UnableMissing、UnableReplyGetGold、MonsterNumDamage、
    ///   HeroHpReplyEveryLevelEnding、RelicNumMax、KillingProbabilityTen（以上 PVE 只读天赋侧或未读英雄侧）。
    /// - PVP 对局无对应概念：AoeDamage、VersatilePerson、VersatilePersonUp、AoeDamageWhenAttack
    ///   （PVP 是 1v1 比牌，无溅射/多目标；野怪轮也是单目标）。
    ///
    /// 新英雄接入口径：只复用上表"支持"栏已有 MechanismType 时纯配表即生效；
    /// 新增 MechanismType 需在对应钩子加一行求和，并在本注释与 README 的支持/不支持清单同步登记。
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

        /// <summary>闪避掷骰：MissDamagePer 求和后一次判定，命中则本次伤害为 0。对齐 PVE 面板 DodgeRate 的英雄侧。</summary>
        public static bool RollDodge(IGameTables tables, int heroId, Random rng)
        {
            var chance = SumValue(tables, heroId, MechanismType.MissDamagePer);
            if (chance <= 0f)
            {
                return false;
            }

            return rng.NextDouble() < chance;
        }

        /// <summary>闪避反击：逐条 MissGetDamage 词条 dmg = round(防守方攻击 × Value[0] + Value[1]) 求和。
        /// PVE 英雄侧未实现（ApplyDodgeCounter 只遍历圣物词条），此为 PVP 新口径。</summary>
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

                var dmg = (int)Math.Round(attack * ValueAt(entry) + ValueAt(entry, 1));
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
        /// 对齐 PVE GrantStageGold：只乘配表基础金，击杀/技能/伤害换算部分不放大（PVP 由 WinnerGold 另行相加）。</summary>
        public static int SettlementGold(IGameTables tables, int heroId, int baseGold)
        {
            var per = SumValue(tables, heroId, MechanismType.GetGoldAfterLevel);
            if (per == 0f)
            {
                return Math.Max(0, baseGold);
            }

            return Math.Max(0, (int)Math.Round(baseGold * (1f + per)));
        }
    }
}
