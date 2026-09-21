using System;
using App.Energy;
using App.Wallet;
using Framework.Log;
using Framework.Save;
using UnityEngine;
#if !UNITY_EDITOR
using System.Collections.Generic;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
#endif

namespace App.TTReward
{
    /// <summary>
    /// 抖音侧边栏入口奖励。领取条件 = 从抖音侧边栏启动（tt.onShow 最新参数 scene=021036）+ 当日未领满；
    /// 入口可见性由 tt.checkScene 判定。编辑器下保持桩行为（入口可见、视为侧边栏进入），便于调试弹窗流程。
    /// 跨日在读取时懒检查（与 EnergyService/AdShopService 同一凌晨时刻），变更即落盘。
    /// </summary>
    public sealed class TTRewardService : ITTRewardService
    {
        public const string SaveKey = "ttreward.v1";

        /// <summary>从抖音首页侧边栏启动时 tt.onShow 回调里的 scene 值。</summary>
        private const string SidebarSceneValue = "021036";

        private readonly ISaveService _save;
        private readonly IWalletService _wallet;
        private int _gameDay;
        private int _claimedCount;
        private bool _dirty;
        private bool _isFromSidebar;
        private bool _sidebarSupported;

        public TTRewardService(ISaveService save, IWalletService wallet)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        public bool IsFromSidebar => _isFromSidebar;

        public bool IsSidebarEntrySupported => _sidebarSupported && TTRewardBalance.DailyClaimLimit > 0;

        public int GoldPerClaim => TTRewardBalance.GoldPerClaim;

        public bool ClaimedToday
        {
            get
            {
                EnsureDailyReset();
                return _claimedCount > 0;
            }
        }

        public int ClaimsLeftToday
        {
            get
            {
                EnsureDailyReset();
                return Math.Max(0, TTRewardBalance.DailyClaimLimit - _claimedCount);
            }
        }

        public event Action Changed;

        public bool TryClaim()
        {
            EnsureDailyReset();
            if (!IsFromSidebar || TTRewardBalance.DailyClaimLimit <= 0 ||
                _claimedCount >= TTRewardBalance.DailyClaimLimit)
            {
                return false;
            }

            _wallet.Add(TTRewardBalance.GoldPerClaim);
            _claimedCount++;
            _dirty = true;
            Save();
            Changed?.Invoke();
            return true;
        }

        public void NavigateToSidebar()
        {
#if UNITY_EDITOR
            AppLog.Info(LogChannel.UI, "NavigateToSidebar stub: editor does not jump.");
#else
            try
            {
                var data = new JsonData();
                data["scene"] = "sidebar";
                TT.NavigateToScene(
                    data,
                    null,
                    null,
                    (code, msg) => AppLog.Warn(LogChannel.UI, $"TT NavigateToScene failed: {code} {msg}"));
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "TT NavigateToScene unavailable: " + ex.Message);
            }
#endif
        }

        public void Load()
        {
            _gameDay = TodayGameDay();
            _claimedCount = 0;
            _dirty = false;
            if (_save.HasKey(SaveKey))
            {
                var json = _save.GetString(SaveKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonUtility.FromJson<TTRewardSaveData>(json);
                    if (data != null)
                    {
                        _gameDay = data.GameDay;
                        _claimedCount = Math.Max(0, data.ClaimedCount);
                    }
                    else
                    {
                        AppLog.Warn(LogChannel.UI, "Failed to parse TT reward save data.");
                    }
                }
            }

#if UNITY_EDITOR
            _isFromSidebar = true;
            _sidebarSupported = true;
#else
            HookSidebarLaunchState();
#endif

            EnsureDailyReset();
            Changed?.Invoke();
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var data = new TTRewardSaveData
            {
                GameDay = _gameDay,
                ClaimedCount = _claimedCount
            };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        /// <summary>跨日懒检查：当前游戏日与存档日不同则清空当日已领次数。</summary>
        private void EnsureDailyReset()
        {
            var today = TodayGameDay();
            if (today == _gameDay)
            {
                return;
            }

            _gameDay = today;
            _claimedCount = 0;
            _dirty = true;
            Save();
            Changed?.Invoke();
        }

        /// <summary>游戏日编号：与 EnergyService 同口径（凌晨 EnergyDailyResetHour 点跨日）。</summary>
        private static int TodayGameDay()
        {
            var shifted = DateTime.Now.AddHours(-EnergyBalance.DailyResetHour);
            return (int)(shifted.Date - new DateTime(2020, 1, 1)).TotalDays;
        }

#if !UNITY_EDITOR
        /// <summary>
        /// 接抖音侧边栏复访能力：启动即监听 tt.onShow 并刷新启动场景（从侧边栏热启动返回时必须取最新值），
        /// 同时用 tt.checkScene 判定宿主是否支持侧边栏入口。接口不可用时按不支持/未从侧边栏进入处理。
        /// </summary>
        private void HookSidebarLaunchState()
        {
            try
            {
                TT.GetAppLifeCycle().OnShow += HandleAppShow;
                RefreshLaunchScene();
                TT.CheckScene(
                    TTSideBar.SceneEnum.SideBar,
                    isExist =>
                    {
                        _sidebarSupported = isExist;
                        Changed?.Invoke();
                    },
                    null,
                    (code, msg) => AppLog.Warn(LogChannel.UI, $"TT CheckScene failed: {code} {msg}"));
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "TT sidebar hooks unavailable: " + ex.Message);
            }
        }

        private void HandleAppShow(Dictionary<string, object> args)
        {
            // tt.onShow 回调参数：侧边栏启动时 scene=021036、launch_from=homepage、location=sidebar_card
            if (args != null && args.TryGetValue("scene", out var scene) && scene != null)
            {
                _isFromSidebar = scene.ToString() == SidebarSceneValue;
            }
            else
            {
                RefreshLaunchScene();
            }

            Changed?.Invoke();
        }

        private void RefreshLaunchScene()
        {
            try
            {
                var options = TT.GetLaunchOptionsSync();
                _isFromSidebar = options != null && options.Scene == SidebarSceneValue;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "TT GetLaunchOptionsSync failed: " + ex.Message);
            }
        }
#endif
    }
}
