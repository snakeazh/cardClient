using System;

namespace App.TTReward
{
    /// <summary>
    /// 抖音侧边栏入口奖励：从抖音主界面左上角侧边栏进入游戏可领限量金币礼盒，每日一次。
    /// 侧边栏启动检测当前为桩（始终视为从侧边栏进入），接抖音 SDK 后替换 <see cref="IsFromSidebar"/>。
    /// </summary>
    public interface ITTRewardService
    {
        /// <summary>本次启动是否来自抖音侧边栏。桩实现恒 true。</summary>
        bool IsFromSidebar { get; }

        /// <summary>每次领取发放的金币数。</summary>
        int GoldPerClaim { get; }

        /// <summary>今日是否已领取。</summary>
        bool ClaimedToday { get; }

        /// <summary>今日剩余可领次数（每日限量内）。</summary>
        int ClaimsLeftToday { get; }

        /// <summary>领取成功返回 true 并发放金币；未从侧边栏进入、当日已领满或禁用时返回 false。</summary>
        bool TryClaim();

        event Action Changed;

        void Load();

        void Save();
    }
}
