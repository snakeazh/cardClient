using CardShare.Battle;
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

        profile.ApplyDailyReset(config, now);

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

        profile.ApplyDailyReset(config, now.AddHours(1));

        Assert.Equal(2, profile.Energy.Current);
    }
}

public class PvpScoreTests
{
    [Fact]
    public void NewProfileStartsAtZeroAndClampsBelowZero()
    {
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), TestConfig.Create(), DateTimeOffset.UtcNow);
        Assert.Equal(0, profile.PvpScore);
        Assert.Equal(0, profile.ApplyPvpScoreDelta(-40));
        Assert.Equal(50, profile.ApplyPvpScoreDelta(50));
        Assert.Equal(75, profile.ApplyPvpScoreDelta(25));
        Assert.Equal(75, profile.ApplyPvpScoreDelta(0));
        Assert.Equal(35, profile.ApplyPvpScoreDelta(-40));
        Assert.Equal(0, profile.ApplyPvpScoreDelta(-100));
    }

    [Fact]
    public void ScoreForRankUsesConfiguredOrDefaultTable()
    {
        Assert.Equal(50, PvpSettlementRules.ScoreForRank(1, null));
        Assert.Equal(25, PvpSettlementRules.ScoreForRank(2, Array.Empty<int>()));
        Assert.Equal(0, PvpSettlementRules.ScoreForRank(3, null));
        Assert.Equal(-40, PvpSettlementRules.ScoreForRank(4, null));
        Assert.Equal(0, PvpSettlementRules.ScoreForRank(5, null));
        Assert.Equal(10, PvpSettlementRules.ScoreForRank(1, new[] { 10, 5 }));
    }
}

public class TalentDrawTests
{
    [Fact]
    public void DrawCostStartsAtBaseThenPriceSteps()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);
        Assert.Equal(50, profile.GetDrawCost(config));

        profile.GrantWalletGold(50);
        var first = profile.DrawTalent(101, config);
        Assert.Equal(50, first.GoldSpent);
        Assert.Equal(1, first.Count);
        Assert.Equal(1, profile.Talent.DrawCount);
        Assert.Equal(100, profile.GetDrawCost(config));
    }

    [Fact]
    public void DrawFailsWhenGoldIsShort()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);
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

public class HeroProgressTests
{
    [Fact]
    public void MarkClearedRecordsAccountAndHeroProgressSeparately()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);

        profile.MarkCleared(new LevelConfig { Id = 1003, Difficulty = 1, Level = 3 }, 2, config);
        profile.MarkCleared(new LevelConfig { Id = 2004, Difficulty = 2, Level = 4 }, 2, config);
        profile.MarkCleared(new LevelConfig { Id = 1003, Difficulty = 1, Level = 3 }, 3, config);

        // 账号级进度与原来一致：取所有英雄里的最高关。
        Assert.Equal(3, profile.Level.GetHighestCleared(1));
        Assert.Equal(4, profile.Level.GetHighestCleared(2));

        // 每个英雄单独记：英雄 3 没打过难度 2。
        Assert.Equal(3, profile.Level.GetHeroHighestCleared(2, 1));
        Assert.Equal(4, profile.Level.GetHeroHighestCleared(2, 2));
        Assert.Equal(3, profile.Level.GetHeroHighestCleared(3, 1));
        Assert.Equal(0, profile.Level.GetHeroHighestCleared(3, 2));
        Assert.Equal(0, profile.Level.GetHeroHighestCleared(4, 1));
    }

    [Fact]
    public void LowerClearDoesNotOverwriteHeroRecord()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);

        profile.MarkCleared(new LevelConfig { Id = 1007, Difficulty = 1, Level = 7 }, 2, config);
        profile.MarkCleared(new LevelConfig { Id = 1002, Difficulty = 1, Level = 2 }, 2, config);

        Assert.Equal(7, profile.Level.GetHeroHighestCleared(2, 1));
        Assert.Equal(7, profile.Level.GetHighestCleared(1));
    }
}


public class AdminGrantTests
{
    [Fact]
    public void GrantEnergyAllowsExceedingMax()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);

        profile.GrantEnergy(10);

        Assert.Equal(config.Balance.EnergyMax + 10, profile.Energy.Current);
        Assert.Throws<DomainException>(() => profile.GrantEnergy(0));
    }

    [Fact]
    public void UnlockAllHeroesIgnoresClearConditions()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);
        // 新档只有默认英雄；2 号英雄要求通关难度 1。
        Assert.False(profile.IsHeroUnlocked(2, config));

        var added = profile.UnlockAllHeroes(config);

        Assert.Equal(1, added);
        Assert.True(profile.IsHeroUnlocked(2, config));
        // 重复执行幂等。
        Assert.Equal(0, profile.UnlockAllHeroes(config));
    }

    [Fact]
    public void UnlockAllTalentsMaxesEveryTalentWithoutTouchingDrawCount()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);

        var changed = profile.UnlockAllTalents(config);

        Assert.Equal(2, changed);
        Assert.Equal(10, profile.Talent.GetCount(101));
        Assert.Equal(10, profile.Talent.GetCount(102));
        // GM 送天赋不推高下次抽取价格。
        Assert.Equal(0, profile.Talent.DrawCount);
        // 只升不降，重复执行不再改档。
        Assert.Equal(0, profile.UnlockAllTalents(config));
    }

    [Fact]
    public void UnlockAllDifficultiesClearsLevelsAndChainsHeroUnlock()
    {
        var config = TestConfig.Create();
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);
        Assert.False(profile.IsLevelUnlocked(1002, config));

        var changed = profile.UnlockAllDifficulties(config);

        Assert.Equal(1, changed);
        Assert.True(profile.IsDifficultyCleared(1, config));
        Assert.True(profile.IsLevelUnlocked(1002, config));
        // 通关条件英雄由 SyncUnlockedHeroes 连带解锁。
        Assert.True(profile.IsHeroUnlocked(2, config));
    }
}
