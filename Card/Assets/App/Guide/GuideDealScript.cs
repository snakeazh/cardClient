using App.Game;

namespace App.Guide
{
    /// <summary>引导强制发牌 / 搓牌脚本：QKA23，搓掉 index 3 的「2」变成 A。</summary>
    public static class GuideDealScript
    {
        public const int FirstBattleGroupId = GuideGroupIds.FirstBattle;
        public const int FirstSkillGroupId = GuideGroupIds.FirstSkill;
        public const int FirstTalentDrawGroupId = GuideGroupIds.FirstTalentDraw;
        public const int FirstShopGroupId = GuideGroupIds.FirstShop;

        /// <summary>必须搓掉的手牌下标（「2」）。</summary>
        public const int RubTargetIndex = 3;

        /// <summary>亮牌必须点选的下标（Q / K / A）。</summary>
        public static readonly int[] RevealTargetIndices = { 0, 1, 2 };

        /// <summary>固定玩家手牌：Q K A 2 3。</summary>
        public static readonly Card[] PlayerHand =
        {
            new Card(Suit.Heart, Rank.Queen),
            new Card(Suit.Diamond, Rank.King),
            new Card(Suit.Club, Rank.Ace),
            new Card(Suit.Spade, Rank.Two),
            new Card(Suit.Heart, Rank.Three)
        };
    }
}
