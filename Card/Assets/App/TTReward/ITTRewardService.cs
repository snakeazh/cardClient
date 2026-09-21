using System;

namespace App.TTReward
{
    /// <summary>
    /// 抖音侧边栏入口奖励：从抖音主界面左上角侧边栏进入游戏可领限量金币礼盒，每日一次。
    /// 编辑器下为桩（入口可见、始终视为从侧边栏进入）；抖音包内通过 TT.CheckScene 判定入口可见性、
    /// 通过 tt.onShow 最新启动参数（scene=021036）判定是否从侧边栏进入。
    /// </summary>
    public interface ITTRewardService
    {
        /// <summary>当前是否从抖音侧边栏启动（取 tt.onShow 的最新值）。编辑器桩恒 true。</summary>
        bool IsFromSidebar { get; }

        /// <summary>当前宿主是否支持侧边栏入口（tt.checkScene 的 isExist），不支持时 Home 隐藏入口。编辑器桩恒 true。</summary>
        bool IsSidebarEntrySupported { get; }

        /// <summary>每次领取发放的金币数。</summary>
        int GoldPerClaim { get; }

        /// <summary>今日是否已领取。</summary>
        bool ClaimedToday { get; }

        /// <summary>今日剩余可领次数（每日限量内）。</summary>
        int ClaimsLeftToday { get; }

        /// <summary>领取成功返回 true 并发放金币；未从侧边栏进入、当日已领满或禁用时返回 false。</summary>
        bool TryClaim();

        /// <summary>跳转抖音首页侧边栏（tt.navigateToScene），用于未从侧边栏进入时的引导。</summary>
        void NavigateToSidebar();

        event Action Changed;

        void Load();

        void Save();
    }
}
