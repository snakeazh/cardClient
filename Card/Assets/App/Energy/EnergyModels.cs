using System;
using App.Config;
using CardShare.Contracts.Config;

namespace App.Energy
{
    /// <summary>体力常量。数值读 GameConst，未加载或非法时用兜底值（同 ScoreBalance 模式）。</summary>
    public static class EnergyBalance
    {
        public const int FallbackMax = 5;
        public const int FallbackCostPerRun = 1;
        public const int FallbackDailyResetHour = 5;
        public const int FallbackAdRefillDailyLimit = 3;

        /// <summary>体力上限。读 GameConst.EnergyMax。</summary>
        public static int Max
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.EnergyMax <= 0)
                {
                    return FallbackMax;
                }

                return GameConst.Instance.EnergyMax;
            }
        }

        /// <summary>每局消耗体力。读 GameConst.EnergyCostPerRun。</summary>
        public static int CostPerRun
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.EnergyCostPerRun <= 0)
                {
                    return FallbackCostPerRun;
                }

                return GameConst.Instance.EnergyCostPerRun;
            }
        }

        /// <summary>每日重置时刻（0-23 点，0 表示午夜跨日）。读 GameConst.EnergyDailyResetHour。</summary>
        public static int DailyResetHour
        {
            get
            {
                if (!GameConst.IsLoaded
                    || GameConst.Instance.EnergyDailyResetHour < 0
                    || GameConst.Instance.EnergyDailyResetHour > 23)
                {
                    return FallbackDailyResetHour;
                }

                return GameConst.Instance.EnergyDailyResetHour;
            }
        }

        /// <summary>每日看广告补充次数，0 为禁用。读 GameConst.EnergyAdRefillDailyLimit。</summary>
        public static int AdRefillDailyLimit
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.EnergyAdRefillDailyLimit < 0)
                {
                    return FallbackAdRefillDailyLimit;
                }

                return GameConst.Instance.EnergyAdRefillDailyLimit;
            }
        }
    }

    [Serializable]
    public sealed class EnergySaveData
    {
        public int Current;
        public int GameDay;
        public int AdRefillCount;
    }
}
