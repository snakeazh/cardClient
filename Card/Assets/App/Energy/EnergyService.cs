using System;
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

        public bool TryRefillByAd()
        {
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
            if (_save.HasKey(SaveKey))
            {
                var json = _save.GetString(SaveKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonUtility.FromJson<EnergySaveData>(json);
                    if (data != null)
                    {
                        _current = ClampToRange(data.Current);
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

        private static int ClampToRange(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            return value > EnergyBalance.Max ? EnergyBalance.Max : value;
        }
    }
}
