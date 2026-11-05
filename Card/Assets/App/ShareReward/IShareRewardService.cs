using System;

namespace App.ShareReward
{
    /// <summary>
    /// 分享奖励：分享成功后账号级永久增加搓牌技能使用次数，只发一次。
    /// 领奖状态入库（`sharereward.v1`），首页入口按 <see cref="HasClaimed"/> 自动隐藏。
    /// </summary>
    public interface IShareRewardService
    {
        /// <summary>是否已领取过分享奖励。</summary>
        bool HasClaimed { get; }

        /// <summary>账号永久搓牌次数加成（未领取为 0）。</summary>
        int RubBonusCharges { get; }

        /// <summary>领取状态变化（领取成功 / 编辑器调试重置）。</summary>
        event Action Changed;

        /// <summary>发放一次永久加成；已领取过返回 false，不重复发放。</summary>
        bool TryClaim();
    }
}
