using CardShare.Contracts;
using CardShare.Contracts.Config;
using CardShare.Domain;
using CardShare.Domain.Config;
using CardShare.Domain.Players;
using CardShare.Infrastructure.Config;
using Xunit;

namespace CardShare.Domain.Tests;

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
}

public static class TestConfig
{
    public static IGameConfig Create() => SharedGameConfig.Fallback(TimeZoneInfo.Utc);
}

public class EnergyResetTests
{
    [Fact]
    public void CrossDayRefillsToMaxAndClearsAdCounters()
    {
        var config = TestConfig.Create();
        var now = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, now.AddDays(-1));
        profile.Energy.Current = 1;
        profile.Energy.AdRefillCount = 2;
        profile.AdShop.GoldCount = 3;

        profile.EnsureDailyReset(config, now);

        Assert.Equal(config.Balance.EnergyMax, profile.Energy.Current);
        Assert.Equal(0, profile.Energy.AdRefillCount);
        Assert.Equal(0, profile.AdShop.GoldCount);
    }

    [Fact]
    public void SameDayKeepsCurrentEnergy()
    {
        var config = TestConfig.Create();
        var now = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, now);
        profile.Energy.Current = 2;

        profile.EnsureDailyReset(config, now.AddHours(1));

        Assert.Equal(2, profile.Energy.Current);
    }
}

public class TalentDrawTests
{
    [Fact]
    public void FirstDrawIsFreeThenPriceSteps()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);
        Assert.Equal(0, profile.GetDrawCost(config));

        var first = profile.DrawTalent(101, config);
        Assert.Equal(0, first.GoldSpent);
        Assert.Equal(1, first.Count);
        Assert.Equal(1, profile.Talent.DrawCount);
        Assert.Equal(50, profile.GetDrawCost(config));
    }

    [Fact]
    public void DrawFailsWhenGoldIsShort()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);
        profile.DrawTalent(101, config);
        var ex = Assert.Throws<DomainException>(() => profile.DrawTalent(101, config));
        Assert.Equal(ErrorCodes.InsufficientGold, ex.Code);
    }
}

public class UnlockRulesTests
{
    [Fact]
    public void AccumulateAddsStackedValue()
    {
        var unlock = new UnlockState();
        var stats = new PveSettleStats { KillMonster = 3 };
        var conditions = new[]
        {
            new UnlockConditionConfig { Id = 10401, Type = ContidionType.KillMonster, StackedValue = 1, Value = 20 }
        };

        UnlockRules.Apply(unlock, stats, conditions);
        UnlockRules.Apply(unlock, stats, conditions);

        Assert.Equal(6, unlock.GetAmount(10401));
    }

    [Fact]
    public void MaxTypesKeepPeak()
    {
        var unlock = new UnlockState();
        var conditions = new[]
        {
            new UnlockConditionConfig { Id = 1, Type = ContidionType.OneDamage, StackedValue = 1, Value = 100 }
        };

        UnlockRules.Apply(unlock, new PveSettleStats { OneDamage = 40 }, conditions);
        UnlockRules.Apply(unlock, new PveSettleStats { OneDamage = 25 }, conditions);
        UnlockRules.Apply(unlock, new PveSettleStats { OneDamage = 90 }, conditions);

        Assert.Equal(90, unlock.GetAmount(1));
    }
}
