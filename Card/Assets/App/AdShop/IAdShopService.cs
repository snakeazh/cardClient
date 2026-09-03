using System;
using Framework.Save;

namespace App.AdShop
{
    /// <summary>
    /// 局外广告商店：看广告免费领体力/金币，各项每日限次，跨日凌晨重置（与体力同时刻）。
    /// 落盘 adshop.v1。
    /// </summary>
    public interface IAdShopService : ISaveFlushable
    {
        /// <summary>每次购买获得体力。</summary>
        int StaminaPerPurchase { get; }

        /// <summary>每次购买获得金币。</summary>
        int GoldPerPurchase { get; }

        /// <summary>今日剩余购买体力次数。</summary>
        int StaminaPurchasesLeftToday { get; }

        /// <summary>今日剩余购买金币次数。</summary>
        int GoldPurchasesLeftToday { get; }

        event Action Changed;

        /// <summary>看广告购买体力（当前为模拟发放）。超出每日次数返回 false 且不改动。</summary>
        bool TryPurchaseStamina();

        /// <summary>看广告购买金币（当前为模拟发放）。超出每日次数返回 false 且不改动。</summary>
        bool TryPurchaseGold();

        void Load();
    }
}
