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

public class AdConversionUrlBuilderTests
{
    [Fact]
    public void BuildsTrackActivateFormatWithEncodedProps()
    {
        var url = AdConversionUrlBuilder.BuildPaidConversionUrl(
            "https://ad.toutiao.com/track/activate/",
            "CLeghfjf8_ICEIO4pYnh8",
            0,
            1290,
            null);

        Assert.Equal(
            "https://ad.toutiao.com/track/activate/?callback=CLeghfjf8_ICEIO4pYnh8&os=0&event_type=2&props=%7B%22pay_amount%22%3A1290%7D",
            url);
    }

    [Fact]
    public void AppendsAccessTokenWhenProvided()
    {
        var url = AdConversionUrlBuilder.BuildPaidConversionUrl(
            "https://ad.toutiao.com/track/activate/",
            "cb",
            1,
            5,
            "token abc");

        Assert.EndsWith("&access_token=token%20abc", url);
    }
}
