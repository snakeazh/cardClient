using System;
using App.Net;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.Energy
{
    /// <summary>
    /// 局外体力。每日固定时刻（GameConst.EnergyDailyResetHour）回满，跨日在读取时懒检查；
    /// 脏标记落盘，存档记录游戏日编号与当日广告补充次数。
    /// </summary>
    public sealed class EnergyService : IEnergyService
    {
        public const string SaveKey = "energy.v1";

        private readonly ISaveService _save;
        private int _current;
        private int _gameDay;
        private int _adRefillCount;
        private bool _dirty;
        /// <summary>登录灌档后为 true：跨日回满以服务端为准，本地时钟不得改写。</summary>
        private bool _serverAuthoritative;

        public EnergyService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public int Max => EnergyBalance.Max;

        public int Current
        {
            get
            {
                EnsureDailyReset();
                return _current;
            }
        }

        public int AdRefillsLeftToday
        {
            get
            {
                EnsureDailyReset();
                return Math.Max(0, EnergyBalance.AdRefillDailyLimit - _adRefillCount);
            }
        }

        public bool HasEnergyForRun => Current >= EnergyBalance.CostPerRun;

        public event Action Changed;

        public bool TrySpendRunCost()
        {
            if (!MetaLocalAuthority.AllowsLocalMutation("Energy.TrySpendRunCost"))
            {
                return false;
            }

            EnsureDailyReset();
            var cost = EnergyBalance.CostPerRun;
            if (_current < cost)
            {
                return false;
            }

            _current -= cost;
            _dirty = true;
            Changed?.Invoke();
            return true;
        }

        public void ReplaceFromServer(int current, int adRefillCount)
        {
            _current = ClampToNonNegative(current);
            _adRefillCount = adRefillCount < 0 ? 0 : adRefillCount;
            _gameDay = TodayGameDay();
            _serverAuthoritative = true;
            _dirty = true;
            Changed?.Invoke();
        }

        public void Add(int amount)
        {
            if (amount <= 0 || !MetaLocalAuthority.AllowsLocalMutation("Energy.Add"))
            {
                return;
            }

            EnsureDailyReset();
            _current += amount;
            _dirty = true;
            Changed?.Invoke();
        }

        public bool TryRefillByAd()
        {
            if (!MetaLocalAuthority.AllowsLocalMutation("Energy.TryRefillByAd"))
            {
                return false;
            }

            EnsureDailyReset();
            if (EnergyBalance.AdRefillDailyLimit <= 0 || _adRefillCount >= EnergyBalance.AdRefillDailyLimit)
            {
                return false;
            }

            // TODO: 接入真实广告 SDK，回调成功后再发放。当前与 GameSession 广告复活一致，直接发放。
            _current = EnergyBalance.Max;
            _adRefillCount++;
            _dirty = true;
            Changed?.Invoke();
            return true;
        }

        public void Load()
        {
            _current = EnergyBalance.Max;
            _gameDay = TodayGameDay();
            _adRefillCount = 0;
            _dirty = false;
            _serverAuthoritative = false;
            if (_save.HasKey(SaveKey))
            {
                var json = _save.GetString(SaveKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonUtility.FromJson<EnergySaveData>(json);
                    if (data != null)
                    {
                        _current = ClampToNonNegative(data.Current);
                        _gameDay = data.GameDay;
                        _adRefillCount = Math.Max(0, data.AdRefillCount);
                    }
                    else
                    {
                        AppLog.Warn(LogChannel.Energy, "Failed to parse save data.");
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

            var data = new EnergySaveData
            {
                Current = _current,
                GameDay = _gameDay,
                AdRefillCount = _adRefillCount
            };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        /// <summary>
        /// 跨日懒检查：当前游戏日与存档日不同则回满并清空当日广告次数。
        /// 在 Changed 回调里重读属性安全（二次检查同日直接返回，无递归）。
        /// </summary>
        private void EnsureDailyReset()
        {
            if (_serverAuthoritative)
            {
                return;
            }

            var today = TodayGameDay();
            if (today == _gameDay)
            {
                return;
            }

            _gameDay = today;
            _current = EnergyBalance.Max;
            _adRefillCount = 0;
            _dirty = true;
            Changed?.Invoke();
        }

        /// <summary>游戏日编号：本地时间前移 ResetHour 小时后按自然日计数，凌晨 ResetHour 点跨日。</summary>
        private static int TodayGameDay()
        {
            var shifted = DateTime.Now.AddHours(-EnergyBalance.DailyResetHour);
            return (int)(shifted.Date - new DateTime(2020, 1, 1)).TotalDays;
        }

        /// <summary>仅防负数；商店购买可超上限，加载时不按 Max 截断。</summary>
        private static int ClampToNonNegative(int value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
