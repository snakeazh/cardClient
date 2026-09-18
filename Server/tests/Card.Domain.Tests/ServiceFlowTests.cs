using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pve;
using CardShare.Domain.Services;
using CardShare.Infrastructure.Memory;
using Xunit;

namespace CardShare.Domain.Tests;

public class PveFlowTests
{
    [Fact]
    public async Task StartSpendsEnergyAndSettleIsIdempotent()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var userId = Guid.NewGuid();
        var profile = CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow);
        await players.SaveAsync(profile, CancellationToken.None);

        var (pve, _) = TestApp.Create(players, runs, config, clock);
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        Assert.Equal(config.Balance.EnergyMax - 1, started.Profile.Energy.Current);
        Assert.Contains(1, started.ShopRelicIds);

        await pve.ReportProgressAsync(
            userId,
            new PveProgressRequest { RunId = started.RunId, ClearedStage = true, Score = 99 },
            CancellationToken.None);

        var settle = new PveSettleRequest
        {
            RunId = started.RunId,
            Cleared = true,
            TotalScore = 99999,
            Stats = new PveSettleStats { KillMonster = 2, OneDamage = 50 }
        };
        var first = await pve.SettlePveAsync(userId, settle, CancellationToken.None);
        Assert.False(first.AlreadySettled);
        Assert.Equal(9, first.GoldGranted);
        Assert.Equal(9, first.Profile.Gold);
        Assert.Equal(1, first.Profile.Level.DifficultyProgress[0].HighestClearedLevel);
        Assert.DoesNotContain(first.Profile.Unlock.Progress, p => p.ConditionId == 10401 && p.Amount == 2);

        var second = await pve.SettlePveAsync(userId, settle, CancellationToken.None);
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
        var (pve, _) = TestApp.Create(players, runs, config, clock);
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        await pve.ReportProgressAsync(
            userId,
            new PveProgressRequest { RunId = started.RunId, ClearedStage = true, Score = 10 },
            CancellationToken.None);
        await pve.ReportProgressAsync(
            userId,
            new PveProgressRequest { RunId = started.RunId, ClearedStage = true, Score = 10 },
            CancellationToken.None);

        var settled = await pve.SettlePveAsync(
            userId,
            new PveSettleRequest { RunId = started.RunId, Cleared = true, LevelId = 1001, TotalScore = 0 },
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
        var (pve, _) = TestApp.Create(players, config: config, clock: clock);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1002, HeroId = 1 }, CancellationToken.None));
        Assert.Equal(ErrorCodes.LevelLocked, ex.Code);
    }

    [Fact]
    public async Task BagGrantConsumeAndGuideAreIdempotentOnProfile()
    {
        var (_, meta, userId) = await CreateApp();
        var granted = await meta.GrantBagAsync(userId, 101, 2, CancellationToken.None);
        Assert.Equal(2, granted.Bag.Single(e => e.ItemId == 101).Count);

        var consumed = await meta.ConsumeBagAsync(userId, 101, 1, CancellationToken.None);
        Assert.Equal(1, consumed.Bag.Single(e => e.ItemId == 101).Count);

        var profile = await meta.GetProfileAsync(userId, CancellationToken.None);
        Assert.Equal(1, profile.Bag.Single(e => e.ItemId == 101).Count);

        var first = await meta.CompleteGuideAsync(userId, 7, CancellationToken.None);
        var second = await meta.CompleteGuideAsync(userId, 7, CancellationToken.None);
        Assert.Equal(new[] { 7 }, first.GuideCompletedGroupIds);
        Assert.Equal(new[] { 7 }, second.GuideCompletedGroupIds);
    }

    [Fact]
    public async Task UnknownBagItemIsRejected()
    {
        var (_, meta, userId) = await CreateApp();
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            meta.GrantBagAsync(userId, 99999, 1, CancellationToken.None));
        Assert.Equal(ErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ShopBuySellAndGrantRunGoldUseServerBalance()
    {
        var (pve, _, userId) = await CreateApp();
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        Assert.NotNull(started.Run);
        Assert.NotEmpty(started.Run.ShopOfferIds);

        var funded = await pve.GrantRunGoldAsync(userId, started.RunId, 100, CancellationToken.None);
        var relicId = funded.Run.ShopOfferIds[0];
        var bought = await pve.BuyShopRelicAsync(userId, started.RunId, relicId, CancellationToken.None);
        Assert.Contains(relicId, bought.Run.RelicIds);
        Assert.DoesNotContain(relicId, bought.Run.ShopOfferIds);
        Assert.True(bought.Run.Gold < funded.Run.Gold);

        var sold = await pve.SellShopRelicAsync(userId, started.RunId, relicId, CancellationToken.None);
        Assert.DoesNotContain(relicId, sold.Run.RelicIds);
        Assert.True(sold.Run.Gold > bought.Run.Gold);
    }

    [Fact]
    public async Task ActiveRunBlocksSecondStartAndClearsAfterSettle()
    {
        var (pve, _, userId) = await CreateApp();
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        var active = await pve.GetActiveRunAsync(userId, CancellationToken.None);
        Assert.Equal(started.RunId, active.Run.RunId);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None));
        Assert.Equal(ErrorCodes.ActiveRunExists, ex.Code);

        await pve.SettlePveAsync(
            userId,
            new PveSettleRequest { RunId = started.RunId, Cleared = false, Forfeit = true },
            CancellationToken.None);
        var after = await pve.GetActiveRunAsync(userId, CancellationToken.None);
        Assert.True(after.Run == null || string.IsNullOrEmpty(after.Run.RunId));

        var again = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(again.RunId));
        Assert.NotEqual(started.RunId, again.RunId);
    }

    [Fact]
    public async Task GetProfileWritesOnlyOnDailyReset()
    {
        var config = TestConfig.Create();
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero) };
        var players = new MemoryPlayerRepository();
        var userId = Guid.NewGuid();
        var created = CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow);
        created.Energy.Current = 1;
        await players.SaveAsync(created, CancellationToken.None);
        var (_, meta) = TestApp.Create(players, config: config, clock: clock);

        var sameDay = await meta.GetProfileAsync(userId, CancellationToken.None);
        Assert.Equal(1, sameDay.Energy.Current);
        var stored = await players.GetAsync(userId, CancellationToken.None);
        Assert.Equal(1, stored!.Energy.Current);

        clock.UtcNow = clock.UtcNow.AddDays(1);
        var nextDay = await meta.GetProfileAsync(userId, CancellationToken.None);
        Assert.Equal(config.Balance.EnergyMax, nextDay.Energy.Current);
        stored = await players.GetAsync(userId, CancellationToken.None);
        Assert.Equal(config.Balance.EnergyMax, stored!.Energy.Current);
    }

    [Fact]
    public async Task ClearedSettleRequiresStageProgress()
    {
        var (pve, _, userId) = await CreateApp();
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            pve.SettlePveAsync(userId, new PveSettleRequest { RunId = started.RunId, Cleared = true }, CancellationToken.None));
        Assert.Equal(ErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ForfeitGrantsRunScoreLikeFailSettle()
    {
        var (pve, _, userId) = await CreateApp();
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        await pve.ReportProgressAsync(
            userId,
            new PveProgressRequest { RunId = started.RunId, ClearedStage = true, Score = 99 },
            CancellationToken.None);
        var settled = await pve.SettlePveAsync(
            userId,
            new PveSettleRequest { RunId = started.RunId, Cleared = false, Forfeit = true },
            CancellationToken.None);
        Assert.Equal(9, settled.GoldGranted);
        Assert.Equal(9, settled.Profile.Gold);
        Assert.Equal(1, settled.Profile.Level.DifficultyProgress[0].HighestClearedLevel);
    }

    private static async Task<(PveRunService Pve, PlayerMetaService Meta, Guid UserId)> CreateApp()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var userId = Guid.NewGuid();
        await players.SaveAsync(
            CardShare.Domain.Players.PlayerProfile.CreateNew(userId, config, clock.UtcNow),
            CancellationToken.None);
        var (pve, meta) = TestApp.Create(players, config: config, clock: clock);
        return (pve, meta, userId);
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
        var tokens = new MemoryTokenService(clock, TimeSpan.FromHours(1));
        var guest = new FakeGuestClient();
        var (pve, _) = TestApp.Create(players, config: config, clock: clock);
        var auth = new AuthService(
            new ICodeSessionClient[] { guest },
            bindings,
            players,
            tokens,
            config,
            clock,
            guestEnabled: true,
            pve);

        var login = await auth.LoginAsync(new LoginRequest { Provider = "guest", Code = "editor-device" }, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.Equal(config.Balance.EnergyMax, login.Profile.Energy.Current);
        Assert.Contains(1, login.Profile.Level.UnlockedHeroIds);

        var again = await auth.LoginAsync(new LoginRequest { Provider = "guest", Code = "editor-device" }, CancellationToken.None);
        Assert.Equal(login.Profile.UserId, again.Profile.UserId);
    }

    [Fact]
    public async Task LoginForfeitsActiveRun()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var bindings = new MemoryAuthBindingRepository();
        var tokens = new MemoryTokenService(clock, TimeSpan.FromHours(1));
        var (pve, _) = TestApp.Create(players, runs, config, clock);
        var auth = new AuthService(
            new ICodeSessionClient[] { new FakeGuestClient() },
            bindings,
            players,
            tokens,
            config,
            clock,
            guestEnabled: true,
            pve);

        var login = await auth.LoginAsync(new LoginRequest { Provider = "guest", Code = "forfeit-device" }, CancellationToken.None);
        var userId = Guid.Parse(login.Profile.UserId);
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(started.RunId));
        await pve.ReportProgressAsync(
            userId,
            new PveProgressRequest { RunId = started.RunId, ClearedStage = true, Score = 99 },
            CancellationToken.None);

        var again = await auth.LoginAsync(new LoginRequest { Provider = "guest", Code = "forfeit-device" }, CancellationToken.None);
        var active = await pve.GetActiveRunAsync(userId, CancellationToken.None);
        Assert.True(active.Run == null || string.IsNullOrEmpty(active.Run.RunId));
        Assert.Equal(login.Profile.Gold + 9, again.Profile.Gold);
        Assert.Equal(1, again.Profile.Level.DifficultyProgress[0].HighestClearedLevel);
    }

    [Fact]
    public async Task LoginAppliesPendingSettleBeforeForfeit()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var bindings = new MemoryAuthBindingRepository();
        var tokens = new MemoryTokenService(clock, TimeSpan.FromHours(1));
        var (pve, _) = TestApp.Create(players, runs, config, clock);
        var auth = new AuthService(
            new ICodeSessionClient[] { new FakeGuestClient() },
            bindings,
            players,
            tokens,
            config,
            clock,
            guestEnabled: true,
            pve);

        var login = await auth.LoginAsync(new LoginRequest { Provider = "guest", Code = "pending-device" }, CancellationToken.None);
        var userId = Guid.Parse(login.Profile.UserId);
        var started = await pve.StartPveAsync(userId, new PveStartRequest { LevelId = 1001, HeroId = 1 }, CancellationToken.None);
        await pve.ReportProgressAsync(
            userId,
            new PveProgressRequest { RunId = started.RunId, ClearedStage = true, Score = 99 },
            CancellationToken.None);

        var again = await auth.LoginAsync(new LoginRequest
        {
            Provider = "guest",
            Code = "pending-device",
            PendingSettle = new PveSettleRequest
            {
                RunId = started.RunId,
                Cleared = true,
                TotalScore = 0
            }
        }, CancellationToken.None);
        var active = await pve.GetActiveRunAsync(userId, CancellationToken.None);
        Assert.True(active.Run == null || string.IsNullOrEmpty(active.Run.RunId));
        Assert.True(again.Profile.Gold > login.Profile.Gold);
    }

    private sealed class FakeGuestClient : ICodeSessionClient
    {
        public string Provider => AuthProviders.Guest;

        public Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new CodeSession("guest:" + code));
    }
}
