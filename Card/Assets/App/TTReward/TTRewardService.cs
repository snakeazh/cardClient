using System;
using App.Energy;
using App.Wallet;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.TTReward
{
    /// <summary>
    /// 抖音侧边栏入口奖励。领取条件 = 从抖音侧边栏启动（当前桩恒 true）+ 当日未领满；
    /// 跨日在读取时懒检查（与 EnergyService/AdShopService 同一凌晨时刻），变更即落盘。
    /// </summary>
    public sealed class TTRewardService : ITTRewardService
    {
        public const string SaveKey = "ttreward.v1";

        private readonly ISaveService _save;
        private readonly IWalletService _wallet;
        private int _gameDay;
        private int _claimedCount;
        private bool _dirty;

        public TTRewardService(ISaveService save, IWalletService wallet)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        // TODO: 接抖音 SDK 后按启动参数判定侧边栏入口；当前与 AdShop 广告桩同口径，直接视为满足。
        public bool IsFromSidebar => true;

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
    }
}
