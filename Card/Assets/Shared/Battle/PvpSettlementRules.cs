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

        /// <summary>未配置 PVPGoldInterestNum 时：每 100 金币给 10 利息。</summary>
        public const int InterestGoldStep = 100;

        public const int InterestPerStep = 10;

        /// <summary>未配置 PVPGoldInterestNumMax 时，利息按所持 500 封顶。</summary>
        public const int InterestGoldCap = 500;

        private static readonly int[] DefaultStreakGold = { 20, 40, 60, 80, 100 };

        /// <summary>未用技能换算金币单价（GameConst.EverySkillProvideGold，缺省 20）。</summary>
        public static int SkillGoldUnit(GameConst gameConst)
            => gameConst.EverySkillProvideGold > 0 ? gameConst.EverySkillProvideGold : 20;

        /// <summary>本桌结算伤害按 GameConst.DamageTurnToGold [伤害, 金币] 换金，向下取整；无效配置不发放。</summary>
        public static int DamageGold(int damage, GameConst gameConst)
        {
            var rate = gameConst?.DamageTurnToGold;
            if (damage <= 0 || rate == null || rate.Length < 2 || rate[0] <= 0 || rate[1] <= 0)
            {
                return 0;
            }

            return (int)Math.Min(int.MaxValue, (long)damage * rate[1] / rate[0]);
        }

        /// <summary>
        /// 单人本轮金币。胜者 = 基础 + 单价×未用技能 + 连胜奖励 + 利息；
        /// 败者 =（基础 + 单价×未用技能）/ 2 + 连败奖励 + 利息。
        /// 连胜/连败与利息都不参与半额。平局不结算（调用方别调）。heldGold 用发奖前持有量。
        /// </summary>
        public static int RoundGold(int goldBase, int skillGoldUnit, int unusedSkills, int streak, int heldGold, bool winner)
            => RoundGold(goldBase, skillGoldUnit, unusedSkills, streak, heldGold, winner, null!);

        /// <summary>
        /// 单人本轮金币。连胜/连败读 PVPWinningStreakGoldNum，利息读 PVPGoldInterestNum 与 PVPGoldInterestNumMax。
        /// </summary>
        public static int RoundGold(int goldBase, int skillGoldUnit, int unusedSkills, int streak, int heldGold, bool winner, GameConst gameConst)
        {
            var core = Math.Max(0, goldBase) + Math.Max(0, skillGoldUnit) * Math.Max(0, unusedSkills);
            if (!winner)
            {
                core /= 2;
            }

            return core + StreakGold(streak, gameConst) + InterestGold(heldGold, gameConst);
        }

        /// <summary>连胜或连败奖励。1 轮为 0；从 2 轮起按下标读表，超出末档用最后一档。野怪轮传 0。未配置时 20/40/60/80/100。</summary>
        public static int StreakGold(int streak)
            => StreakGold(streak, null!);

        public static int StreakGold(int streak, GameConst gameConst)
        {
            if (streak < 2)
            {
                return 0;
            }

            var table = gameConst != null && gameConst.PVPWinningStreakGoldNum != null && gameConst.PVPWinningStreakGoldNum.Length > 0
                ? gameConst.PVPWinningStreakGoldNum
                : DefaultStreakGold;
            var index = streak - 2;
            if (index >= table.Length)
            {
                index = table.Length - 1;
            }

            var gold = table[index];
            return gold > 0 ? gold : 0;
        }

        /// <summary>利息 = floor(min(所持, 峰值) / 每档金币) × 每档利息。未配置时每 100 给 10，所持 500 封顶。</summary>
        public static int InterestGold(int heldGold)
            => InterestGold(heldGold, null!);

        public static int InterestGold(int heldGold, GameConst gameConst)
        {
            if (heldGold <= 0)
            {
                return 0;
            }

            var step = InterestGoldStep;
            var per = InterestPerStep;
            var pair = gameConst != null ? gameConst.PVPGoldInterestNum : null;
            if (pair != null && pair.Length >= 2 && pair[0] > 0 && pair[1] > 0)
            {
                step = pair[0];
                per = pair[1];
            }

            var cap = gameConst != null && gameConst.PVPGoldInterestNumMax > 0
                ? gameConst.PVPGoldInterestNumMax
                : InterestGoldCap;
            var capped = heldGold > cap ? cap : heldGold;
            return capped / step * per;
        }

        /// <summary>野怪轮胜方回血：10% 最大生命，至少 1，上限由调用方 clamp。</summary>
        public static int MonsterWinHeal(int maxHp)
            => Math.Max(1, (int)Math.Floor(maxHp * 0.1));

        /// <summary>未配置 RankScore 时的默认局外分数：1~4 名 +50/+25/+0/-40。</summary>
        private static readonly int[] DefaultRankScore = { 50, 25, 0, -40 };

        /// <summary>名次奖励：rank 从 1 起，越界或未配置取 0。</summary>
        public static int RewardForRank(int rank, int[]? rewards)
            => rank >= 1 && rewards != null && rank <= rewards.Length ? rewards[rank - 1] : 0;

        /// <summary>名次 PVP 分数加减：rank 从 1 起；未配 RankScore 用默认表；可为负。</summary>
        public static int ScoreForRank(int rank, int[]? scores)
            => RewardForRank(rank, scores != null && scores.Length > 0 ? scores : DefaultRankScore);

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
