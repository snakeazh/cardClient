using System;

namespace App.ShareReward
{
    /// <summary>
    /// 分享奖励常量。数值暂用代码兜底常量，策划上表后可改读 GameConst（同 <c>TTRewardBalance</c> 口径）。
    /// </summary>
    public static class ShareRewardBalance
    {
        /// <summary>分享成功后永久增加的搓牌技能使用次数。</summary>
        public const int RubBonusCharges = 1;

        /// <summary>分享卡片标题。</summary>
        public const string ShareTitle = "牌桌对决，等你来战！";

        /// <summary>分享卡片图片；留空时由平台使用游戏画面截图。</summary>
        public const string ShareImageUrl = "";

        /// <summary>分享卡片 query，透传给进入方的启动参数，便于来源统计。</summary>
        public const string ShareQuery = "from=sharereward";
    }

    /// <summary>分享奖励存档。</summary>
    [Serializable]
    public sealed class ShareRewardSaveData
    {
        /// <summary>是否已领取过分享奖励（领取后首页入口永久隐藏）。</summary>
        public bool Claimed;

        /// <summary>已发放的永久搓牌次数加成总量。</summary>
        public int RubBonusCharges;
    }
}
