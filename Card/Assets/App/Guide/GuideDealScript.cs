using App.Game;

namespace App.Guide
{
    /// <summary>
    /// 引导强制发牌 / 搓牌脚本。
    /// FirstBattle：QKA23，教选三张开战；
    /// FirstSkill：AA223，搓中间「2」(index 2) 成 AAA23。
    /// </summary>
    public static class GuideDealScript
    {
        public const int FirstBattleGroupId = GuideGroupIds.FirstBattle;
        public const int FirstSkillGroupId = GuideGroupIds.FirstSkill;
        public const int FirstTalentDrawGroupId = GuideGroupIds.FirstTalentDraw;
        public const int FirstShopGroupId = GuideGroupIds.FirstShop;

        /// <summary>FirstSkill 必须搓掉的手牌下标（中间的「2」）。</summary>
        public const int RubTargetIndex = 2;

        /// <summary>亮牌必须点选的下标（搓后为三张 A）。</summary>
        public static readonly int[] RevealTargetIndices = { 0, 1, 2 };

        /// <summary>首局选牌：Q K A 2 3。</summary>
        public static readonly Card[] FirstBattleHand =
        {
            new Card(Suit.Heart, Rank.Queen),
            new Card(Suit.Diamond, Rank.King),
            new Card(Suit.Club, Rank.Ace),
            new Card(Suit.Spade, Rank.Two),
            new Card(Suit.Heart, Rank.Three)
        };

        /// <summary>搓牌引导：A A 2 2 3 → 搓 index2 成 A A A 2 3。</summary>
        public static readonly Card[] FirstSkillHand =
        {
            new Card(Suit.Heart, Rank.Ace),
            new Card(Suit.Diamond, Rank.Ace),
            new Card(Suit.Club, Rank.Two),
            new Card(Suit.Spade, Rank.Two),
            new Card(Suit.Heart, Rank.Three)
        };

        /// <summary>搓牌引导时敌人固定：J J Q Q K。</summary>
        public static readonly Card[] FirstSkillEnemyHand =
        {
            new Card(Suit.Heart, Rank.Jack),
            new Card(Suit.Diamond, Rank.Jack),
            new Card(Suit.Club, Rank.Queen),
            new Card(Suit.Spade, Rank.Queen),
            new Card(Suit.Heart, Rank.King)
        };

        /// <summary>兼容旧引用：默认搓牌手牌。</summary>
        public static readonly Card[] PlayerHand = FirstSkillHand;
    }
}
