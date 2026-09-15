using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Services;
using CardShare.Infrastructure.Memory;
using Xunit;

namespace CardShare.Domain.Tests;

public class PveFlowTests
{
    [Fact]
    public async Task SettleFailureKeepsMidRunProgressAndWalletOnlyFromScore()
    {
        var config = TestConfig.Create();
        // GetGold 故意设大，确认局外金不吃关卡局内金。
        config.Tables.Levels[0].GetGold = 999;
        config.Tables.Levels[1].GetGold = 999;
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var userId = Guid.NewGuid();
        await players.SaveAsync(
            CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow),
            CancellationToken.None);
        var commands = new PlayerCommandService(players, runs, config, clock, new MemoryPlayerLock());
        var started = await commands.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);

        var settled = await commands.SettlePveAsync(
            userId,
            new PveSettleRequest
            {
                RunId = started.RunId,
                Cleared = false,
                LevelId = 1001,
                HighestClearedLevelId = 1002,
                TotalScore = 55
            },
            CancellationToken.None);

        Assert.False(settled.AlreadySettled);
        Assert.Equal(5, settled.GoldGranted);
        Assert.Equal(5, settled.Profile.Gold);
        Assert.Equal(2, settled.Profile.Level.DifficultyProgress[0].HighestClearedLevel);
    }

    [Fact]
    public async Task GrantTalentByAdIncrementsCountWithoutSpendingGold()
    {
        var (commands, userId) = await CreateCommands();
        var before = await commands.GetProfileAsync(userId, CancellationToken.None);
        var granted = await commands.GrantTalentByAdAsync(userId, 101, CancellationToken.None);
        Assert.Equal(101, granted.TalentId);
        Assert.Equal(1, granted.Count);
        Assert.Equal(0, granted.GoldSpent);
        Assert.Equal(before.Gold, granted.Profile.Gold);
        Assert.Equal(before.Talent.DrawCount, granted.DrawCount);
    }

    [Fact]
    public async Task StartSpendsEnergyAndSettleIsIdempotent()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var locks = new MemoryPlayerLock();
        var userId = Guid.NewGuid();
        var profile = CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow);
        await players.SaveAsync(profile, CancellationToken.None);

        var commands = new PlayerCommandService(players, runs, config, clock, locks);
        var started = await commands.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        Assert.Equal(config.Balance.EnergyMax - 1, started.Profile.Energy.Current);
        Assert.Contains(1, started.ShopRelicIds);

        var settle = new PveSettleRequest
        {
            RunId = started.RunId,
            Cleared = true,
            LevelId = 1001,
            TotalScore = 99,
            Stats = new PveSettleStats { KillMonster = 2, OneDamage = 50 }
        };
        var first = await commands.SettlePveAsync(userId, settle, CancellationToken.None);
        Assert.False(first.AlreadySettled);
        Assert.Equal(9, first.GoldGranted);
        Assert.Equal(9, first.Profile.Gold);
        Assert.Equal(1, first.Profile.Level.DifficultyProgress[0].HighestClearedLevel);
        Assert.Equal(2, first.Profile.Unlock.Progress.First(p => p.ConditionId == 10401).Amount);

        var second = await commands.SettlePveAsync(userId, settle, CancellationToken.None);
        Assert.True(second.AlreadySettled);
        Assert.Equal(9, second.Profile.Gold);
    }

    [Fact]
    public async Task SettleAcceptsLaterLevelInSameDifficulty()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var userId = Guid.NewGuid();
        await players.SaveAsync(
            CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow),
            CancellationToken.None);
        var commands = new PlayerCommandService(players, runs, config, clock, new MemoryPlayerLock());
        var started = await commands.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);

        var settled = await commands.SettlePveAsync(
            userId,
            new PveSettleRequest { RunId = started.RunId, Cleared = true, LevelId = 1002, TotalScore = 20 },
            CancellationToken.None);
        Assert.False(settled.AlreadySettled);
        Assert.True(settled.Profile.Level.DifficultyProgress[0].HighestClearedLevel >= 2);
    }

    [Fact]
    public async Task LockedLevelCannotStart()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var userId = Guid.NewGuid();
        await players.SaveAsync(
            CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow),
            CancellationToken.None);
        var commands = new PlayerCommandService(players, new MemoryPveRunRepository(), config, clock, new MemoryPlayerLock());

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            commands.StartPveAsync(userId, new PveStartRequest { LevelId = 1002, HeroId = 1 }, CancellationToken.None));
        Assert.Equal(ErrorCodes.LevelLocked, ex.Code);
    }

    [Fact]
    public async Task BagGrantConsumeAndGuideAreIdempotentOnProfile()
    {
        var (commands, userId) = await CreateCommands();
        var granted = await commands.GrantBagAsync(userId, 101, 2, CancellationToken.None);
        Assert.Equal(2, granted.Bag.Single(e => e.ItemId == 101).Count);

        var consumed = await commands.ConsumeBagAsync(userId, 101, 1, CancellationToken.None);
        Assert.Equal(1, consumed.Bag.Single(e => e.ItemId == 101).Count);

        var profile = await commands.GetProfileAsync(userId, CancellationToken.None);
        Assert.Equal(1, profile.Bag.Single(e => e.ItemId == 101).Count);

        var first = await commands.CompleteGuideAsync(userId, 7, CancellationToken.None);
        var second = await commands.CompleteGuideAsync(userId, 7, CancellationToken.None);
        Assert.Equal(new[] { 7 }, first.GuideCompletedGroupIds);
        Assert.Equal(new[] { 7 }, second.GuideCompletedGroupIds);
    }

    [Fact]
    public async Task UnknownBagItemIsRejected()
    {
        var (commands, userId) = await CreateCommands();
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            commands.GrantBagAsync(userId, 99999, 1, CancellationToken.None));
        Assert.Equal(ErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ShopBuySellAndGrantRunGoldUseServerBalance()
    {
        var (commands, userId) = await CreateCommands();
        var started = await commands.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        Assert.NotNull(started.Run);
        Assert.NotEmpty(started.Run.ShopOfferIds);

        var funded = await commands.GrantRunGoldAsync(userId, started.RunId, 100, "combat", CancellationToken.None);
        var relicId = funded.Run.ShopOfferIds[0];
        var bought = await commands.BuyShopRelicAsync(userId, started.RunId, relicId, CancellationToken.None);
        Assert.Contains(relicId, bought.Run.RelicIds);
        Assert.DoesNotContain(relicId, bought.Run.ShopOfferIds);
        Assert.True(bought.Run.Gold < funded.Run.Gold);

        var sold = await commands.SellShopRelicAsync(userId, started.RunId, relicId, CancellationToken.None);
        Assert.DoesNotContain(relicId, sold.Run.RelicIds);
        Assert.True(sold.Run.Gold > bought.Run.Gold);
    }

    [Fact]
    public async Task GrantRunGoldRejectsUnknownReasonAndHugeAmount()
    {
        var (commands, userId) = await CreateCommands();
        var started = await commands.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);

        var badReason = await Assert.ThrowsAsync<DomainException>(() =>
            commands.GrantRunGoldAsync(userId, started.RunId, 10, "hack", CancellationToken.None));
        Assert.Equal(ErrorCodes.InvalidRequest, badReason.Code);

        var tooMuch = await Assert.ThrowsAsync<DomainException>(() =>
            commands.GrantRunGoldAsync(userId, started.RunId, 50_001, "combat", CancellationToken.None));
        Assert.Equal(ErrorCodes.InvalidRequest, tooMuch.Code);
    }

    private static async Task<(PlayerCommandService Commands, Guid UserId)> CreateCommands()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var userId = Guid.NewGuid();
        await players.SaveAsync(
            CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow),
            CancellationToken.None);
        return (new PlayerCommandService(players, new MemoryPveRunRepository(), config, clock, new MemoryPlayerLock()), userId);
    }
}

public class AuthServiceTests
{
    [Fact]
    public async Task GuestLoginCreatesProfile()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var bindings = new MemoryAuthBindingRepository();
        var tokens = new MemoryTokenService(clock, TimeSpan.FromHours(1), TimeSpan.FromDays(1));
        var guest = new FakeGuestClient();
        var auth = new AuthService(
            new ICodeSessionClient[] { guest },
            bindings,
            players,
            tokens,
            config,
            clock,
            guestEnabled: true);

        var login = await auth.LoginAsync(new LoginRequest { Provider = "guest", Code = "editor-device" }, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.Equal(config.Balance.EnergyMax, login.Profile.Energy.Current);
        Assert.Contains(1, login.Profile.Level.UnlockedHeroIds);

        var again = await auth.LoginAsync(new LoginRequest { Provider = "guest", Code = "editor-device" }, CancellationToken.None);
        Assert.Equal(login.Profile.UserId, again.Profile.UserId);
    }

    private sealed class FakeGuestClient : ICodeSessionClient
    {
        public string Provider => AuthProviders.Guest;

        public Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new CodeSession("guest:" + code));
    }
}
