using System;
using App.Config;

namespace App.AdShop
{
    /// <summary>广告商店常量。数值读 GameConst，未加载或非法时用兜底值（同 EnergyBalance 模式）。</summary>
    public static class AdShopBalance
    {
        public const int FallbackStaminaPerPurchase = 10;
        public const int FallbackStaminaDailyLimit = 10;
        public const int FallbackGoldPerPurchase = 10;
        public const int FallbackGoldDailyLimit = 10;

        /// <summary>每次购买获得体力。读 GameConst.AdShopStaminaPerBuy。</summary>
        public static int StaminaPerPurchase
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.AdShopStaminaPerBuy <= 0)
                {
                    return FallbackStaminaPerPurchase;
                }

                return GameConst.Instance.AdShopStaminaPerBuy;
            }
        }

        /// <summary>每日可购买体力次数，0 为禁用。读 GameConst.AdShopStaminaDailyLimit。</summary>
        public static int StaminaDailyLimit
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.AdShopStaminaDailyLimit < 0)
                {
                    return FallbackStaminaDailyLimit;
                }

                return GameConst.Instance.AdShopStaminaDailyLimit;
            }
        }

        /// <summary>每次购买获得金币。读 GameConst.AdShopGoldPerBuy。</summary>
        public static int GoldPerPurchase
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.AdShopGoldPerBuy <= 0)
                {
                    return FallbackGoldPerPurchase;
                }

                return GameConst.Instance.AdShopGoldPerBuy;
            }
        }

        /// <summary>每日可购买金币次数，0 为禁用。读 GameConst.AdShopGoldDailyLimit。</summary>
        public static int GoldDailyLimit
        {
            get
            {
                if (!GameConst.IsLoaded || GameConst.Instance.AdShopGoldDailyLimit < 0)
                {
                    return FallbackGoldDailyLimit;
                }

                return GameConst.Instance.AdShopGoldDailyLimit;
            }
        }
    }

    [Serializable]
    public sealed class AdShopSaveData
    {
        public int GameDay;
        public int StaminaCount;
        public int GoldCount;
    }
}
