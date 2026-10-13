using System;
using App.Energy;
using App.Wallet;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.AdShop
{
    /// <summary>
    /// 局外广告商店。看广告免费领体力/金币，各项每日限次；跨日在读取时懒检查
    /// （与 EnergyService 同一凌晨时刻），变更即落盘。
    /// </summary>
    public sealed class AdShopService : IAdShopService
    {
        public const string SaveKey = "adshop.v1";

        private readonly ISaveService _save;
        private readonly IEnergyService _energy;
        private readonly IWalletService _wallet;
        private int _gameDay;
        private int _staminaCount;
        private int _goldCount;
        private bool _dirty;

        public AdShopService(ISaveService save, IEnergyService energy, IWalletService wallet)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _energy = energy ?? throw new ArgumentNullException(nameof(energy));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        public int StaminaPerPurchase => AdShopBalance.StaminaPerPurchase;

        public int GoldPerPurchase => AdShopBalance.GoldPerPurchase;

        public int StaminaPurchasesLeftToday
        {
            get
            {
                EnsureDailyReset();
                return Math.Max(0, AdShopBalance.StaminaDailyLimit - _staminaCount);
            }
        }

        public int GoldPurchasesLeftToday
        {
            get
            {
                EnsureDailyReset();
                return Math.Max(0, AdShopBalance.GoldDailyLimit - _goldCount);
            }
        }

        public event Action Changed;

        public void ReplaceFromServer(int staminaCount, int goldCount)
        {
            _staminaCount = staminaCount < 0 ? 0 : staminaCount;
            _goldCount = goldCount < 0 ? 0 : goldCount;
            _gameDay = TodayGameDay();
            _dirty = true;
            Changed?.Invoke();
        }

        public bool TryPurchaseStamina()
        {
            EnsureDailyReset();
            if (AdShopBalance.StaminaDailyLimit <= 0 || _staminaCount >= AdShopBalance.StaminaDailyLimit)
            {
                return false;
            }

            // TODO: 接入真实广告 SDK，回调成功后再发放。当前与 GameSession/EnergyService 一致，直接发放。
            _energy.Add(AdShopBalance.StaminaPerPurchase);
            _staminaCount++;
            _dirty = true;
            Save();
            Changed?.Invoke();
            return true;
        }

        public bool TryPurchaseGold()
        {
            EnsureDailyReset();
            if (AdShopBalance.GoldDailyLimit <= 0 || _goldCount >= AdShopBalance.GoldDailyLimit)
            {
                return false;
            }

            // TODO: 接入真实广告 SDK，回调成功后再发放。
            _wallet.Add(AdShopBalance.GoldPerPurchase);
            _goldCount++;
            _dirty = true;
            Save();
            Changed?.Invoke();
            return true;
        }

        public void Load()
        {
            _gameDay = TodayGameDay();
            _staminaCount = 0;
            _goldCount = 0;
            _dirty = false;
            if (_save.HasKey(SaveKey))
            {
                var json = _save.GetString(SaveKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonUtility.FromJson<AdShopSaveData>(json);
                    if (data != null)
                    {
                        _gameDay = data.GameDay;
                        _staminaCount = Math.Max(0, data.StaminaCount);
                        _goldCount = Math.Max(0, data.GoldCount);
                    }
                    else
                    {
                        AppLog.Warn(LogChannel.UI, "Failed to parse ad shop save data.");
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

            var data = new AdShopSaveData
            {
                GameDay = _gameDay,
                StaminaCount = _staminaCount,
                GoldCount = _goldCount
            };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        /// <summary>
        /// 跨日懒检查：当前游戏日与存档日不同则清空当日已购次数。
        /// 在 Changed 回调里重读属性安全（二次检查同日直接返回，无递归）。
        /// </summary>
        private void EnsureDailyReset()
        {
            var today = TodayGameDay();
            if (today == _gameDay)
            {
                return;
            }

            _gameDay = today;
            _staminaCount = 0;
            _goldCount = 0;
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
