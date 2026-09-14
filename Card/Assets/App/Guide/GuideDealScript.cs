using App.Game;

namespace App.Guide
{
    /// <summary>FirstBattle 强制发牌 / 搓牌脚本：AA344，搓掉 index 2 的「3」变成 A。</summary>
    public static class GuideDealScript
    {
        public const int FirstBattleGroupId = 1;

        /// <summary>必须搓掉的手牌下标（「3」）。</summary>
        public const int RubTargetIndex = 2;

        /// <summary>固定玩家手牌：A A 3 4 4（花色互不冲突，牌堆仍留有 A）。</summary>
        public static readonly Card[] PlayerHand =
        {
            new Card(Suit.Heart, Rank.Ace),
            new Card(Suit.Diamond, Rank.Ace),
            new Card(Suit.Club, Rank.Three),
            new Card(Suit.Spade, Rank.Four),
            new Card(Suit.Heart, Rank.Four)
        };
    }
}
