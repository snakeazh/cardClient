using System;

namespace App.TTReward
{
    /// <summary>抖音侧边栏奖励常量。数值暂用代码兜底常量，策划在 GameConst 加字段后改为读表（同 AdShopBalance 口径）。</summary>
    public static class TTRewardBalance
    {
        public const int FallbackGoldPerClaim = 500;
        public const int FallbackDailyClaimLimit = 1;

        /// <summary>每次领取获得金币。</summary>
        public static int GoldPerClaim => FallbackGoldPerClaim;

        /// <summary>每日可领取次数，0 为禁用。</summary>
        public static int DailyClaimLimit => FallbackDailyClaimLimit;
    }

    [Serializable]
    public sealed class TTRewardSaveData
    {
        public int GameDay;
        public int ClaimedCount;
    }
}
