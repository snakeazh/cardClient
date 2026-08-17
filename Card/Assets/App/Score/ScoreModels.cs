using System;

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

    /// <summary>Score / HP / courage constants. Independent of GameBalance.</summary>
    public static class ScoreBalance
    {
        public const int PlayerStartHp = 4000;

        /// <summary>满血进关时的勇气值。4000 HP → 800。</summary>
        public const int PlayerStartCourage = 800;

        /// <summary>10 score = 1 gold. Conversion always floors.</summary>
        public const int ScorePerGold = 10;

        /// <summary>进关时血量 → 勇气值，向下取整。hp &lt;= 0 为 0。</summary>
        public static int HpToCourage(int hp)
        {
            if (hp <= 0)
            {
                return 0;
            }

            return hp * PlayerStartCourage / PlayerStartHp;
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

