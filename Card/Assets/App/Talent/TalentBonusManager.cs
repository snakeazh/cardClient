using System;
using App.Config;
using CardShare.Contracts.Config;

namespace App.Talent
{
    /// <summary>
    /// 英雄面板属性：配置底值 + 英雄词条 + 天赋加成。
    /// 局外选角展示与局内开局共用；圣物、关卡与对局成长由对局另行叠加。
    /// </summary>
    public readonly struct HeroPanelStats
    {
        public HeroPanelStats(int attack, float critRate, float dodgeRate, int hp)
        {
            Attack = Math.Max(0, attack);
            CritRate = Math.Max(0f, critRate);
            DodgeRate = Math.Max(0f, dodgeRate);
            Hp = Math.Max(0, hp);
        }

        public int Attack { get; }

        public float CritRate { get; }

        public float DodgeRate { get; }

        public int Hp { get; }
    }

    /// <summary>
    /// 天赋对英雄面板属性的统一加成。局内开局与局外选角走同一套计算。
    /// </summary>
    public sealed class TalentBonusManager
    {
        private readonly ITalentService _talent;

        public TalentBonusManager(ITalentService talent)
        {
            _talent = talent;
        }

        /// <summary>已拥有天赋提供的基础攻击力加成。</summary>
        public int AttackBonus => Round(Sum(MechanismType.HeroAttack));

        /// <summary>已拥有天赋提供的暴击率加成。</summary>
        public float CritRateBonus => Sum(MechanismType.HeroCritical);

        /// <summary>已拥有天赋提供的闪避率加成。</summary>
        public float DodgeRateBonus => Sum(MechanismType.MissDamagePer);

        /// <summary>已拥有天赋提供的生命上限加成。</summary>
        public int HpBonus => Round(Sum(MechanismType.HeroHpMax));

        /// <summary>无天赋服务时只结算英雄配置与词条。</summary>
        public static HeroPanelStats EvaluateBase(HeroConfig hero)
        {
            return Evaluate(hero, attackBonus: 0, critBonus: 0f, dodgeBonus: 0f, hpBonus: 0);
        }

        /// <summary>英雄面板最终值 = 配置 + 英雄词条 + 当前已拥有天赋。加成一次遍历算齐，避免每项各调一次 GetOwned。</summary>
        public HeroPanelStats Evaluate(HeroConfig hero)
        {
            var attackBonus = 0f;
            var critBonus = 0f;
            var dodgeBonus = 0f;
            var hpBonus = 0f;
            var owned = _talent?.GetOwned();
            if (owned != null)
            {
                for (var i = 0; i < owned.Count; i++)
                {
                    var snapshot = owned[i];
                    if (snapshot == null || !snapshot.IsOwned || snapshot.Entry == null)
                    {
                        continue;
                    }

                    switch (snapshot.Entry.Type)
                    {
                        case MechanismType.HeroAttack:
                            attackBonus += snapshot.EffectiveValue;
                            break;
                        case MechanismType.HeroCritical:
                            critBonus += snapshot.EffectiveValue;
                            break;
                        case MechanismType.MissDamagePer:
                            dodgeBonus += snapshot.EffectiveValue;
                            break;
                        case MechanismType.HeroHpMax:
                            hpBonus += snapshot.EffectiveValue;
                            break;
                    }
                }
            }

            return Evaluate(hero, Round(attackBonus), critBonus, dodgeBonus, Round(hpBonus));
        }

        public static string FormatRate(float rate)
        {
            return (rate * 100f).ToString("0.##") + "%";
        }

        public static string FormatPanelLines(HeroPanelStats stats)
        {
            return "攻击力：" + stats.Attack + "\n"
                + "暴击率：" + FormatRate(stats.CritRate) + "\n"
                + "闪避率：" + FormatRate(stats.DodgeRate) + "\n"
                + "生命值：" + stats.Hp;
        }

        /// <summary>描述后空一行再拼面板属性；无描述则只显示属性。</summary>
        public static string AppendPanelLines(string desc, HeroPanelStats stats)
        {
            var lines = FormatPanelLines(stats);
            return string.IsNullOrEmpty(desc) ? lines : desc + "\n\n" + lines;
        }

        private static HeroPanelStats Evaluate(
            HeroConfig hero,
            int attackBonus,
            float critBonus,
            float dodgeBonus,
            int hpBonus)
        {
            var attack = (hero != null ? Math.Max(0, hero.HeroDamage) : 0) + attackBonus;
            var crit = (hero != null ? hero.Critical : 0f)
                + SumHeroEntry(hero, MechanismType.HeroCritical)
                + critBonus;
            var dodge = SumHeroEntry(hero, MechanismType.MissDamagePer) + dodgeBonus;
            var hp = (hero != null ? Math.Max(0, hero.Hp) : 0) + hpBonus;
            return new HeroPanelStats(attack, crit, dodge, hp);
        }

        private float Sum(MechanismType type)
        {
            if (_talent == null)
            {
                return 0f;
            }

            var sum = 0f;
            var owned = _talent.GetOwned();
            for (var i = 0; i < owned.Count; i++)
            {
                var snapshot = owned[i];
                if (snapshot == null || !snapshot.IsOwned || snapshot.Entry == null)
                {
                    continue;
                }

                if (snapshot.Entry.Type == type)
                {
                    sum += snapshot.EffectiveValue;
                }
            }

            return sum;
        }

        private static float SumHeroEntry(HeroConfig hero, MechanismType type)
        {
            if (hero?.HeroEntryId == null)
            {
                return 0f;
            }

            var sum = 0f;
            for (var i = 0; i < hero.HeroEntryId.Length; i++)
            {
                var entry = HeroEntryConfig.Get(hero.HeroEntryId[i]);
                if (entry != null && entry.Type == type)
                {
                    sum += entry.Value;
                }
            }

            return sum;
        }

        private static int Round(float value)
        {
            return (int)Math.Round(value);
        }
    }
}
