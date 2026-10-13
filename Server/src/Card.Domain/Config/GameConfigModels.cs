using CardShare.Contracts;
using CardShare.Contracts.Config;

namespace CardShare.Domain.Config;

public sealed class GameBalance
{
    public int EnergyMax { get; init; } = 5;

    public int EnergyCostPerRun { get; init; } = 1;

    public int EnergyDailyResetHour { get; init; } = 5;

    public int EnergyAdRefillDailyLimit { get; init; } = 3;

    public int AdShopStaminaPerBuy { get; init; } = 10;

    public int AdShopStaminaDailyLimit { get; init; } = 10;

    public int AdShopGoldPerBuy { get; init; } = 10;

    public int AdShopGoldDailyLimit { get; init; } = 10;

    public int DefaultHeroId { get; init; } = 1;

    public int ExchangePointsForGoldCoins { get; init; } = 10;

    public int TalentChestNeedGold { get; init; } = 0;

    public int TalentNeedChestGold { get; init; } = 50;

    public int CopiesPerLevel { get; init; } = 1;

    public int PlayerInitialGoldNum { get; init; }

    public int ShopRefreshFirst { get; init; } = 5;

    public int ShopRefreshAfter { get; init; } = 3;

    public int ShopRefreshGoldUpNumMax { get; init; } = 20;

    public int ShopOfferCount { get; init; } = 4;

    public int DefaultRelicNumMax { get; init; } = 3;

    public static GameBalance Fallback { get; } = new GameBalance();

    public static GameBalance From(GameConst c)
    {
        return new GameBalance
        {
            EnergyMax = c.EnergyMax > 0 ? c.EnergyMax : 5,
            EnergyCostPerRun = c.EnergyCostPerRun > 0 ? c.EnergyCostPerRun : 1,
            EnergyDailyResetHour = c.EnergyDailyResetHour,
            EnergyAdRefillDailyLimit = c.EnergyAdRefillDailyLimit,
            AdShopStaminaPerBuy = c.AdShopStaminaPerBuy > 0 ? c.AdShopStaminaPerBuy : 10,
            AdShopStaminaDailyLimit = c.AdShopStaminaDailyLimit,
            AdShopGoldPerBuy = c.AdShopGoldPerBuy > 0 ? c.AdShopGoldPerBuy : 10,
            AdShopGoldDailyLimit = c.AdShopGoldDailyLimit,
            DefaultHeroId = c.DefaultHeroId > 0 ? c.DefaultHeroId : 1,
            ExchangePointsForGoldCoins = c.ExchangePointsForGoldCoins > 0 ? c.ExchangePointsForGoldCoins : 10,
            TalentChestNeedGold = c.TalentChestNeedGold,
            TalentNeedChestGold = c.TalentNeedChestGold > 0 ? c.TalentNeedChestGold : 50,
            CopiesPerLevel = 1,
            PlayerInitialGoldNum = Math.Max(0, c.PlayerInitialGoldNum),
            ShopRefreshFirst = c.ShopRefreshFirst > 0 ? c.ShopRefreshFirst : 5,
            ShopRefreshAfter = Math.Max(0, c.ShopRefreshAfter),
            ShopRefreshGoldUpNumMax = c.ShopRefreshGoldUpNumMax > 0 ? c.ShopRefreshGoldUpNumMax : 20,
            ShopOfferCount = 4,
            DefaultRelicNumMax = c.DefaultRelicNumMax > 0 ? c.DefaultRelicNumMax : 3
        };
    }
}

public interface IGameConfig
{
    IGameTables Tables { get; }

    TimeZoneInfo TimeZone { get; }

    GameBalance Balance { get; }
}
