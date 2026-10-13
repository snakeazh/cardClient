namespace CardShare.Domain;

public static class GameDay
{
    private static readonly DateTime EpochLocal = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static int Of(DateTimeOffset utc, TimeZoneInfo zone, int resetHour)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone).DateTime;
        var shifted = local.AddHours(-resetHour);
        return (int)(shifted.Date - EpochLocal).TotalDays;
    }
}
