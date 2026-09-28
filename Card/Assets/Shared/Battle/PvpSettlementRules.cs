using System;
using System.Collections.Generic;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>PvP 结算纯计算：伤害系数、金币经济、野怪回血、淘汰名次、名次奖励。不触碰状态机，PvpMatch 负责落地副作用。</summary>
    public static class PvpSettlementRules
    {
        /// <summary>败方承伤 = 原始伤害 × (1 + 每轮系数 × 全局轮数)，下限 0。</summary>
        public static int ScaleDamage(int raw, float damageRoundScale, int round)
        {
            var damage = (int)Math.Floor(raw * (1f + damageRoundScale * round));
            return damage < 0 ? 0 : damage;
        }

        /// <summary>未用技能换算金币单价（GameConst.EverySkillProvideGold，缺省 20）。</summary>
        public static int SkillGoldUnit(GameConst gameConst)
            => gameConst.EverySkillProvideGold > 0 ? gameConst.EverySkillProvideGold : 20;

        /// <summary>胜者金币 = 本轮 GoldBase + damage/12 + 单价 × 未用技能数。</summary>
        public static int WinnerGold(int goldBase, int damage, int skillGoldUnit, int unusedSkills)
            => Math.Max(0, goldBase) + damage / 12 + skillGoldUnit * Math.Max(0, unusedSkills);

        /// <summary>负者金币 = 胜者金币半额；平局不结算（调用方别调）。</summary>
        public static int LoserGold(int winnerGold) => winnerGold / 2;

        /// <summary>野怪轮胜方回血：10% 最大生命，至少 1，上限由调用方 clamp。</summary>
        public static int MonsterWinHeal(int maxHp)
            => Math.Max(1, (int)Math.Floor(maxHp * 0.1));

        /// <summary>名次奖励：rank 从 1 起，越界或未配置取 0。</summary>
        public static int RewardForRank(int rank, int[]? rewards)
            => rank >= 1 && rewards != null && rank <= rewards.Length ? rewards[rank - 1] : 0;

        /// <summary>同轮淘汰名次：淘汰后血量更接近 0 者名次靠前，并列按座位号；返回（座位 → 名次），起始名次 = 存活数 + 1。</summary>
        public static List<KeyValuePair<int, int>> RankDeadOrder(IReadOnlyDictionary<int, int> deathHp, int aliveAfter)
        {
            var order = new List<int>(deathHp.Keys);
            order.Sort((a, b) =>
            {
                var cmp = Math.Abs(deathHp[a]).CompareTo(Math.Abs(deathHp[b]));
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            var result = new List<KeyValuePair<int, int>>(order.Count);
            var rank = aliveAfter + 1;
            for (var i = 0; i < order.Count; i++)
            {
                result.Add(new KeyValuePair<int, int>(order[i], rank++));
            }

            return result;
        }
    }
}
