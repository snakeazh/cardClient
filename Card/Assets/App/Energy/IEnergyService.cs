using System;
using Framework.Save;

namespace App.Energy
{
    /// <summary>
    /// 局外体力。每局消耗，每日固定时刻回满（跨日懒检查），看广告补充（每日限次）。落盘 energy.v1。
    /// </summary>
    public interface IEnergyService : ISaveFlushable
    {
        /// <summary>当前体力。读取时自动检查跨日回满。</summary>
        int Current { get; }

        int Max { get; }

        /// <summary>今日剩余看广告补充次数。</summary>
        int AdRefillsLeftToday { get; }

        /// <summary>体力是否足够开一局。</summary>
        bool HasEnergyForRun { get; }

        event Action Changed;

        /// <summary>扣除开局消耗。体力不足返回 false 且不改动。</summary>
        bool TrySpendRunCost();

        /// <summary>看广告补充体力（当前为模拟发放），回满。超出每日次数返回 false。</summary>
        bool TryRefillByAd();

        void Load();
    }
}
