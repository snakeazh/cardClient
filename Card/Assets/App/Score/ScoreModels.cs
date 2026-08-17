using System;
using App.Config;

namespace App.Score
{
    /// <summary>
    /// Chapter / stage / round score. Total is the chapter sum; Stage is the current stage sum;
    /// Round is this hand only.
    /// </summary>
    public sealed class ScoreSnapshot
    {
        public ScoreSnapshot(int total, int stage, int round)
        {
            Total = total;
            Stage = stage;
            Round = round;
        }

        /// <summary>总积分：本章节每一关 + 每一回合之和。</summary>
        public int Total { get; }

        /// <summary>关卡积分：当前关卡每一回合之和。</summary>
        public int Stage { get; }

        /// <summary>本轮积分：这一回合获得的积分。</summary>
        public int Round { get; }
    }

    /// <summary>HP / courage constants. Chip and gold rates come from GameConst.</summary>
    public static class ScoreBalance
    {
        public const int PlayerStartHp = 4000;

        /// <summary>满血进关时的勇气值。4000 HP → 800。</summary>
        public const int PlayerStartCourage = 800;

        /// <summary>多少筹码换 1 积分。读 GameConst.ChipsForPoints，未加载或 &lt;= 0 时按 1。</summary>
        public static int ChipsForPoints
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.ChipsForPoints <= 0)
                {
                    return 1;
                }

                return GameConst.Instance.ChipsForPoints;
            }
        }

        /// <summary>多少积分换 1 金币。读 GameConst.ExchangePointsForGoldCoins，未加载或 &lt;= 0 时按 10。</summary>
        public static int ExchangePointsForGoldCoins
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.ExchangePointsForGoldCoins <= 0)
                {
                    return 10;
                }

                return GameConst.Instance.ExchangePointsForGoldCoins;
            }
        }

        /// <summary>进关时血量 → 勇气值，向下取整。hp &lt;= 0 为 0。</summary>
        public static int HpToCourage(int hp)
        {
            if (hp <= 0)
            {
                return 0;
            }

            return hp * PlayerStartCourage / PlayerStartHp;
        }

        /// <summary>筹码 → 积分，向下取整。chips / ChipsForPoints。</summary>
        public static int ChipsToPoints(int chips)
        {
            if (chips <= 0)
            {
                return 0;
            }

            return chips / ChipsForPoints;
        }

        /// <summary>积分 → 金币，向下取整。points / ExchangePointsForGoldCoins。</summary>
        public static int PointsToGold(int points)
        {
            if (points <= 0)
            {
                return 0;
            }

            return points / ExchangePointsForGoldCoins;
        }
    }

    [Serializable]
    public sealed class ScoreSaveData
    {
        public int Total;
        public int Stage;
        public int Round;
        public int GrantedGold;
    }
}

