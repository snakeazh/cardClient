using CardShare.Contracts;
using CardShare.Domain.Config;

namespace CardShare.Infrastructure.Config;

public static class JsonGameConfig
{
    public static SharedGameConfig Load(string configDirectory, string timeZoneId)
    {
        var zone = ResolveTimeZone(timeZoneId);
        var tables = new FileGameConfigLoader(configDirectory).Load();
        return new SharedGameConfig(tables, zone);
    }

    public static SharedGameConfig CreateFallback(TimeZoneInfo? zone = null)
        => SharedGameConfig.Fallback(zone);

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, "Asia/Shanghai", "China Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
