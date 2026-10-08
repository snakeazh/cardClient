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

        /// <summary>利息按所持金币每 100 给 10，所持达到 500 后不再增加。</summary>
        public const int InterestGoldStep = 100;

        public const int InterestPerStep = 10;

        public const int InterestGoldCap = 500;

        /// <summary>未用技能换算金币单价（GameConst.EverySkillProvideGold，缺省 20）。</summary>
        public static int SkillGoldUnit(GameConst gameConst)
            => gameConst.EverySkillProvideGold > 0 ? gameConst.EverySkillProvideGold : 20;

        /// <summary>
        /// 单人本轮金币。胜者 = 基础 + 单价×未用技能 + 连胜奖励 + 利息；
        /// 败者 =（基础 + 单价×未用技能）/ 2 + 连败奖励 + 利息。
        /// 连胜/连败与利息都不参与半额。平局不结算（调用方别调）。heldGold 用发奖前持有量。
        /// </summary>
        public static int RoundGold(int goldBase, int skillGoldUnit, int unusedSkills, int streak, int heldGold, bool winner)
        {
            var core = Math.Max(0, goldBase) + Math.Max(0, skillGoldUnit) * Math.Max(0, unusedSkills);
            if (!winner)
            {
                core /= 2;
            }

            return core + StreakGold(streak) + InterestGold(heldGold);
        }

        /// <summary>连胜或连败奖励。1 轮为 0；2/3/4/5 为 20/40/60/80；6 轮及以上封顶 100。野怪轮传 0。</summary>
        public static int StreakGold(int streak)
        {
            if (streak >= 6)
            {
                return 100;
            }

            switch (streak)
            {
                case 2: return 20;
                case 3: return 40;
                case 4: return 60;
                case 5: return 80;
                default: return 0;
            }
        }

        /// <summary>利息 = floor(min(所持, 500) / 100) × 10。所持不足 100 或为负时为 0。</summary>
        public static int InterestGold(int heldGold)
        {
            if (heldGold <= 0)
            {
                return 0;
            }

            var capped = heldGold > InterestGoldCap ? InterestGoldCap : heldGold;
            return capped / InterestGoldStep * InterestPerStep;
        }

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
