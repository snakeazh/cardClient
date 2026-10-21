using System.Text.Json;
using CardShare.Domain.Ads;
using CardShare.Domain.Players;
using Xunit;

namespace CardShare.Domain.Tests;

public class AdAttributionTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyCallbackDoesNotOverwrite()
    {
        var profile = new PlayerProfile();
        profile.AdAttribution.Callback = "old-cb";
        profile.AdAttribution.Os = 0;

        profile.ApplyAdAttribution("  ", 1, Now);

        Assert.Equal("old-cb", profile.AdAttribution.Callback);
        Assert.Equal(0, profile.AdAttribution.Os);
    }

    [Fact]
    public void NonEmptyCallbackOverwritesWithLatest()
    {
        var profile = new PlayerProfile();
        profile.AdAttribution.Callback = "old-cb";

        profile.ApplyAdAttribution(" new-cb ", 1, Now);

        Assert.Equal("new-cb", profile.AdAttribution.Callback);
        Assert.Equal(1, profile.AdAttribution.Os);
        Assert.Equal(Now, profile.AdAttribution.ReceivedAtUtc);
    }
}

public class AdConversionPayloadBuilderTests
{
    [Fact]
    public void BuildsActivePayBodyWithAndroidPlatform()
    {
        var json = AdConversionPayloadBuilder.BuildPaidConversionBody(
            "CLeghfjf8_ICEIO4pYnh8",
            0,
            1290,
            1604888786102L);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("active_pay", root.GetProperty("event_type").GetString());
        Assert.Equal("CLeghfjf8_ICEIO4pYnh8", root.GetProperty("context").GetProperty("ad").GetProperty("callback").GetString());
        Assert.Equal("android", root.GetProperty("context").GetProperty("device").GetProperty("platform").GetString());
        Assert.Equal(1290, root.GetProperty("properties").GetProperty("pay_amount").GetInt32());
        Assert.Equal(1604888786102L, root.GetProperty("timestamp").GetInt64());
    }

    [Fact]
    public void MapsIosPlatform()
    {
        var json = AdConversionPayloadBuilder.BuildPaidConversionBody("cb", 1, 5, 1L);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ios", doc.RootElement.GetProperty("context").GetProperty("device").GetProperty("platform").GetString());
    }

    [Fact]
    public void OmitsDeviceWhenOsUnknown()
    {
        var json = AdConversionPayloadBuilder.BuildPaidConversionBody("cb", -1, 5, 1L);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("context").TryGetProperty("device", out _));
    }
}
