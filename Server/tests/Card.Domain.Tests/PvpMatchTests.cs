using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using Xunit;

namespace CardShare.Domain.Tests;

public class PvpScheduleTests
{
    [Fact]
    public void ClassicModeLoadsThirteenRoundsFromTables()
    {
        var tables = PvpTestTables.Classic();
        Assert.True(PvpSchedule.TryLoad(tables, 1, out var mode, out var rounds));
        Assert.Equal("经典", mode.Name);
        Assert.Equal(150, mode.InitialGold);
        Assert.Equal(13, rounds.Count);
        Assert.Equal(PvpFightKind.Monster, rounds[0].FightKind);
        Assert.Equal(PvpFightKind.Pvp, rounds[1].FightKind);
        Assert.Equal(13, PvpSchedule.LastRound(rounds));
        Assert.Equal(10011, rounds[0].MonsterGroup);
    }

    [Fact]
    public void TwoAliveSkipsMonsterWhenModeSaysSo()
    {
        var tables = PvpTestTables.Classic();
        Assert.True(tables.TryGetPvpMode(1, out var mode));
        Assert.True(PvpSchedule.TryGetRound(tables.GetPvpRounds(1), 5, out var row));
        Assert.Equal(PvpFightKind.Monster, row.FightKind);
        Assert.Equal(PvpFightKind.Pvp, PvpSchedule.EffectiveKind(mode, row, 2));
        Assert.Equal(PvpFightKind.Monster, PvpSchedule.EffectiveKind(mode, row, 4));
    }
}

public class PvpPairingTests
{
    [Fact]
    public void FourPlayersUseRotation()
    {
        var seats = new[] { 0, 1, 2, 3 };
        var first = PvpPairing.ForPvp(seats, 0);
        Assert.Equal(2, first.Count);
        Assert.Equal(0, first[0].LeftSeat);
        Assert.Equal(1, first[0].RightSeat);
        Assert.Equal(2, first[1].LeftSeat);
        Assert.Equal(3, first[1].RightSeat);

        var second = PvpPairing.ForPvp(seats, 1);
        Assert.Equal(0, second[0].LeftSeat);
        Assert.Equal(2, second[0].RightSeat);
    }

    [Fact]
    public void ThreeAliveByeGoesToMonster()
    {
        var slots = PvpPairing.ForPvp(new[] { 0, 1, 2 }, 0);
        Assert.Contains(slots, s => !s.IsMonster && s.LeftSeat == 0 && s.RightSeat == 1);
        Assert.Contains(slots, s => s.IsMonster && s.LeftSeat == 2);
    }

    [Fact]
    public void TwoAliveAreFinal()
    {
        var slots = PvpPairing.ForPvp(new[] { 0, 3 }, 2);
        Assert.Single(slots);
        Assert.False(slots[0].IsMonster);
        Assert.Equal(0, slots[0].LeftSeat);
        Assert.Equal(3, slots[0].RightSeat);
    }
}

public class PvpMatchTests
{
    [Fact]
    public void OpenStartsMonsterRoundWithOneDuelEach()
    {
        var match = OpenClassic();
        Assert.Equal(1, match.Round);
        Assert.Equal(PvpFightKind.Monster, match.FightKind);
        Assert.Equal(4, match.Duels.Count);
        Assert.All(match.Fighters, f => Assert.Equal(150, f.Gold));
        Assert.All(match.Fighters, f => Assert.True(f.Hp > 0));
    }

    [Fact]
    public void ResolvingMonsterRoundAdvancesToPvpPairs()
    {
        var match = OpenClassic();
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);
        AdvanceThrough(match);

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpFightKind.Pvp, match.FightKind);
        Assert.Equal(2, match.Duels.Count);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
    }

    [Fact]
    public void ViewerOnlySeesOwnDuelCardsBeforeShowdown()
    {
        var match = OpenClassic();
        var a = match.Fighters[0].UserId;
        var view = match.ViewFor(a);
        Assert.Equal("monster", view.FightKind);
        Assert.NotNull(view.Duel);
        Assert.Equal(2, view.Duel!.Seats.Count);
        Assert.NotNull(view.Duel.Seats[0].Cards);
        Assert.Equal(5, view.Duel.Seats[0].Cards!.Count);
        Assert.Null(view.Duel.Seats[0].Selected);
        Assert.Null(view.Duel.Seats[1].Cards);
        Assert.Equal(a, view.Duel.Seats[0].UserId);
        Assert.Equal(3, view.Players[0].RubLeft);
        Assert.Equal(1, view.Players[0].ReplaceLeft);
        Assert.Equal(1, view.Players[0].PeekLeft);

        match.Act(a, "pick", 0, new[] { 1, 2, 3 });
        view = match.ViewFor(a);
        Assert.Equal(new[] { 1, 2, 3 }, view.Duel!.Seats[0].Selected);

        match.Act(a, "replace", 0, Array.Empty<int>());
        view = match.ViewFor(a);
        Assert.Null(view.Duel!.Seats[0].Selected);
    }

    [Fact]
    public void PeekRevealsOpponentHoleOnlyToViewer()
    {
        var match = OpenClassic();
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        AdvanceThrough(match);

        var a = match.Fighters[0].UserId;
        var duel = match.Duels.First(d => d.Involves(a));
        var b = PvpBattleTable.SameUser(duel.LeftUserId, a) ? duel.RightUserId : duel.LeftUserId;
        match.Act(a, "peek", 0, Array.Empty<int>());

        var viewA = match.ViewFor(a);
        var oppA = viewA.Duel!.Seats[1 - viewA.Duel.ViewerSeat];
        Assert.NotNull(oppA.Cards);
        Assert.Equal(5, oppA.Cards!.Count);

        var viewB = match.ViewFor(b);
        var oppB = viewB.Duel!.Seats[1 - viewB.Duel.ViewerSeat];
        Assert.Null(oppB.Cards);
        Assert.Equal(0, match.Fighters[0].PeekLeft);
        Assert.Throws<InvalidOperationException>(() => match.Act(a, "peek", 0, Array.Empty<int>()));
    }

    [Fact]
    public void RubReplacePeekTouchStateVersionWithoutEvents()
    {
        var match = OpenClassic();
        var a = match.Fighters[0].UserId;
        match.DrainEvents();
        var version = match.StateVersion;

        match.Act(a, "rub", 0, Array.Empty<int>());
        Assert.True(match.StateVersion > version);
        Assert.Empty(match.DrainEvents());
        Assert.Equal(match.StateVersion, match.ViewFor(a).StateVersion);
        version = match.StateVersion;

        match.Act(a, "peek", 0, Array.Empty<int>());
        Assert.True(match.StateVersion > version);
        Assert.Empty(match.DrainEvents());
        Assert.NotNull(match.ViewFor(a).Duel!.Seats[1 - match.ViewFor(a).Duel!.ViewerSeat].Cards);
        version = match.StateVersion;

        match.Act(a, "replace", 0, Array.Empty<int>());
        Assert.True(match.StateVersion > version);
        Assert.Empty(match.DrainEvents());
        Assert.Equal(0, match.Fighters[0].ReplaceLeft);
    }

    [Fact]
    public void RubReplaceConsumeAndResetNextRound()
    {
        var match = OpenClassic();
        var a = match.Fighters[0].UserId;
        var before = match.Duels[0].Engine.Snapshot.Hands[0][0];
        match.Act(a, "rub", 0, Array.Empty<int>());
        Assert.NotEqual(before, match.Duels[0].Engine.Snapshot.Hands[0][0]);
        Assert.Equal(2, match.Fighters[0].RubLeft);
        match.Act(a, "replace", 0, Array.Empty<int>());
        Assert.Equal(0, match.Fighters[0].ReplaceLeft);
        Assert.Throws<InvalidOperationException>(() => match.Act(a, "replace", 0, Array.Empty<int>()));

        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        AdvanceThrough(match);

        Assert.Equal(2, match.Round);
        Assert.Equal(3, match.Fighters[0].RubLeft);
        Assert.Equal(1, match.Fighters[0].ReplaceLeft);
        Assert.Equal(1, match.Fighters[0].PeekLeft);
    }

    [Fact]
    public void MonsterAutoPicksBestThreeOfFive()
    {
        var match = OpenClassic();
        var engine = match.Duels[0].Engine;
        var hole = engine.Snapshot.Hands[1];
        Assert.Equal(5, hole.Count);
        var cards = new CardShare.Battle.Card[5];
        for (var i = 0; i < 5; i++)
        {
            cards[i] = hole[i];
        }

        var flags = new bool[5];
        HandEvaluator.Tables = PvpTestTables.Classic();
        HandEvaluator.SelectBestOpen(cards, flags, 5);
        var expected = new List<int>();
        for (var i = 0; i < flags.Length; i++)
        {
            if (flags[i])
            {
                expected.Add(i);
            }
        }

        Assert.Equal(expected, engine.Snapshot.Picked[1]);
    }

    [Fact]
    public void SingleRoundModeFinishesAndRanksByHp()
    {
        var tables = PvpTestTables.OneMonsterRound();
        var match = Open(tables);
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);
        AdvanceThrough(match);

        Assert.Equal(PvpMatch.PhaseFinished, match.Phase);
        Assert.Equal(4, match.Fighters.Count(f => f.Rank >= 1));
        Assert.Contains(match.Fighters, f => f.Rank == 1);
    }

    [Fact]
    public void OneHumanAndThreeBotsAutoResolveOtherTables()
    {
        var you = Pub("你");
        var match = Open(
            PvpTestTables.Classic(),
            new[] { you, Bot("甲"), Bot("乙"), Bot("丙") });
        Assert.Equal(1, match.Round);
        Assert.Equal(PvpFightKind.Monster, match.FightKind);
        Assert.Equal(1, match.Duels.Count(d => !d.Resolved));
        Assert.Equal(3, match.Duels.Count(d => d.Resolved));
        Assert.Contains(match.ViewFor(you.UserId).Players, p => p.IsBot);

        match.Showdown(you.UserId);
        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);
        AdvanceThrough(match);

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpFightKind.Pvp, match.FightKind);
        Assert.Equal(1, match.Duels.Count(d => !d.Resolved));
        Assert.True(match.Duels.First(d => !d.Resolved).Involves(you.UserId));
    }

    [Fact]
    public void UnpickedSeatsAutoPickAtShowdown()
    {
        var you = Pub("你");
        var match = Open(
            PvpTestTables.Classic(),
            new[] { you, Bot("甲"), Bot("乙"), Bot("丙") });

        // 发牌后（选牌阶段）双方都未显式选牌。
        var duel = match.Duels.First(d => d.Involves(you.UserId));
        var snap = duel.Engine.Snapshot;
        Assert.False(snap.PickedExplicit[0]);
        Assert.False(snap.PickedExplicit[1]);

        // 亮牌流程：真人没选自动补 3 张，野怪取最高牌型 3 张，都显式化。
        match.Showdown(you.UserId);
        Assert.True(duel.Resolved);
        snap = duel.Engine.Snapshot;
        Assert.True(snap.PickedExplicit[0]);
        Assert.True(snap.PickedExplicit[1]);
        Assert.Equal(BattleLimits.OpenHandSize, snap.Picked[0].Count);
        Assert.Equal(BattleLimits.OpenHandSize, snap.Picked[1].Count);
        Assert.Equal(BattleLimits.OpenHandSize, snap.Picked[1].Distinct().Count());

        AdvanceThrough(match);

        // PVP 轮：只锁真人、机器人座位未选时，亮牌同样给机器人补最高牌型。
        var botDuel = match.Duels.First(d => d.Involves(you.UserId));
        snap = botDuel.Engine.Snapshot;
        var botSeat = PvpBattleTable.SameUser(botDuel.LeftUserId, you.UserId) ? 1 : 0;
        Assert.False(snap.PickedExplicit[botSeat]);
        match.Showdown(you.UserId);
        Assert.True(botDuel.Resolved);
        snap = botDuel.Engine.Snapshot;
        Assert.True(snap.PickedExplicit[botSeat]);
        Assert.Equal(BattleLimits.OpenHandSize, snap.Picked[botSeat].Count);
        Assert.Equal(BattleLimits.OpenHandSize, snap.Picked[botSeat].Distinct().Count());
    }

    [Fact]
    public void BothHumansMustLockBeforeCompareThenLoserTakesScaledDamage()
    {
        var match = OpenClassic();
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        AdvanceThrough(match);

        Assert.Equal(2, match.Round);
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));
        var hpA = a.Hp;
        var hpB = b.Hp;

        match.Showdown(a.UserId);
        Assert.False(duel.Resolved);
        Assert.Equal(hpA, a.Hp);
        Assert.Equal(hpB, b.Hp);
        Assert.True(match.ViewFor(a.UserId).Duel!.Seats[match.ViewFor(a.UserId).Duel!.ViewerSeat].Locked);
        Assert.Throws<InvalidOperationException>(() => match.Act(a.UserId, "rub", 0, Array.Empty<int>()));

        match.Showdown(b.UserId);
        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        var expected = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 2));
        Assert.Equal(expected, match.ViewFor(a.UserId).DuelDamage);
        Assert.Equal(expected, match.ViewFor(b.UserId).DuelDamage);
        var loseId = snap.Winners[0] == 0 ? duel.RightUserId : duel.LeftUserId;
        if (PvpBattleTable.SameUser(loseId, a.UserId))
        {
            Assert.Equal(hpA - expected, a.Hp);
            Assert.Equal(hpB, b.Hp);
        }
        else
        {
            Assert.Equal(hpB - expected, b.Hp);
            Assert.Equal(hpA, a.Hp);
        }

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
        Assert.Contains(match.Duels, d => !d.Resolved);
    }

    [Fact]
    public void DuelLoserAtZeroHpIsRankedWhileOtherTablesContinue()
    {
        var match = Open(PvpTestTables.OnePvpRound(), new[] { Pub("甲"), Pub("乙"), Pub("丙"), Pub("丁") }, hp: 1);
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        match.Showdown(a.UserId);
        match.Showdown(bId);

        Assert.True(duel.Resolved);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
        Assert.Contains(match.Duels, d => !d.Resolved);
        var loser = match.Fighters.First(f => !f.Alive);
        Assert.Equal(0, loser.Hp);
        Assert.Equal(4, loser.Rank);
        Assert.Equal(3, match.Fighters.Count(f => f.Alive));
    }

    [Fact]
    public void PhaseTimeoutLocksUnlockedSeatsWithAutoPickAndSettles()
    {
        var match = Open(PvpTestTables.OnePvpRound());
        var a = match.Fighters[0].UserId;
        var view = match.ViewFor(a);
        Assert.InRange(view.PhaseDeadlineUtcMs - view.ServerNowUtcMs, 1, PvpTiming.DealAnimMs + 20_000);
        Assert.All(match.Duels, d => Assert.False(d.Resolved));

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.False(match.ApplyTimeouts(now));
        Assert.True(match.ApplyTimeouts(now + PvpTiming.DealAnimMs + 20_000 + 1));

        Assert.All(match.Duels, d => Assert.True(d.Resolved));
        Assert.All(match.Duels, d => Assert.True(d.HpApplied));
        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);

        // 最后一轮：settle 演出窗到点后才置 finished。
        Assert.True(match.ApplyTimeouts(now + PvpTiming.DealAnimMs + 20_000 + 1 + PvpTiming.SettleAnimMs + 1));
        Assert.Equal(PvpMatch.PhaseFinished, match.Phase);
        var after = match.ViewFor(a);
        Assert.Equal(0, after.PhaseDeadlineUtcMs);
        Assert.True(after.ServerNowUtcMs > 0);
        var duel = match.Duels.First(d => d.Involves(a));
        var snap = duel.Engine.Snapshot;
        if (snap.Winners.Count == 1)
        {
            var expected = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 1));
            Assert.Equal(expected, after.DuelDamage);
        }

        Assert.False(match.ApplyTimeouts(now + 40_000));
    }

    [Fact]
    public void WinnerEarnsGoldAndLoserGetsHalf()
    {
        var match = Open(PvpTestTables.OnePvpRound());
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));
        var goldA = a.Gold;
        var goldB = b.Gold;

        match.Showdown(a.UserId);
        match.Showdown(b.UserId);

        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        var damage = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 1));
        // 胜 = GoldBase(15) + damage/12 + 20×未用技能(3+1+1)。
        var winGold = 15 + damage / 12 + 20 * (3 + 1 + 1);
        var aWon = snap.Winners[0] == (PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? 0 : 1);
        if (aWon)
        {
            Assert.Equal(goldA + winGold, a.Gold);
            Assert.Equal(goldB + winGold / 2, b.Gold);
        }
        else
        {
            Assert.Equal(goldB + winGold, b.Gold);
            Assert.Equal(goldA + winGold / 2, a.Gold);
        }
    }

    [Fact]
    public void MonsterRoundPlayerWinAlsoPaysGold()
    {
        var match = OpenClassic();
        var goldBefore = match.Fighters.Select(f => f.Gold).ToArray();
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        for (var i = 0; i < match.Fighters.Count; i++)
        {
            var fighter = match.Fighters[i];
            var duel = match.Duels.First(d => d.Involves(fighter.UserId));
            var snap = duel.Engine.Snapshot;
            if (snap.Winners == null || snap.Winners.Count != 1)
            {
                Assert.Equal(goldBefore[i], fighter.Gold);
                continue;
            }

            var damage = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 1));
            if (snap.Winners[0] == 0)
            {
                // 野怪轮玩家胜：GoldBase(10) + damage/12 + 20×未用技能(3+1+1)。
                Assert.Equal(goldBefore[i] + 10 + damage / 12 + 20 * (3 + 1 + 1), fighter.Gold);
            }
            else
            {
                // 败给野怪：半额（野怪无技能加成）。
                Assert.Equal(goldBefore[i] + (10 + damage / 12) / 2, fighter.Gold);
            }
        }
    }

    [Fact]
    public void SettleThenShopThenNextRoundByDeadlines()
    {
        var match = Open(PvpTestTables.Classic(), shopPool: new[] { 1, 2 });
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);
        var a = match.Fighters[0].UserId;
        Assert.Throws<InvalidOperationException>(() => match.Act(a, "pick", 0, new[] { 1, 2, 3 }));
        Assert.Throws<InvalidOperationException>(() => match.Act(a, "buy", 1, Array.Empty<int>()));

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.False(match.ApplyTimeouts(now + 1_000));
        Assert.True(match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1));

        Assert.Equal(PvpMatch.PhaseShop, match.Phase);
        var view = match.ViewFor(a);
        Assert.NotNull(view.Shop);
        Assert.True(view.Shop!.OfferIds.Length > 0);
        Assert.True(view.Shop.OfferIds.Length <= PvpShopRules.OfferCount);
        Assert.Equal(view.Shop.OfferIds.Length, view.Shop.OfferPrices.Length);
        Assert.False(view.Shop.Done);
        Assert.InRange(view.PhaseDeadlineUtcMs - view.ServerNowUtcMs, 1, 30_000);

        Assert.True(match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1 + 30_000 + 1));
        Assert.Equal(2, match.Round);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
        Assert.Null(match.ViewFor(a).Shop);
    }

    [Fact]
    public void AllShopDoneAdvancesBeforeDeadline()
    {
        var match = Open(PvpTestTables.Classic(), shopPool: new[] { 1, 2 });
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);

        for (var i = 0; i < 3; i++)
        {
            match.Act(match.Fighters[i].UserId, "shop_done", 0, Array.Empty<int>());
            Assert.Equal(PvpMatch.PhaseShop, match.Phase);
        }

        match.Act(match.Fighters[3].UserId, "shop_done", 0, Array.Empty<int>());
        Assert.Equal(2, match.Round);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
        Assert.Throws<InvalidOperationException>(
            () => match.Act(match.Fighters[0].UserId, "shop_done", 0, Array.Empty<int>()));
    }

    [Fact]
    public void ShopBuySellRefreshAdjustGoldAndShelf()
    {
        var match = Open(PvpTestTables.Classic(), shopPool: new[] { 1, 2 });
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);

        var a = match.Fighters[0];
        var shop = match.ViewFor(a.UserId).Shop!;
        Assert.Equal(5, shop.RefreshCost);
        Assert.True(shop.OfferIds.Length > 0);

        var relicId = shop.OfferIds[0];
        var price = relicId == 1 ? 10 : 20;
        var sellPrice = relicId == 1 ? 5 : 8;
        var gold = a.Gold;
        match.Act(a.UserId, "buy", relicId, Array.Empty<int>());
        Assert.Equal(gold - price, a.Gold);
        Assert.Contains(relicId, a.OwnedRelicIds);
        Assert.DoesNotContain(relicId, a.ShopOfferIds);
        Assert.Contains(relicId, match.ViewFor(a.UserId).Players[0].RelicIds);
        Assert.Throws<InvalidOperationException>(() => match.Act(a.UserId, "buy", relicId, Array.Empty<int>()));

        match.Act(a.UserId, "sell", relicId, Array.Empty<int>());
        Assert.Equal(gold - price + sellPrice, a.Gold);
        Assert.DoesNotContain(relicId, a.OwnedRelicIds);

        gold = a.Gold;
        match.Act(a.UserId, "refresh", 0, Array.Empty<int>());
        Assert.Equal(gold - 5, a.Gold);
        Assert.Equal(1, a.ShopRefreshCount);
        Assert.True(a.ShopOfferIds.Count > 0);
    }

    [Fact]
    public void BoughtRelicPersistsIntoNextRoundView()
    {
        var match = Open(PvpTestTables.Classic(), shopPool: new[] { 1, 2 });
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        var a = match.Fighters[0];
        var relicId = match.ViewFor(a.UserId).Shop!.OfferIds[0];
        match.Act(a.UserId, "buy", relicId, Array.Empty<int>());
        foreach (var fighter in match.Fighters)
        {
            if (!fighter.ShopDone)
            {
                match.Act(fighter.UserId, "shop_done", 0, Array.Empty<int>());
            }
        }

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
        Assert.Contains(relicId, match.ViewFor(a.UserId).Players[0].RelicIds);
    }

    [Fact]
    public void FightDeadlineIncludesDealAnimationBuffer()
    {
        var match = Open(PvpTestTables.OnePvpRound());
        var view = match.ViewFor(match.Fighters[0].UserId);
        var budget = PvpTiming.DealAnimMs + 20_000;
        Assert.InRange(view.PhaseDeadlineUtcMs - view.ServerNowUtcMs, budget - 1_000, budget);
    }

    [Fact]
    public void StateVersionIncreasesAndEventsDrainOnce()
    {
        var match = Open(PvpTestTables.Classic(), shopPool: new[] { 1, 2 });
        Assert.True(match.StateVersion > 0);
        var events = match.DrainEvents();
        Assert.Contains(events, e => e.Kind == "round_start" && e.Round == 1);
        Assert.Empty(match.DrainEvents());

        var version = match.StateVersion;
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        Assert.True(match.StateVersion > version);
        events = match.DrainEvents();
        Assert.Contains(events, e => e.Kind == "duel_resolved");
        Assert.Contains(events, e => e.Kind == "settle_start");
        for (var i = 1; i < events.Count; i++)
        {
            Assert.True(events[i].Version > events[i - 1].Version);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "shop_start");

        foreach (var fighter in match.Fighters)
        {
            if (!fighter.ShopDone)
            {
                match.Act(fighter.UserId, "shop_done", 0, Array.Empty<int>());
            }
        }

        Assert.Contains(match.DrainEvents(), e => e.Kind == "round_start" && e.Round == 2);
        Assert.Equal(match.StateVersion, match.ViewFor(match.Fighters[0].UserId).StateVersion);
    }

    [Fact]
    public void SetDisconnectedEmitsPresenceEvents()
    {
        var match = OpenClassic();
        var a = match.Fighters[0].UserId;
        match.DrainEvents();

        match.SetDisconnected(a, true);
        Assert.True(match.Fighters[0].Disconnected);
        Assert.True(match.ViewFor(a).Players[0].Disconnected);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "player_offline" && e.UserId == a);

        match.SetDisconnected(a, false);
        Assert.False(match.Fighters[0].Disconnected);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "player_online" && e.UserId == a);
    }

    [Fact]
    public void DisconnectedSeatAutoLocksAndIsAutoPilotedNextRound()
    {
        var match = Open(PvpTestTables.Classic(), shopPool: new[] { 1, 2 });
        var a = match.Fighters[0].UserId;

        // 掉线：当轮座位立即自动锁定（野怪轮对面已锁，直接比牌结算）。
        match.SetDisconnected(a, true);
        var duel = match.Duels.First(d => d.Involves(a));
        Assert.True(duel.IsLocked(duel.ViewerSeat(a)));
        Assert.True(duel.Resolved);

        foreach (var fighter in match.Fighters)
        {
            if (!PvpBattleTable.SameUser(fighter.UserId, a))
            {
                match.Showdown(fighter.UserId);
            }
        }

        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);
        Assert.True(match.Fighters[0].ShopDone);

        foreach (var fighter in match.Fighters)
        {
            if (!fighter.ShopDone)
            {
                match.Act(fighter.UserId, "shop_done", 0, Array.Empty<int>());
            }
        }

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);

        // 次轮断线座位 IsHuman=false：发牌即自动锁定代打。
        var next = match.Duels.First(d => d.Involves(a));
        Assert.True(next.IsLocked(next.ViewerSeat(a)));

        // 重连：清标志并发 player_online。
        match.DrainEvents();
        match.SetDisconnected(a, false);
        Assert.False(match.Fighters[0].Disconnected);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "player_online" && e.UserId == a);
    }

    [Fact]
    public void FinishGrantsRankRewardsAndMarksGrantedOnce()
    {
        var match = Open(PvpTestTables.OneMonsterRound());
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        Assert.False(match.TryMarkRewardsGranted());
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseFinished, match.Phase);
        Assert.True(match.FinishedUtcMs > 0);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "match_finished");

        var rewards = new[] { 200, 120, 60, 30 };
        foreach (var fighter in match.Fighters)
        {
            Assert.InRange(fighter.Rank, 1, 4);
            Assert.Equal(rewards[fighter.Rank - 1], fighter.RewardGold);
            Assert.Equal(fighter.RewardGold, match.ViewFor(fighter.UserId).Players[fighter.SeatIndex].RewardGold);
        }

        Assert.True(match.TryMarkRewardsGranted());
        Assert.False(match.TryMarkRewardsGranted());
    }

    [Fact]
    public void RelicSkillCountBonusesApplyNextRound()
    {
        var match = Open(PvpTestTables.WithSkillEntries(), shopPool: new[] { 10, 12 });
        Assert.Equal(3, match.Fighters[0].RubLeft);

        var a = BuyAndAdvance(match, 10, 12);
        Assert.Equal(3 + 2, a.RubLeft);
        Assert.Equal(1 + 2, a.PeekLeft);
        Assert.Equal(1, a.ReplaceLeft);
        var view = match.ViewFor(a.UserId);
        Assert.Equal(5, view.Players[0].RubLeft);
        Assert.Equal(3, view.Players[0].PeekLeft);
    }

    [Fact]
    public void HeroSkillCountBonusesApplyFromFirstRound()
    {
        var rubHero = Open(PvpTestTables.WithSkillEntries(), heroId: 2);
        Assert.Equal(3 + 1, rubHero.Fighters[0].RubLeft);
        Assert.Equal(1, rubHero.Fighters[0].PeekLeft);

        var peekHero = Open(PvpTestTables.WithSkillEntries(), heroId: 3);
        Assert.Equal(1 + 1, peekHero.Fighters[0].PeekLeft);
        Assert.Equal(3, peekHero.Fighters[0].RubLeft);
    }

    [Fact]
    public void TalentSkillCountBonusAppliesFromFirstRound()
    {
        var talents = new[] { new CombatTalentCount { TalentId = 500, Count = 1 } };
        var match = Open(PvpTestTables.WithSkillEntries(), talents: talents);
        Assert.Equal(3 + 1, match.Fighters[0].RubLeft);
    }

    [Fact]
    public void NoSkillRelicAppliesOnlyAfterAllSkillsSpent()
    {
        // 技能有剩余：NoSkill 不触发。
        var tables = PvpTestTables.WithSkillEntries();
        var match = Open(tables, shopPool: new[] { 11, 13 });
        var a = BuyAndAdvance(match, 11, 13);
        Assert.Equal(0, a.RubLeft);
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        match.Showdown(a.UserId);
        match.Showdown(bId);
        var snap = duel.Engine.Snapshot;
        var seat = duel.ViewerSeat(a.UserId);
        var expected = Math.Max(1, (int)Math.Round(10 * snap.Scores[seat].Multiplier));
        Assert.Equal(expected, snap.Damages[seat]);

        // 用光透视+换牌（搓牌被"钝手"归零）：NoSkill +3 倍率。
        var match2 = Open(tables, shopPool: new[] { 11, 13 });
        var a2 = BuyAndAdvance(match2, 11, 13);
        match2.Act(a2.UserId, "peek", 0, Array.Empty<int>());
        match2.Act(a2.UserId, "replace", 0, Array.Empty<int>());
        Assert.Equal(0, a2.RubLeft);
        Assert.Equal(0, a2.PeekLeft);
        Assert.Equal(0, a2.ReplaceLeft);
        var duel2 = match2.Duels.First(d => d.Involves(a2.UserId));
        var b2Id = PvpBattleTable.SameUser(duel2.LeftUserId, a2.UserId) ? duel2.RightUserId : duel2.LeftUserId;
        match2.Showdown(a2.UserId);
        match2.Showdown(b2Id);
        var snap2 = duel2.Engine.Snapshot;
        var seat2 = duel2.ViewerSeat(a2.UserId);
        var expected2 = Math.Max(1, (int)Math.Round(10 * (snap2.Scores[seat2].Multiplier + 3f)));
        Assert.Equal(expected2, snap2.Damages[seat2]);
    }

    [Fact]
    public void EveryRubbingNumAddsAttackPerRubLeft()
    {
        var match = Open(PvpTestTables.WithSkillEntries(), shopPool: new[] { 14 });
        var a = BuyAndAdvance(match, 14);
        Assert.Equal(3, a.RubLeft);
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        match.Showdown(a.UserId);
        match.Showdown(bId);
        var snap = duel.Engine.Snapshot;
        var seat = duel.ViewerSeat(a.UserId);
        // 每剩余 1 搓牌 +2 攻击：攻击 10 + 3×2。
        var expected = Math.Max(1, (int)Math.Round((10 + 3 * 2) * snap.Scores[seat].Multiplier));
        Assert.Equal(expected, snap.Damages[seat]);
    }

    [Fact]
    public void MonsterRoundWinHealsTenPercentMaxHp()
    {
        var wins = 0;
        foreach (var seed in new[] { 42, 7, 1001 })
        {
            var match = OpenClassic(seed);
            foreach (var fighter in match.Fighters)
            {
                fighter.Hp = 50;
            }

            foreach (var fighter in match.Fighters)
            {
                match.Showdown(fighter.UserId);
            }

            foreach (var fighter in match.Fighters)
            {
                var duel = match.Duels.First(d => d.Involves(fighter.UserId));
                var snap = duel.Engine.Snapshot;
                if (snap.Winners == null || snap.Winners.Count != 1)
                {
                    Assert.Equal(50, fighter.Hp);
                    continue;
                }

                if (snap.Winners[0] == 0)
                {
                    wins++;
                    Assert.Equal(50 + 10, fighter.Hp);
                }
                else
                {
                    var damage = (int)Math.Floor(snap.Damages[1] * (1f + 0.1f * 1));
                    Assert.Equal(Math.Max(0, 50 - damage), fighter.Hp);
                }
            }
        }

        Assert.True(wins > 0);

        // 满血不溢出。
        var full = OpenClassic();
        foreach (var fighter in full.Fighters)
        {
            full.Showdown(fighter.UserId);
        }

        foreach (var fighter in full.Fighters)
        {
            var duel = full.Duels.First(d => d.Involves(fighter.UserId));
            var snap = duel.Engine.Snapshot;
            if (snap.Winners != null && snap.Winners.Count == 1 && snap.Winners[0] == 0)
            {
                Assert.Equal(fighter.MaxHp, fighter.Hp);
            }
        }
    }

    private static PvpMatch OpenClassic(int seed = 42) => Open(PvpTestTables.Classic(), seed: seed);

    private static PvpMatch Open(
        GameTables tables,
        int[]? shopPool = null,
        int heroId = 1,
        IReadOnlyList<CombatTalentCount>? talents = null,
        int hp = 0,
        int seed = 42)
        => Open(
            tables,
            new[] { Pub("甲"), Pub("乙"), Pub("丙"), Pub("丁") },
            hp: hp,
            shopPool: shopPool,
            heroId: heroId,
            talents: talents,
            seed: seed);

    private static PvpMatch Open(
        GameTables tables,
        PlayerPublic[] players,
        int hp = 0,
        int[]? shopPool = null,
        int heroId = 1,
        IReadOnlyList<CombatTalentCount>? talents = null,
        int seed = 42)
    {
        var seats = new SeatSetup[players.Length];
        for (var i = 0; i < players.Length; i++)
        {
            seats[i] = CombatBonuses.BuildSeat(i, players[i].UserId, players[i].NickName, heroId, talents ?? Array.Empty<CombatTalentCount>(), tables);
            seats[i].IsHuman = !players[i].IsBot;
            seats[i].ShopPoolIds = shopPool ?? Array.Empty<int>();
            if (hp > 0)
            {
                seats[i].Hp = hp;
                seats[i].MaxHp = hp;
            }
        }

        return PvpMatch.Open(Guid.NewGuid(), seed, players, tables, seats, 1);
    }

    /// <summary>第 1 轮全亮 → settle 到点进商店 → fighter[0] 买圣物 → 全员 shop_done 进第 2 轮。</summary>
    private static PvpFighter BuyAndAdvance(PvpMatch match, params int[] relicIds)
    {
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);

        var a = match.Fighters[0];
        foreach (var relicId in relicIds)
        {
            match.Act(a.UserId, "buy", relicId, Array.Empty<int>());
        }

        foreach (var fighter in match.Fighters)
        {
            if (!fighter.ShopDone)
            {
                match.Act(fighter.UserId, "shop_done", 0, Array.Empty<int>());
            }
        }

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
        return a;
    }

    /// <summary>settle/shop 阶段由 deadline 驱动：用远期时间戳推进到下一个 fight 或 finished。</summary>
    private static void AdvanceThrough(PvpMatch match)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < 4 && match.Phase != PvpMatch.PhaseFight && match.Phase != PvpMatch.PhaseFinished; i++)
        {
            match.ApplyTimeouts(now + 600_000);
        }
    }

    private static PlayerPublic Pub(string nick)
        => new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = nick };

    private static PlayerPublic Bot(string nick)
        => new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = nick, IsBot = true };
}

internal static class PvpTestTables
{
    public static GameTables Classic() => Build(ClassicRounds());

    /// <summary>带技能次数/NoSkill/EveryRubbingNum 词条的表：圣物 10-14、英雄 2/3、天赋 500。</summary>
    public static GameTables WithSkillEntries()
    {
        var fallback = GameTables.Fallback();
        var relics = fallback.Relics.Concat(new[]
        {
            new RelicConfig { Id = 10, Name = "搓牌护手", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 1001 } },
            new RelicConfig { Id = 11, Name = "无技之刃", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 1002 } },
            new RelicConfig { Id = 12, Name = "透视眼镜", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 1003 } },
            new RelicConfig { Id = 13, Name = "钝手", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 1004 } },
            new RelicConfig { Id = 14, Name = "余搓之刃", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 1005 } }
        }).ToArray();
        var relicEntries = new[]
        {
            new RelicEntryConfig { Id = 1001, Type = MechanismType.RubbingCardsNum, Value = new[] { 2f } },
            new RelicEntryConfig { Id = 1002, Type = MechanismType.NoSkill, Value = new[] { 3f } },
            new RelicEntryConfig { Id = 1003, Type = MechanismType.PerspectiveNum, Value = new[] { 2f } },
            new RelicEntryConfig { Id = 1004, Type = MechanismType.RubbingCardsNum, Value = new[] { -3f } },
            new RelicEntryConfig { Id = 1005, Type = MechanismType.EveryRubbingNum, Value = new[] { 2f } }
        };
        var heroes = new[]
        {
            new HeroConfig { Id = 1, HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = Array.Empty<int>() },
            new HeroConfig { Id = 2, Name = "赌神", HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = new[] { 2001 } },
            new HeroConfig { Id = 3, Name = "阴阳师", HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = new[] { 2002 } }
        };
        var heroEntries = new[]
        {
            new HeroEntryConfig { Id = 2001, Type = MechanismType.RubbingCardsNum, Value = new[] { 1f } },
            new HeroEntryConfig { Id = 2002, Type = MechanismType.PerspectiveNum, Value = new[] { 1f } }
        };
        var talentRows = fallback.TalentRows.Concat(new[]
        {
            new TalentConfig { Id = 10, TalentId = 500, TalentLevel = 1, TalentEntry = 3001 }
        }).ToArray();
        var talentEntries = new[]
        {
            new TalentEntryConfig { Id = 3001, Type = MechanismType.RubbingCardsNum, Value = 1f }
        };
        return Build(
            ClassicRounds(),
            heroes: heroes,
            relics: relics,
            relicEntries: relicEntries,
            heroEntries: heroEntries,
            talentRows: talentRows,
            talentEntries: talentEntries);
    }

    private static PvpRoundConfig[] ClassicRounds()
    {
        return new PvpRoundConfig[]
        {
            Round(101, 1, PvpFightKind.Monster, 10011, 10),
            Round(102, 2, PvpFightKind.Pvp, 0, 15),
            Round(103, 3, PvpFightKind.Pvp, 0, 15),
            Round(104, 4, PvpFightKind.Pvp, 0, 15),
            Round(105, 5, PvpFightKind.Monster, 10041, 20),
            Round(106, 6, PvpFightKind.Pvp, 0, 15),
            Round(107, 7, PvpFightKind.Pvp, 0, 15),
            Round(108, 8, PvpFightKind.Pvp, 0, 15),
            Round(109, 9, PvpFightKind.Monster, 10071, 25),
            Round(110, 10, PvpFightKind.Pvp, 0, 15),
            Round(111, 11, PvpFightKind.Pvp, 0, 15),
            Round(112, 12, PvpFightKind.Pvp, 0, 15),
            Round(113, 13, PvpFightKind.Monster, 10101, 30)
        };
    }

    public static GameTables OneMonsterRound()
        => Build(new[] { Round(1, 1, PvpFightKind.Monster, 10011, 10) });

    public static GameTables OnePvpRound()
        => Build(new[] { Round(1, 1, PvpFightKind.Pvp, 0, 15) });

    private static PvpRoundConfig Round(int id, int round, PvpFightKind kind, int group, int gold)
        => new PvpRoundConfig
        {
            Id = id,
            ModeId = 1,
            Round = round,
            FightKind = kind,
            MonsterGroup = group,
            GoldBase = gold
        };

    private static GameTables Build(
        IReadOnlyList<PvpRoundConfig> rounds,
        IReadOnlyList<HeroConfig>? heroes = null,
        IReadOnlyList<RelicConfig>? relics = null,
        IReadOnlyList<RelicEntryConfig>? relicEntries = null,
        IReadOnlyList<HeroEntryConfig>? heroEntries = null,
        IReadOnlyList<TalentConfig>? talentRows = null,
        IReadOnlyList<TalentEntryConfig>? talentEntries = null)
    {
        var fallback = GameTables.Fallback();
        return new GameTables(
            fallback.GameConst,
            fallback.HandScores,
            fallback.Levels,
            heroes ?? new[]
            {
                new HeroConfig
                {
                    Id = 1,
                    HeroDamage = 10,
                    Hp = 100,
                    Critical = 0f,
                    CriticalDamage = 2f,
                    HeroEntryId = Array.Empty<int>()
                }
            },
            relics ?? fallback.Relics,
            fallback.UnlockConditions,
            talentRows ?? fallback.TalentRows,
            new[]
            {
                new MonsterConfig
                {
                    Id = 1,
                    MonsterId = 1001,
                    MonsterLevel = 1,
                    MonsterHp = 40,
                    MonsterDamage = 8,
                    Name = "屎莱姆"
                }
            },
            fallback.Items,
            "pvp-test",
            heroEntries ?? fallback.HeroEntries,
            talentEntries ?? fallback.TalentEntries,
            relicEntries ?? fallback.RelicEntries,
            new[] { new MonsterGroupConfig { Id = 10011, MonsterId = 1001, MonsterLevel = 1 } },
            new[]
            {
                new PvpModeConfig
                {
                    Id = 1,
                    Name = "经典",
                    PlayerCount = 4,
                    InitialGold = 150,
                    ShopSeconds = 30,
                    SkipMonsterWhenTwoLeft = true,
                    RankReward = new[] { 200, 120, 60, 30 },
                    DamageRoundScale = 0.1f,
                    OpenPhaseSeconds = 20
                }
            },
            rounds);
    }
}
