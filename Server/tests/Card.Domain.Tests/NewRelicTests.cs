using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using CardShare.Domain.Players;
using CardShare.Infrastructure.Config;
using CardShare.Infrastructure.Memory;
using CardShare.Domain.Pve;
using Xunit;

namespace CardShare.Domain.Tests;

public class NewRelicTests
{
    private static IGameTables Tables()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Card/Assets/Res/Config"));
        return new FileGameConfigLoader(path).Load();
    }

    [Fact]
    public void ConfigAndPvpShopContainNewRelics()
    {
        var tables = Tables();
        foreach (var (pve, pvp, condition, mechanism) in new[] { (447, 438, 10503, 40046), (448, 439, 11006, 40047), (449, 440, 11003, 40048) })
        {
            Assert.True(tables.TryGetRelic(pve, out var relic));
            Assert.Equal(condition, relic.UnlockConditionId);
            Assert.Contains(mechanism, relic.MechanismId);
            Assert.True(tables.TryGetPvpRelic(pvp, out var pvpRelic));
            Assert.Contains(mechanism, pvpRelic.MechanismId);
            Assert.True(PvpRelicRuntime.IsPvpSupported(tables, pvpRelic));
            Assert.True(PvpRelicStock.Create(tables).Available(pvp) > 0);
        }
    }

    [Fact]
    public void PvpSlotsAndGoldSpendingUseOwnedRelics()
    {
        var tables = PvpGameTables.Wrap(Tables());
        var fighter = new PvpFighter { RelicSlots = 4 };
        fighter.OwnedRelicIds.Add(438);
        fighter.OwnedRelicIds.Add(440);
        Assert.Equal(6, PvpRelicRuntime.EffectiveRelicSlots(tables, fighter));
        PvpRelicRuntime.OnGoldSpent(tables, fighter);
        PvpRelicRuntime.OnGoldSpent(tables, fighter);
        Assert.Equal(0.1f, fighter.MagicCoinDamageBonus, 4);
        fighter.OwnedRelicIds.Remove(438);
        Assert.Equal(4, PvpRelicRuntime.EffectiveRelicSlots(tables, fighter));
    }

    [Fact]
    public void PvpSharpBladeForcesCrit()
    {
        var tables = PvpGameTables.Wrap(Tables());
        var seat = new SeatSetup { HeroId = 1, Attack = 100, RelicIds = new[] { 439 } };
        var score = HandEvaluator.Evaluate(new[] { new Card(Suit.Spade, Rank.Two), new Card(Suit.Heart, Rank.Four), new Card(Suit.Club, Rank.Six) });
        var input = CombatBonuses.BuildPlayerInput(tables, seat, score, new CombatSituation());
        Assert.True(input.CritRate >= 1f);
    }

    [Fact]
    public void PvpSpendingBonusAndSharpBladeReachDamageResolution()
    {
        var tables = PvpGameTables.Wrap(Tables());
        var fighter = new PvpFighter { Combat = new SeatSetup { HeroId = 1, Attack = 100, Hp = 100, MaxHp = 100 } };
        fighter.OwnedRelicIds.AddRange(new[] { 439, 440 });
        fighter.OwnedRelicPower.AddRange(new[] { 1, 1 });
        PvpRelicRuntime.OnGoldSpent(tables, fighter);
        PvpRelicRuntime.OnGoldSpent(tables, fighter);
        var seat = new SeatSetup { HeroId = 1, Hp = 100, MaxHp = 100 };
        PvpRelicRuntime.ApplyTrackersToSeat(tables, fighter, seat);
        var score = HandEvaluator.Evaluate(new[] { new Card(Suit.Spade, Rank.Two), new Card(Suit.Heart, Rank.Four), new Card(Suit.Club, Rank.Six) });
        var input = CombatBonuses.BuildPlayerInput(tables, seat, score, new CombatSituation());
        Assert.Equal(0.1f, input.OutgoingDamagePercent, 4);
        var result = CombatDamage.Resolve(input, new Random(1));
        Assert.True(result.Crit);
        Assert.True(result.Damage > result.FormulaDamage);
    }

    [Fact]
    public void UnlockConditionsRemainIndependent()
    {
        var tables = Tables();
        var state = new UnlockState();
        UnlockRules.Apply(state, new PveSettleStats { ClearDifficulty = 10, NoSkillLevelTen = 1 }, tables.UnlockConditions);
        Assert.True(state.GetAmount(10503) >= 10);
        Assert.True(state.GetAmount(11006) >= 1);
        Assert.Equal(0, state.GetAmount(11003));

        UnlockRules.Apply(state, new PveSettleStats { LuxuryGoods = 1 }, tables.UnlockConditions);
        Assert.True(state.GetAmount(11003) >= 1);
    }

    [Fact]
    public async Task PveGoldSpendsPersistRelicBonusAndShopCapacity()
    {
        var config = new SharedGameConfig(Tables(), TimeZoneInfo.Utc);
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var user = Guid.NewGuid();
        await players.SaveAsync(PlayerProfile.CreateNew(user, config, clock.UtcNow), CancellationToken.None);
        var run = new PveRun { RunId = Guid.NewGuid(), UserId = user, Gold = 5000, RelicIds = new List<int> { 447, 449, 101, 102 }, ShopOfferIds = new List<int> { 448 } };
        Assert.Equal(config.Balance.DefaultRelicNumMax, run.RelicIds.Count);
        await runs.SaveAsync(run, CancellationToken.None);
        var (pve, _) = TestApp.Create(players, runs, config, clock);

        var bought = await pve.BuyShopRelicAsync(user, run.RunId.ToString(), 448, CancellationToken.None);
        Assert.Equal(0.05f, bought.Run.GoldTurnToDamageBonus, 4);
        Assert.True(PveShopRules.RefreshCost(run, config.Balance) > 0, $"Refresh first={config.Balance.ShopRefreshFirst}, after={config.Balance.ShopRefreshAfter}");
        var refreshed = await pve.RefreshShopAsync(user, run.RunId.ToString(), CancellationToken.None);
        Assert.Equal(0.10f, refreshed.Run.GoldTurnToDamageBonus, 4);
        var spent = await pve.SpendRunGoldAsync(user, run.RunId.ToString(), 1, CancellationToken.None);
        Assert.Equal(0.15f, spent.Run.GoldTurnToDamageBonus, 4);
        var restored = await pve.GetActiveRunAsync(user, CancellationToken.None);
        Assert.Equal(0.15f, restored.Run.GoldTurnToDamageBonus, 4);
    }

    [Theory]
    [InlineData(1, false, true, true)]
    [InlineData(1, true, true, false)]
    [InlineData(2, false, false, true)]
    public async Task PveSettlementPersistsIndependentDifficultyUnlocks(int heroId, bool usedSkill, bool defaultHero, bool noSkill)
    {
        var tables = Tables();
        var config = new SharedGameConfig(tables, TimeZoneInfo.Utc);
        var clock = new TestClock();
        var players = new MemoryPlayerRepository();
        var runs = new MemoryPveRunRepository();
        var user = Guid.NewGuid();
        await players.SaveAsync(PlayerProfile.CreateNew(user, config, clock.UtcNow), CancellationToken.None);
        var level = tables.Levels.Where(x => x.Difficulty == 10).OrderBy(x => x.Id).Last();
        var run = new PveRun { RunId = Guid.NewGuid(), UserId = user, HeroId = heroId, LevelId = level.Id, CurrentLevelId = level.Id, HighestClearedLevelId = level.Id };
        await runs.SaveAsync(run, CancellationToken.None);
        var (pve, _) = TestApp.Create(players, runs, config, clock);
        if (usedSkill)
            await pve.ReportProgressAsync(user, new PveProgressRequest { RunId = run.RunId.ToString(), SkillOnly = true, UsedSkillThisRun = true }, CancellationToken.None);
        var settled = await pve.SettlePveAsync(user, new PveSettleRequest { RunId = run.RunId.ToString(), Cleared = true }, CancellationToken.None);
        Assert.Contains(settled.Profile.Unlock.Progress, x => x.ConditionId == 10503 && x.Amount >= 10);
        Assert.Equal(defaultHero, settled.Profile.Unlock.Progress.Any(x => x.ConditionId == 11003 && x.Amount >= 1));
        Assert.Equal(noSkill, settled.Profile.Unlock.Progress.Any(x => x.ConditionId == 11006 && x.Amount >= 1));
    }

    [Fact]
    public void ExistingDifficultyRecordsRestoreOnlyProvableUnlocks()
    {
        var config = new SharedGameConfig(Tables(), TimeZoneInfo.Utc);
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), config, DateTimeOffset.UtcNow);
        var max = config.Tables.GetMaxLevel(10);
        Assert.True(max > 0);
        profile.Level.SetHighestCleared(10, max);
        profile.Level.SetHeroHighestCleared(config.Tables.GameConst.DefaultHeroId, 10, max);
        Assert.True(profile.SyncNewRelicUnlocks(config));
        Assert.True(profile.Unlock.GetAmount(10503) >= 10);
        Assert.True(profile.Unlock.GetAmount(11003) >= 1);
        Assert.Equal(0, profile.Unlock.GetAmount(11006));
        Assert.False(profile.SyncNewRelicUnlocks(config));
    }
}
