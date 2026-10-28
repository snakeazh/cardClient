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
    public void BuildsKeyBehaviorBodyWithAndroidPlatform()
    {
        var json = AdConversionPayloadBuilder.BuildKeyBehaviorBody(
            "CLeghfjf8_ICEIO4pYnh8",
            0,
            1604888786102L);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("game_addiction", root.GetProperty("event_type").GetString());
        Assert.Equal("CLeghfjf8_ICEIO4pYnh8", root.GetProperty("context").GetProperty("ad").GetProperty("callback").GetString());
        Assert.Equal("android", root.GetProperty("context").GetProperty("device").GetProperty("platform").GetString());
        Assert.False(root.TryGetProperty("properties", out _));
        Assert.Equal(1604888786102L, root.GetProperty("timestamp").GetInt64());
    }

    [Fact]
    public void MapsIosPlatform()
    {
        var json = AdConversionPayloadBuilder.BuildKeyBehaviorBody("cb", 1, 1L);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ios", doc.RootElement.GetProperty("context").GetProperty("device").GetProperty("platform").GetString());
    }

    [Fact]
    public void OmitsDeviceWhenOsUnknown()
    {
        var json = AdConversionPayloadBuilder.BuildKeyBehaviorBody("cb", -1, 1L);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("context").TryGetProperty("device", out _));
    }
}

public class AdWatchReportTests
{
    [Fact]
    public async Task ValidSceneRecordsStatsAndReportsConversion()
    {
        var players = new CardShare.Infrastructure.Memory.MemoryPlayerRepository();
        var stats = new TestApp.FakeAdWatchStats();
        var conversions = new TestApp.FakeAdConversionReporter();
        var userId = Guid.NewGuid();
        var profile = PlayerProfile.CreateNew(userId, TestConfig.Create(), DateTimeOffset.UtcNow);
        profile.ApplyAdAttribution("click-1", 0, DateTimeOffset.UtcNow);
        await players.SaveAsync(profile, CancellationToken.None);
        var (_, meta) = TestApp.Create(players, conversions: conversions, adStats: stats);

        await meta.ReportAdWatchAsync(userId, "revive", CancellationToken.None);

        Assert.Equal(new[] { "revive" }, stats.Scenes);
        Assert.Equal(new[] { "revive" }, conversions.Scenes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Revive")]
    [InlineData("revive!")]
    [InlineData("this_scene_name_is_way_too_long_to_be_valid")]
    public async Task InvalidSceneIsRejectedWithoutStats(string scene)
    {
        var players = new CardShare.Infrastructure.Memory.MemoryPlayerRepository();
        var stats = new TestApp.FakeAdWatchStats();
        var conversions = new TestApp.FakeAdConversionReporter();
        var userId = Guid.NewGuid();
        await players.SaveAsync(PlayerProfile.CreateNew(userId, TestConfig.Create(), DateTimeOffset.UtcNow), CancellationToken.None);
        var (_, meta) = TestApp.Create(players, conversions: conversions, adStats: stats);

        await Assert.ThrowsAsync<CardShare.Domain.DomainException>(() =>
            meta.ReportAdWatchAsync(userId, scene, CancellationToken.None));
        Assert.Empty(stats.Scenes);
        Assert.Empty(conversions.Scenes);
    }

    [Fact]
    public async Task AdShopClaimAlsoRecordsStats()
    {
        var players = new CardShare.Infrastructure.Memory.MemoryPlayerRepository();
        var stats = new TestApp.FakeAdWatchStats();
        var userId = Guid.NewGuid();
        await players.SaveAsync(PlayerProfile.CreateNew(userId, TestConfig.Create(), DateTimeOffset.UtcNow), CancellationToken.None);
        var (_, meta) = TestApp.Create(players, adStats: stats);

        await meta.ClaimAdShopAsync(userId, "gold", CancellationToken.None);

        Assert.Equal(new[] { AdScenes.AdShopGold }, stats.Scenes);
    }
}
