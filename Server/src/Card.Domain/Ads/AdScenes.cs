namespace CardShare.Domain.Ads;

/// <summary>广告场景名约定与校验。energy_refill_ad / adshop_* 由服务端发奖接口自动统计，客户端只上报局内点位。</summary>
public static class AdScenes
{
    public const string EnergyRefillAd = "energy_refill_ad";
    public const string AdShopStamina = "adshop_stamina";
    public const string AdShopGold = "adshop_gold";

    public const int MaxLength = 32;

    /// <summary>合法场景名：非空、≤32 字符、仅小写字母/数字/下划线（防统计存储被刷垃圾字段）。</summary>
    public static bool IsValid(string? scene)
    {
        if (string.IsNullOrWhiteSpace(scene) || scene.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in scene)
        {
            if (!char.IsLower(c) && !char.IsDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
