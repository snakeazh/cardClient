using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using Xunit;

namespace CardShare.Domain.Tests;

public class PvpScheduleTests
{
    [Fact]
    public void RoundPatternRepeatsWithoutARoundTable()
    {
        var tables = PvpTestTables.Classic();
        Assert.True(PvpSchedule.TryLoad(tables, 1, out var mode));
        Assert.Equal("经典", mode.Name);
        Assert.Equal(150, mode.InitialGold);
        Assert.Equal(PvpFightKind.Monster, PvpSchedule.Kind(mode, 1, 4));
        Assert.Equal(PvpFightKind.Pvp, PvpSchedule.Kind(mode, 2, 4));
        Assert.Equal(PvpFightKind.Monster, PvpSchedule.Kind(mode, 5, 4));
        Assert.Equal(PvpFightKind.Monster, PvpSchedule.Kind(mode, 17, 4));
        Assert.Equal(10, PvpSchedule.GoldBase(1));
        Assert.Equal(15, PvpSchedule.GoldBase(2));
        Assert.Equal(20, PvpSchedule.GoldBase(5));
        Assert.Equal(30, PvpSchedule.GoldBase(13));
        Assert.Equal(35, PvpSchedule.GoldBase(17));
        Assert.Equal(10011, PvpSchedule.MonsterGroup(tables, 1));
        Assert.Equal(10011, PvpSchedule.MonsterGroup(tables, 5));
    }

    [Fact]
    public void TwoAliveSkipsMonsterWhenModeSaysSo()
    {
        var tables = PvpTestTables.Classic();
        Assert.True(tables.TryGetPvpMode(1, out var mode));
        Assert.Equal(PvpFightKind.Pvp, PvpSchedule.Kind(mode, 5, 2));
        Assert.Equal(PvpFightKind.Monster, PvpSchedule.Kind(mode, 5, 4));
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
    public void ThreeAliveByeMirrorsTheDeadSeat()
    {
        var slots = PvpPairing.ForPvp(new[] { 0, 1, 2 }, 0);
        Assert.Contains(slots, s => !s.IsMonster && !s.IsMirror && s.LeftSeat == 0 && s.RightSeat == 1);
        Assert.Contains(slots, s => s.IsMirror && !s.IsMonster && s.LeftSeat == 2 && s.MirrorSeat == 3);
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
    public void DebugGrantAddsRelicAndTouchesVersion()
    {
        var match = OpenClassic();
        var a = match.Fighters[0].UserId;
        match.DrainEvents();
        var version = match.StateVersion;
        match.Act(a, "debug_grant", 1, Array.Empty<int>());
        Assert.Contains(1, match.Fighters[0].OwnedRelicIds);
        Assert.True(match.StateVersion > version);
        Assert.Contains(1, match.ViewFor(a).Players[0].RelicIds);
        Assert.Throws<InvalidOperationException>(() => match.Act(a, "debug_grant", 1, Array.Empty<int>()));
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
    public void RubReplacePersistAcrossRoundsUntilShopOrNewMatch()
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
        // 同局跨轮不回满：搓牌/替换保持用后剩余，透视未用仍为 1。
        Assert.Equal(2, match.Fighters[0].RubLeft);
        Assert.Equal(0, match.Fighters[0].ReplaceLeft);
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
        var cap = HandEvaluator.TypeLevel(CardShare.Battle.HandType.Flush);
        HandEvaluator.SelectBestOpen(cards, flags, 5, maxHandScoreLevel: cap);
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
    public void MonsterBestOpenStopsAtFlushWhenTripsExist()
    {
        var cards = new[]
        {
            new Card(Suit.Heart, Rank.Ace),
            new Card(Suit.Heart, Rank.King),
            new Card(Suit.Heart, Rank.Nine),
            new Card(Suit.Spade, Rank.Ace),
            new Card(Suit.Club, Rank.Ace)
        };
        HandEvaluator.Tables = PvpTestTables.Classic();
        var cap = HandEvaluator.TypeLevel(CardShare.Battle.HandType.Flush);
        var flags = new bool[cards.Length];
        var score = HandEvaluator.SelectBestOpen(cards, flags, cards.Length, maxHandScoreLevel: cap);
        Assert.Equal(CardShare.Battle.HandType.Flush, score.Type);
        Assert.True(score.Level <= cap);
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

        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
        Assert.Equal(2, match.Round);
        Assert.Equal(4, match.Fighters.Count(f => f.Alive));
        Assert.All(match.Fighters, f => Assert.Equal(0, f.Rank));
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
        Assert.All(match.Duels, d => Assert.False(d.Resolved));
        Assert.All(match.Duels, d => Assert.True(d.IsLocked(1)));
        Assert.Contains(match.ViewFor(you.UserId).Players, p => p.IsBot);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(match.ApplyTimeouts(now + PvpTiming.DealAnimMs + PvpTiming.BotLockMaxMs));
        Assert.Equal(1, match.Duels.Count(d => !d.Resolved));
        Assert.True(match.Duels.First(d => !d.Resolved).Involves(you.UserId));

        match.Showdown(you.UserId);
        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);
        AdvanceThrough(match);

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpFightKind.Pvp, match.FightKind);
        Assert.All(match.Duels, d => Assert.False(d.Resolved));

        now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(match.ApplyTimeouts(now + PvpTiming.DealAnimMs + PvpTiming.BotLockMaxMs));
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

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(match.ApplyTimeouts(now + PvpTiming.DealAnimMs + PvpTiming.BotLockMaxMs));
        Assert.NotEqual(PvpMatch.PhaseFight, match.Phase);
        AdvanceThrough(match);

        // PVP 轮：机器人尚未到点，真人亮牌前它还没显式选牌；亮牌后补最高牌型。
        var botDuel = match.Duels.First(d => d.Involves(you.UserId));
        snap = botDuel.Engine.Snapshot;
        var botSeat = PvpBattleTable.SameUser(botDuel.LeftUserId, you.UserId) ? 1 : 0;
        Assert.False(snap.PickedExplicit[botSeat]);
        match.Showdown(you.UserId);
        Assert.False(botDuel.Resolved);
        now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(match.ApplyTimeouts(now + PvpTiming.DealAnimMs + PvpTiming.BotLockMaxMs));
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
        var match = Open(PvpTestTables.OnePvpRound(), new[] { Pub("甲"), Pub("乙"), Pub("丙"), Pub("丁") });
        AdvanceToPvpFight(match, hpOnPvpRound: 1);
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

        // 这一轮打完仍有多人存活：settle 到点进商店，不按血量结束。
        Assert.True(match.ApplyTimeouts(now + PvpTiming.DealAnimMs + 20_000 + 1 + PvpTiming.SettleAnimMs + 1));
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);
        var after = match.ViewFor(a);
        Assert.True(after.PhaseDeadlineUtcMs > 0);
        Assert.True(after.ServerNowUtcMs > 0);
        var duel = match.Duels.First(d => d.Involves(a));
        var snap = duel.Engine.Snapshot;
        if (snap.Winners.Count == 1)
        {
            var expected = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 1));
            Assert.Equal(expected, after.DuelDamage);
        }

        Assert.False(match.ApplyTimeouts(after.PhaseDeadlineUtcMs - 1));
        var timed = duel.Engine.Snapshot;
        HandEvaluator.Tables = PvpTestTables.OnePvpRound();
        for (var seat = 0; seat <= 1; seat++)
        {
            var hole = timed.Hands[seat];
            var cards = new Card[hole.Count];
            for (var i = 0; i < hole.Count; i++)
            {
                cards[i] = hole[i];
            }

            var flags = new bool[cards.Length];
            HandEvaluator.SelectBestOpen(cards, flags, cards.Length);
            var expected = new List<int>();
            for (var i = 0; i < flags.Length; i++)
            {
                if (flags[i])
                {
                    expected.Add(i);
                }
            }

            Assert.Equal(expected, timed.Picked[seat]);
        }
    }

    [Fact]
    public void WinnerEarnsGoldAndLoserGetsHalf()
    {
        var match = Open(PvpTestTables.OnePvpRound());
        AdvanceToPvpFight(match);
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
        // 第 2 轮是 PvP：基础 15 + 20×未用技能(5) + 连胜 1(0) + 按发奖前金币算的利息。
        var winBody = 15 + 20 * (3 + 1 + 1);
        var loseBody = winBody / 2;
        var aWon = snap.Winners[0] == (PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? 0 : 1);
        if (aWon)
        {
            Assert.Equal(goldA + winBody + PvpSettlementRules.InterestGold(goldA), a.Gold);
            Assert.Equal(goldB + loseBody + PvpSettlementRules.InterestGold(goldB), b.Gold);
            Assert.Equal(1, a.WinStreak);
            Assert.Equal(0, a.LoseStreak);
            Assert.Equal(1, b.LoseStreak);
            Assert.Equal(0, b.WinStreak);
        }
        else
        {
            Assert.Equal(goldB + winBody + PvpSettlementRules.InterestGold(goldB), b.Gold);
            Assert.Equal(goldA + loseBody + PvpSettlementRules.InterestGold(goldA), a.Gold);
            Assert.Equal(1, b.WinStreak);
            Assert.Equal(1, a.LoseStreak);
        }
    }

    [Fact]
    public void SecondPvpWinPaysStreakAndLossStreakSeparately()
    {
        var match = Open(PvpTestTables.OnePvpRound());
        AdvanceToPvpFight(match);
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));
        a.WinStreak = 1;
        b.WinStreak = 1;
        var goldA = a.Gold;
        var goldB = b.Gold;

        match.Showdown(a.UserId);
        match.Showdown(b.UserId);

        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        // 胜者连胜到 2：+20；败者连败到 1：+0。
        var winBody = 15 + 100 + 20;
        var loseBody = (15 + 100) / 2;
        var aWon = snap.Winners[0] == (PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? 0 : 1);
        if (aWon)
        {
            Assert.Equal(2, a.WinStreak);
            Assert.Equal(0, a.LoseStreak);
            Assert.Equal(1, b.LoseStreak);
            Assert.Equal(goldA + winBody + PvpSettlementRules.InterestGold(goldA), a.Gold);
            Assert.Equal(goldB + loseBody + PvpSettlementRules.InterestGold(goldB), b.Gold);
        }
        else
        {
            Assert.Equal(2, b.WinStreak);
            Assert.Equal(1, a.LoseStreak);
            Assert.Equal(goldB + winBody + PvpSettlementRules.InterestGold(goldB), b.Gold);
            Assert.Equal(goldA + loseBody + PvpSettlementRules.InterestGold(goldA), a.Gold);
        }
    }

    [Fact]
    public void StreakAndInterestTables()
    {
        Assert.Equal(0, PvpSettlementRules.StreakGold(1));
        Assert.Equal(20, PvpSettlementRules.StreakGold(2));
        Assert.Equal(40, PvpSettlementRules.StreakGold(3));
        Assert.Equal(60, PvpSettlementRules.StreakGold(4));
        Assert.Equal(80, PvpSettlementRules.StreakGold(5));
        Assert.Equal(100, PvpSettlementRules.StreakGold(6));
        Assert.Equal(100, PvpSettlementRules.StreakGold(9));

        Assert.Equal(0, PvpSettlementRules.InterestGold(99));
        Assert.Equal(10, PvpSettlementRules.InterestGold(100));
        Assert.Equal(10, PvpSettlementRules.InterestGold(150));
        Assert.Equal(40, PvpSettlementRules.InterestGold(499));
        Assert.Equal(50, PvpSettlementRules.InterestGold(500));
        Assert.Equal(50, PvpSettlementRules.InterestGold(900));

        // 败方半额只砍基础+技能，连败和利息全额。
        Assert.Equal(15 + 40 + 20 + 10, PvpSettlementRules.RoundGold(15, 20, 2, 2, 150, winner: true));
        Assert.Equal((15 + 40) / 2 + 20 + 10, PvpSettlementRules.RoundGold(15, 20, 2, 2, 150, winner: false));
    }

    [Fact]
    public void MonsterRoundPlayerWinAlsoPaysGold()
    {
        var match = OpenClassic();
        foreach (var fighter in match.Fighters)
        {
            fighter.WinStreak = 4;
            fighter.LoseStreak = 0;
        }

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

            // 野怪轮改连胜连败次数，但不发连胜连败奖励。利息按发奖前 150。
            if (snap.Winners[0] == 0)
            {
                Assert.Equal(5, fighter.WinStreak);
                Assert.Equal(0, fighter.LoseStreak);
                Assert.Equal(goldBefore[i] + 10 + 20 * (3 + 1 + 1) + 10, fighter.Gold);
            }
            else
            {
                Assert.Equal(0, fighter.WinStreak);
                Assert.Equal(1, fighter.LoseStreak);
                Assert.Equal(goldBefore[i] + (10 + 20 * (3 + 1 + 1)) / 2 + 10, fighter.Gold);
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
        var onShelf = 0;
        for (var i = 0; i < a.ShopOfferIds.Count; i++)
        {
            if (a.ShopOfferIds[i] == relicId)
            {
                onShelf++;
            }
        }

        var gold = a.Gold;
        match.Act(a.UserId, "buy", relicId, Array.Empty<int>());
        Assert.Equal(gold - price, a.Gold);
        Assert.Contains(relicId, a.OwnedRelicIds);
        var leftOnShelf = 0;
        for (var i = 0; i < a.ShopOfferIds.Count; i++)
        {
            if (a.ShopOfferIds[i] == relicId)
            {
                leftOnShelf++;
            }
        }

        Assert.Equal(onShelf - 1, leftOnShelf);
        Assert.Contains(relicId, match.ViewFor(a.UserId).Players[0].RelicIds);
        if (leftOnShelf == 0)
        {
            Assert.Throws<InvalidOperationException>(() => match.Act(a.UserId, "buy", relicId, Array.Empty<int>()));
        }

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
    public void CopiesStaySeparateSlots()
    {
        var fighter = new PvpFighter();
        PvpRelicBag.Add(fighter, 7);
        PvpRelicBag.Add(fighter, 7);
        PvpRelicBag.Add(fighter, 7);
        Assert.Equal(3, fighter.OwnedRelicIds.Count);
        Assert.All(fighter.OwnedRelicPower, p => Assert.Equal(1, p));
        Assert.Equal(3, PvpRelicBag.Expand(fighter).Count);
        Assert.Equal(1, PvpRelicBag.RemoveOne(fighter, 7));
        Assert.Equal(2, fighter.OwnedRelicIds.Count);
    }

    [Fact]
    public void BindingDamageRequiresEveryMember()
    {
        var relics = new[]
        {
            PvpTestTables.BondRelic(101),
            PvpTestTables.BondRelic(201),
            PvpTestTables.BondRelic(301),
            PvpTestTables.BondRelic(401)
        };
        var tables = PvpTestTables.WithRelics(
            relics,
            bindings: new[]
            {
                new BindingConfig { Id = 101, Type = BindingType.Leiting, Value = new[] { 1f, 0.4f } }
            });
        var fighter = new PvpFighter();
        fighter.OwnedRelicIds.Add(101);
        fighter.OwnedRelicIds.Add(201);
        fighter.OwnedRelicIds.Add(301);
        fighter.OwnedRelicIds.Add(101);
        Assert.Equal(0, PvpRelicBindings.BonusDamage(tables, fighter, 100, attack: 100, isCrit: true));

        fighter.OwnedRelicIds.Add(401);
        Assert.Equal(0, PvpRelicBindings.BonusDamage(tables, fighter, 100, attack: 100, isCrit: false));
        Assert.Equal(40, PvpRelicBindings.BonusDamage(tables, fighter, 100, attack: 100, isCrit: true));

        fighter.OwnedRelicIds.Remove(201);
        Assert.Equal(0, PvpRelicBindings.BonusDamage(tables, fighter, 100, attack: 100, isCrit: true));
    }

    [Fact]
    public void PvpPoolUsesOnlyPvpRelicTable()
    {
        var pveRelic = new RelicConfig
        {
            Id = 7,
            Name = "仅PVE",
            Type = QualityType.Ordinary,
            Price = 10,
            SellingPrice = 4,
            RefreshProbability = 1f,
            MechanismId = Array.Empty<int>()
        };
        var pvpRelic = new PvpRelicConfig
        {
            Id = 8,
            Name = "仅PVP",
            Type = QualityType.Ordinary,
            Price = 99,
            SellingPrice = 40,
            RefreshProbability = 1f,
            MechanismId = Array.Empty<int>()
        };

        // 显式空 PVP 表：有 RelicConfig 也不进 PVP 池。
        var emptyPvp = PvpTestTables.WithPvpRelics(
            new[] { pveRelic },
            Array.Empty<PvpRelicConfig>());
        Assert.Empty(emptyPvp.PvpRelics);
        Assert.False(emptyPvp.TryGetPvpRelic(7, out _));
        Assert.Equal(0, PvpRelicStock.Create(emptyPvp).Available(7));

        var onlyPvp = PvpTestTables.WithPvpRelics(new[] { pveRelic }, new[] { pvpRelic });
        Assert.False(onlyPvp.TryGetPvpRelic(7, out _));
        Assert.True(onlyPvp.TryGetPvpRelic(8, out var fromPvp));
        Assert.Equal(99, fromPvp.Price);
        Assert.Single(onlyPvp.PvpRelics);
        Assert.Equal(6, PvpRelicStock.Create(onlyPvp).Available(8));

        // 商店池即便误塞 PVE Id，BuildPool 也只扫 PvpRelics，不会上架 7。
        var fighter = new PvpFighter
        {
            ShopPoolIds = new[] { 7, 8 },
            Combat = new SeatSetup { HeroId = 1 }
        };
        var stock = PvpRelicStock.Create(onlyPvp);
        PvpShopRules.FillOffers(fighter, onlyPvp, new Random(1), stock);
        Assert.DoesNotContain(7, fighter.ShopOfferIds);
        Assert.All(fighter.ShopOfferIds, id => Assert.Equal(8, id));
    }

    [Fact]
    public void SharedStockFollowsQualityAndReturnsOnSell()
    {
        var relic = new RelicConfig
        {
            Id = 7,
            Name = "公共",
            Type = QualityType.Ordinary,
            Price = 10,
            SellingPrice = 4,
            RefreshProbability = 1f,
            MechanismId = Array.Empty<int>()
        };
        var rare = new RelicConfig
        {
            Id = 8,
            Name = "稀有",
            Type = QualityType.Rare,
            Price = 10,
            SellingPrice = 4,
            RefreshProbability = 1f,
            MechanismId = Array.Empty<int>()
        };
        var consumable = new RelicConfig
        {
            Id = 9,
            Name = "消耗",
            Type = QualityType.Legend,
            Price = 10,
            SellingPrice = 4,
            RefreshProbability = 1f,
            UseType = 1,
            MechanismId = new[] { 4242 }
        };
        Assert.Equal(6, PvpRelicStock.BaseCount(QualityType.Ordinary));
        Assert.Equal(5, PvpRelicStock.BaseCount(QualityType.Rare));
        Assert.Equal(4, PvpRelicStock.BaseCount(QualityType.Epic));
        Assert.Equal(3, PvpRelicStock.BaseCount(QualityType.Legend));

        var tables = PvpTestTables.WithRelics(
            new[] { relic, rare, consumable },
            new[] { new RelicEntryConfig { Id = 4242, Type = MechanismType.HeroHpMax, Value = new[] { 1f } } });
        var stock = PvpRelicStock.Create(tables);
        Assert.Equal(6, stock.Available(7));
        Assert.Equal(5, stock.Available(8));
        Assert.Equal(int.MaxValue, stock.Available(9));
        for (var i = 0; i < 6; i++)
        {
            Assert.True(stock.TryReserve(7));
        }

        Assert.False(stock.TryReserve(7));
        stock.Return(7, 3);
        Assert.Equal(3, stock.Available(7));
        stock.Return(7, 100);
        Assert.Equal(6, stock.Available(7));

        var match = Open(tables, shopPool: new[] { 7 }, hp: 5000);
        match.Fighters[2].ShopPoolIds = Array.Empty<int>();
        match.Fighters[3].ShopPoolIds = Array.Empty<int>();
        EnterShopPhase(match);

        var offered = 0;
        for (var i = 0; i < match.Fighters.Count; i++)
        {
            var offers = match.Fighters[i].ShopOfferIds;
            offered += offers.Count;
            Assert.All(offers, id => Assert.Equal(7, id));
            if (i >= 2)
            {
                Assert.Empty(offers);
            }
        }

        Assert.Equal(PvpRelicStock.OrdinaryStock, offered);

        var buyer = match.Fighters[0];
        var gold = buyer.Gold;
        match.Act(buyer.UserId, "buy", 7, Array.Empty<int>());
        match.Act(buyer.UserId, "buy", 7, Array.Empty<int>());
        match.Act(buyer.UserId, "buy", 7, Array.Empty<int>());
        Assert.Equal(3, buyer.OwnedRelicIds.Count);
        Assert.All(buyer.OwnedRelicPower, p => Assert.Equal(1, p));
        Assert.Equal(gold - 30, buyer.Gold);

        var locked = match.Fighters[1];
        var lockedOffers = locked.ShopOfferIds.Count;
        match.Act(locked.UserId, "refresh", 0, Array.Empty<int>());
        Assert.True(locked.ShopOfferIds.Count <= lockedOffers);

        match.Act(buyer.UserId, "sell", 7, Array.Empty<int>());
        Assert.Equal(2, buyer.OwnedRelicIds.Count);
        Assert.Equal(gold - 30 + 4, buyer.Gold);
        match.Act(locked.UserId, "refresh", 0, Array.Empty<int>());
        Assert.Contains(7, locked.ShopOfferIds);
    }

    [Fact]
    public void RelicSlotsStartAtFourAndUnlockWithGold()
    {
        var relicA = new RelicConfig { Id = 7, Name = "甲", Type = QualityType.Ordinary, Price = 10, SellingPrice = 4, RefreshProbability = 1f, MechanismId = Array.Empty<int>() };
        var relicB = new RelicConfig { Id = 8, Name = "乙", Type = QualityType.Ordinary, Price = 10, SellingPrice = 4, RefreshProbability = 1f, MechanismId = Array.Empty<int>() };
        var relicC = new RelicConfig { Id = 9, Name = "丙", Type = QualityType.Ordinary, Price = 10, SellingPrice = 4, RefreshProbability = 1f, MechanismId = Array.Empty<int>() };
        var relicD = new RelicConfig { Id = 10, Name = "丁", Type = QualityType.Ordinary, Price = 10, SellingPrice = 4, RefreshProbability = 1f, MechanismId = Array.Empty<int>() };
        var tables = PvpTestTables.WithRelics(new[] { relicA, relicB, relicC, relicD });
        var match = Open(tables, shopPool: new[] { 7, 8, 9, 10 }, hp: 5000);
        var fighter = match.Fighters[0];
        Assert.Equal(PvpRelicBag.FreeSlots, fighter.RelicSlots);
        fighter.OwnedRelicIds.Add(7);
        fighter.OwnedRelicIds.Add(8);
        fighter.OwnedRelicIds.Add(9);
        fighter.OwnedRelicIds.Add(10);
        fighter.OwnedRelicPower.Add(1);
        fighter.OwnedRelicPower.Add(1);
        fighter.OwnedRelicPower.Add(1);
        fighter.OwnedRelicPower.Add(1);
        EnterShopPhase(match);
        Assert.NotEmpty(fighter.ShopOfferIds);
        var onSale = fighter.ShopOfferIds[0];
        Assert.Throws<InvalidOperationException>(() => match.Act(fighter.UserId, "buy", onSale, Array.Empty<int>()));

        var gold = fighter.Gold;
        var cost = PvpRelicBag.NextSlotCost(fighter.RelicSlots);
        Assert.Equal(40, cost);
        match.Act(fighter.UserId, "unlock_slot", 0, Array.Empty<int>());
        Assert.Equal(5, fighter.RelicSlots);
        Assert.Equal(gold - cost, fighter.Gold);
        Assert.Equal(5, match.ViewFor(fighter.UserId).Shop!.RelicSlots);
        match.Act(fighter.UserId, "buy", onSale, Array.Empty<int>());
        Assert.Equal(5, fighter.OwnedRelicIds.Count);
    }

    [Fact]
    public void BuySkillChargesGoldAndTakesEffectAtNextRoundStart()
    {
        var tables = PvpTestTables.Classic();
        var match = Open(tables, hp: 5000);
        var fighter = match.Fighters[0];
        EnterShopPhase(match);

        var gold = fighter.Gold;
        var leftBefore = fighter.RubLeft;
        var price = PvpSkillShop.Price(fighter, PvpSkillIds.Rub);
        Assert.Equal(PvpSkillShop.RubBaseGold, price);

        match.Act(fighter.UserId, "buy_skill", PvpSkillIds.Rub, Array.Empty<int>());
        Assert.Equal(gold - price, fighter.Gold);
        // 商店里不动手上剩余，只记下一轮待生效次数。
        Assert.Equal(leftBefore, fighter.RubLeft);
        Assert.Equal(1, fighter.PendingRub);

        var shop = match.ViewFor(fighter.UserId).Shop!;
        var rub = shop.Skills.Single(s => s.Id == PvpSkillIds.Rub);
        // 展示分子含 pending：0/1 买一次 → 1/1；分母始终是基数。
        Assert.Equal(leftBefore + 1, rub.Left);
        Assert.Equal(PvpSkillShop.Base(tables, fighter, PvpSkillIds.Rub), rub.Max);
        // 第二口涨价。
        Assert.Equal(price + PvpSkillShop.StepGold, rub.Price);

        // 金币不足 / 技能 Id 非法都由服务端拒绝。
        fighter.Gold = 0;
        Assert.Throws<InvalidOperationException>(
            () => match.Act(fighter.UserId, "buy_skill", PvpSkillIds.Peek, Array.Empty<int>()));
        Assert.Throws<InvalidOperationException>(
            () => match.Act(fighter.UserId, "buy_skill", 99, Array.Empty<int>()));

        // 下一轮只把购买并入剩余，不回满。
        var expectedRub = leftBefore + 1;
        foreach (var f in match.Fighters)
        {
            if (!f.ShopDone)
            {
                match.Act(f.UserId, "shop_done", 0, Array.Empty<int>());
            }
        }

        Assert.Equal(2, match.Round);
        Assert.Equal(expectedRub, fighter.RubLeft);
        Assert.Equal(0, fighter.PendingRub);
        // 已购计数保留，后续商店继续按递增价卖。
        Assert.Equal(1, fighter.BoughtRub);
    }

    private static void EnterShopPhase(PvpMatch match)
    {
        // 机器人桌开局可能已自动结算；全员亮完后 phase 会离开 fight，勿再 Showdown。
        foreach (var fighter in match.Fighters)
        {
            if (match.Phase != PvpMatch.PhaseFight)
            {
                break;
            }

            match.Showdown(fighter.UserId);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (match.Phase == PvpMatch.PhaseSettle)
        {
            Assert.True(match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1));
        }

        Assert.Equal(PvpMatch.PhaseShop, match.Phase);
    }

    [Fact]
    public void BotShopPickHighestAffordablePrefersPrice()
    {
        var cheap = BotShopRelic(101, 10);
        var pricey = BotShopRelic(102, 50);
        var mid = BotShopRelic(103, 30);
        var tables = PvpTestTables.WithRelics(new[] { cheap, pricey, mid });
        var fighter = new PvpFighter
        {
            Gold = 40,
            RelicSlots = 4,
            Combat = new SeatSetup { HeroId = 1 }
        };
        fighter.ShopOfferIds.Add(101);
        fighter.ShopOfferIds.Add(102);
        fighter.ShopOfferIds.Add(103);

        // 40 金买不起 50，应选 30。
        Assert.Equal(103, PvpBotShop.PickHighestAffordable(fighter, tables));
        fighter.Gold = 50;
        Assert.Equal(102, PvpBotShop.PickHighestAffordable(fighter, tables));
        fighter.OwnedRelicIds.Add(1);
        fighter.OwnedRelicIds.Add(2);
        fighter.OwnedRelicIds.Add(3);
        fighter.OwnedRelicIds.Add(4);
        Assert.Null(PvpBotShop.PickHighestAffordable(fighter, tables));
        Assert.True(PvpBotShop.CanUnlockSlot(fighter, tables));
        fighter.Gold = 10;
        Assert.False(PvpBotShop.CanUnlockSlot(fighter, tables));
    }

    [Fact]
    public void BotAutoShopBuysHighestThenUnlocksWhenFullAndKeepsBuying()
    {
        var cheap = BotShopRelic(201, 10);
        var pricey = BotShopRelic(202, 50);
        var tables = PvpTestTables.WithRelics(new[] { cheap, pricey });
        var bot = Bot("机");
        var match = Open(
            tables,
            new[] { Pub("你"), bot, Bot("乙"), Bot("丙") },
            shopPool: new[] { 201, 202 },
            hp: 5000);
        var botFighter = match.Fighters.First(f => f.NickName == "机");

        // 槽已满进店：先开槽，再买货架上最贵的。
        botFighter.OwnedRelicIds.AddRange(new[] { 201, 201, 201, 201 });
        botFighter.OwnedRelicPower.AddRange(new[] { 1, 1, 1, 1 });
        botFighter.Gold = 200;

        EnterShopPhase(match);

        Assert.True(botFighter.ShopDone);
        Assert.True(botFighter.RelicSlots > PvpRelicBag.FreeSlots);
        Assert.True(botFighter.OwnedRelicIds.Count > 4);
        Assert.Contains(202, botFighter.OwnedRelicIds);
        Assert.True(botFighter.OwnedRelicIds.Count(id => id == 202) >= 1);
    }

    [Fact]
    public void BotAutoShopMarksDoneAndHumansStillShop()
    {
        var match = Open(
            PvpTestTables.Classic(),
            new[] { Pub("你"), Bot("甲"), Bot("乙"), Bot("丙") },
            shopPool: new[] { 1, 2 });
        EnterShopPhase(match);

        Assert.All(match.Fighters.Where(f => f.IsBot), f => Assert.True(f.ShopDone));
        Assert.False(match.Fighters[0].ShopDone);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);
        Assert.True(match.Fighters.Where(f => f.IsBot).Sum(f => f.OwnedRelicIds.Count) > 0);
    }

    private static RelicConfig BotShopRelic(int id, int price)
        => new RelicConfig
        {
            Id = id,
            Name = "bot" + id,
            Type = QualityType.Ordinary,
            UnlockConditionId = 0,
            Price = price,
            SellingPrice = 4,
            RefreshProbability = 1f,
            MechanismId = Array.Empty<int>()
        };

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
        var match = Open(PvpTestTables.OnePvpRound(), hp: 1);
        Assert.False(match.TryMarkRewardsGranted());
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var n = 0; n < 8 && match.Phase != PvpMatch.PhaseFinished; n++)
        {
            if (match.Phase == PvpMatch.PhaseFight)
            {
                foreach (var fighter in match.Fighters)
                {
                    if (fighter.Alive)
                    {
                        match.Showdown(fighter.UserId);
                    }
                }
            }

            match.ApplyTimeouts(now + 600_000);
        }

        Assert.Equal(PvpMatch.PhaseFinished, match.Phase);
        Assert.True(match.FinishedUtcMs > 0);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "match_finished");

        var rewards = new[] { 200, 120, 60, 30 };
        var scores = new[] { 50, 25, 0, -40 };
        foreach (var fighter in match.Fighters)
        {
            Assert.InRange(fighter.Rank, 1, 4);
            Assert.Equal(rewards[fighter.Rank - 1], fighter.RewardGold);
            Assert.Equal(scores[fighter.Rank - 1], fighter.RewardScore);
            Assert.Equal(fighter.RewardGold, match.ViewFor(fighter.UserId).Players[fighter.SeatIndex].RewardGold);
            Assert.Equal(fighter.RewardScore, match.ViewFor(fighter.UserId).Players[fighter.SeatIndex].RewardScore);
        }

        Assert.True(match.TryMarkRewardsGranted());
        Assert.False(match.TryMarkRewardsGranted());
    }

    [Fact]
    public void ByeAfterDeathFightsMirrorBotWithRelicsNotMonster()
    {
        var match = OpenClassic();
        var dead = match.Fighters[0];
        match.Act(dead.UserId, "debug_grant", 1, Array.Empty<int>());
        Assert.True(match.Abandon(dead.UserId));
        Assert.Contains(1, dead.DeathRelicIds);
        Assert.NotNull(dead.DeathMirror);
        Assert.Contains(1, dead.DeathMirror.RelicIds);

        foreach (var fighter in match.Fighters)
        {
            if (fighter.Alive)
            {
                match.Showdown(fighter.UserId);
            }
        }

        AdvanceThrough(match);

        Assert.Equal(2, match.Round);
        Assert.Equal(PvpFightKind.Pvp, match.FightKind);
        Assert.DoesNotContain(match.Duels, d => d.VsMonster);
        var mirror = Assert.Single(match.Duels, d => d.VsMirror);
        Assert.Equal(dead.UserId, mirror.RightUserId);
        Assert.False(mirror.Involves(dead.UserId));
        var view = match.ViewFor(mirror.LeftUserId);
        Assert.Equal("pvp", view.FightKind);
        Assert.Contains(view.Players, p => p.UserId == dead.UserId && p.RelicIds.Contains(1));
        var foe = view.Duel.Seats[1];
        Assert.Equal(dead.UserId, foe.UserId);
        Assert.True(foe.MaxHp > 0);
        Assert.True(foe.Hp > 0);
    }

    [Fact]
    public void AbandonEliminatesAndRanksLastWithoutBlockingOthers()
    {
        var match = OpenClassic();
        var a = match.Fighters[0];
        match.DrainEvents();

        Assert.True(match.Abandon(a.UserId));
        Assert.False(a.Alive);
        Assert.Equal(0, a.Hp);
        Assert.True(a.Disconnected);
        Assert.Equal(4, a.Rank);
        Assert.Equal(3, match.Fighters.Count(f => f.Alive));

        // 放弃者座位当轮自动锁定（野怪轮对面已锁，直接结算），后续不再被等待。
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        Assert.True(duel.Resolved);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);

        var events = match.DrainEvents();
        Assert.Contains(events, e => e.Kind == "player_offline" && e.UserId == a.UserId);
        Assert.Contains(events, e => e.Kind == "player_eliminated" && e.UserId == a.UserId && e.Value == 4);

        // 已淘汰/不存在的玩家再放弃：返回 false 无副作用。
        Assert.False(match.Abandon(a.UserId));
        Assert.False(match.Abandon(Guid.NewGuid().ToString("N")));
        Assert.Empty(match.DrainEvents());
    }

    [Fact]
    public void AbandonInShopMarksShopDoneAndCanAdvance()
    {
        var match = Open(PvpTestTables.Classic(), shopPool: new[] { 1, 2 });
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);

        for (var i = 1; i < 4; i++)
        {
            match.Act(match.Fighters[i].UserId, "shop_done", 0, Array.Empty<int>());
        }

        var a = match.Fighters[0];
        Assert.True(match.Abandon(a.UserId));
        Assert.True(a.ShopDone);
        Assert.False(a.Alive);
        // 其余玩家已 shop_done，放弃者补齐后直接进入下一轮。
        Assert.Equal(2, match.Round);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
    }

    [Fact]
    public void AbandonDownToLastSurvivorFinishesMatch()
    {
        var match = Open(PvpTestTables.OnePvpRound(), new[] { Pub("甲"), Pub("乙") });
        var a = match.Fighters[0];
        var b = match.Fighters[1];
        var hpB = b.Hp;

        Assert.True(match.Abandon(a.UserId));
        Assert.Equal(2, a.Rank);
        // 逃跑当桌直接判负，对手不受伤；只剩一人后进 settle。
        Assert.Equal(hpB, b.Hp);
        Assert.Equal(PvpMatch.PhaseSettle, match.Phase);
        Assert.True(match.Duels.All(d => d.Resolved));

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1));
        Assert.Equal(PvpMatch.PhaseFinished, match.Phase);
        Assert.Equal(1, b.Rank);
        Assert.True(b.RewardGold > 0);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "match_finished");

        // 已结束再放弃：无副作用。
        Assert.False(match.Abandon(b.UserId));
    }

    [Fact]
    public void AbandonForfeitsPvpDuelWithoutDamagingOpponent()
    {
        var match = Open(PvpTestTables.Classic(), hp: 500);
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        AdvanceThrough(match);
        Assert.Equal(2, match.Round);
        Assert.Equal(PvpFightKind.Pvp, match.FightKind);

        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var foeId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var foe = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, foeId));
        var foeHp = foe.Hp;

        Assert.True(match.Abandon(a.UserId));
        Assert.False(a.Alive);
        Assert.Equal(4, a.Rank);
        Assert.True(duel.Resolved);
        Assert.Equal(foeHp, foe.Hp);
        Assert.True(foe.Alive);
        Assert.Contains(match.DrainEvents(), e => e.Kind == "duel_resolved" && e.Value == 0);
    }

    [Fact]
    public void ApplyPvpHpBoostScalesHpByGameConstPercent()
    {
        var tables = PvpTestTables.Classic();
        tables.GameConst.PvpHeroHpBoostPercent = 1300;
        var seat = CombatBonuses.BuildSeat(0, "u", "n", 1, Array.Empty<CombatTalentCount>(), tables);
        seat.Hp = 100;
        seat.MaxHp = 100;
        var attack = seat.Attack;

        CombatBonuses.ApplyPvpHpBoost(seat, tables);

        // 提高 1300%：100 × (1 + 1300%) = 1400，攻击力不变。
        Assert.Equal(1400, seat.Hp);
        Assert.Equal(1400, seat.MaxHp);
        Assert.Equal(attack, seat.Attack);
    }

    [Fact]
    public void ApplyPvpHpBoostZeroOrNegativePercentKeepsHp()
    {
        var tables = PvpTestTables.Classic();
        var seat = CombatBonuses.BuildSeat(0, "u", "n", 1, Array.Empty<CombatTalentCount>(), tables);
        seat.Hp = 100;
        seat.MaxHp = 100;

        CombatBonuses.ApplyPvpHpBoost(seat, tables);

        Assert.Equal(100, seat.Hp);
        Assert.Equal(100, seat.MaxHp);
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
    public void MonsterSeatHpIsReducedWhenPlayerWins()
    {
        var sawWin = false;
        foreach (var seed in new[] { 42, 7, 1001, 2024, 9 })
        {
            var match = OpenClassic(seed);
            foreach (var fighter in match.Fighters)
            {
                match.Showdown(fighter.UserId);
            }

            foreach (var duel in match.Duels)
            {
                Assert.True(duel.VsMonster);
                Assert.True(duel.HpApplied);
                var snap = duel.Engine.Snapshot;
                if (snap.Winners == null || snap.Winners.Count != 1 || snap.Winners[0] != 0)
                {
                    continue;
                }

                sawWin = true;
                var maxHp = duel.Engine.MutableSeat(1).MaxHp;
                var hp = duel.Engine.MutableSeat(1).Hp;
                Assert.True(maxHp > 0);
                Assert.Equal(Math.Max(0, maxHp - duel.AppliedDamage), hp);
                var view = match.ViewFor(duel.LeftUserId);
                var foe = view.Duel!.Seats.First(s => s.SeatId == 1);
                Assert.Equal(hp, foe.Hp);
            }
        }

        Assert.True(sawWin);
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
    /// <summary>第 1 轮是野怪。打完进商店，再进入下一轮玩家对战。hpOnPvpRound 在商店里改血，下一轮按这个血量开打。</summary>
    internal static void AdvanceToPvpFight(PvpMatch match, int hpOnPvpRound = 0)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 600_000;
        for (var i = 0; i < 8 && match.Phase != PvpMatch.PhaseFinished; i++)
        {
            if (match.Phase == PvpMatch.PhaseFight && match.FightKind == PvpFightKind.Pvp)
            {
                return;
            }

            if (match.Phase == PvpMatch.PhaseShop && hpOnPvpRound > 0)
            {
                foreach (var fighter in match.Fighters)
                {
                    if (fighter.Alive)
                    {
                        fighter.Hp = hpOnPvpRound;
                        fighter.MaxHp = hpOnPvpRound;
                    }
                }
            }

            if (match.Phase == PvpMatch.PhaseFight)
            {
                foreach (var fighter in match.Fighters)
                {
                    if (fighter.Alive)
                    {
                        match.Showdown(fighter.UserId);
                    }
                }
            }

            match.ApplyTimeouts(now);
        }

        Assert.Equal(PvpFightKind.Pvp, match.FightKind);
        Assert.Equal(PvpMatch.PhaseFight, match.Phase);
    }

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

    /// <summary>带治疗/血量/消耗词条的表：绷带113、急救粉119、小精灵210、月光酒229、凤凰羽毛425、
    /// PVE 专属消耗 999（UseDamageFixed，验证不上架/拒用）。</summary>
    public static GameTables WithHealRelics()
    {
        var fallback = GameTables.Fallback();
        var relics = fallback.Relics.Concat(new[]
        {
            new RelicConfig { Id = 113, Name = "绷带", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9001 } },
            new RelicConfig { Id = 119, Name = "急救粉", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9002 }, UseType = 1 },
            new RelicConfig { Id = 210, Name = "小精灵", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9003, 9004 } },
            new RelicConfig { Id = 220, Name = "激励徽章", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9009 } },
            new RelicConfig { Id = 222, Name = "永恒之心", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9008 } },
            new RelicConfig { Id = 229, Name = "月光酒", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9005 } },
            new RelicConfig { Id = 425, Name = "凤凰羽毛", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9006 }, UseType = 1 },
            new RelicConfig { Id = 999, Name = "PVE专属", UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = new[] { 9007 }, UseType = 1 }
        }).ToArray();
        var relicEntries = new[]
        {
            new RelicEntryConfig { Id = 9001, Type = MechanismType.HeroHpReplyEveryRoundEnding, Value = new[] { 2f } },
            new RelicEntryConfig { Id = 9002, Type = MechanismType.HealHpPercent, Value = new[] { 0.2f } },
            new RelicEntryConfig { Id = 9003, Type = MechanismType.HeroHpMax, Value = new[] { 30f } },
            new RelicEntryConfig { Id = 9004, Type = MechanismType.HeroHpReplyEveryRoundEnding, Value = new[] { 2f } },
            new RelicEntryConfig { Id = 9005, Type = MechanismType.WinHeal, Value = new[] { 3f } },
            new RelicEntryConfig { Id = 9006, Type = MechanismType.UseRoundNullify, Value = new[] { 1f, 0.3f } },
            new RelicEntryConfig { Id = 9007, Type = MechanismType.UseDamageFixed, Value = new[] { 50f } },
            new RelicEntryConfig { Id = 9008, Type = MechanismType.EveryRoundGetHpMax, Value = new[] { 1f } },
            new RelicEntryConfig { Id = 9009, Type = MechanismType.DefeatGetHpMax, Value = new[] { 3f } }
        };
        return Build(ClassicRounds(), relics: relics, relicEntries: relicEntries);
    }

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

    /// <summary>圣骑士（Id=8）：承伤-25%、圣光燃烧、无法闪避。与正式表口径一致。</summary>
    public static GameTables WithPaladin(bool pvpOnly = true)
    {
        var heroes = new[]
        {
            new HeroConfig { Id = 1, HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = Array.Empty<int>() },
            new HeroConfig
            {
                Id = 8,
                Name = "圣骑士",
                HeroDamage = 30,
                Hp = 260,
                Critical = 0f,
                CriticalDamage = 2f,
                HeroEntryId = new[] { 801, 802, 803 }
            }
        };
        var heroEntries = new[]
        {
            new HeroEntryConfig { Id = 801, Type = MechanismType.HeroTakeDamagePer, Value = new[] { -0.25f } },
            new HeroEntryConfig { Id = 802, Type = MechanismType.HolyLightBurning, Value = new[] { 1f, 0.5f, 0.1f } },
            new HeroEntryConfig { Id = 803, Type = MechanismType.UnableMissing, Value = Array.Empty<float>() }
        };
        var rounds = pvpOnly
            ? new[] { Round(1, 1, PvpFightKind.Pvp, 0, 15) }
            : ClassicRounds();
        return Build(rounds, heroes: heroes, heroEntries: heroEntries);
    }

    /// <summary>带英雄词条的表：2 吸血鬼（承伤-25%）、3 商人（初始金 80 / 买价-20% / 过关金+20%）、
    /// 4 武僧（100% 闪避 + 反击 攻击×200%）、5 狂战士（吸血 50%）、6 射手（追击 35%）；天赋 500 追击 25%。
    /// pvpOnly 时只配一个 PVP 轮，方便直接构造玩家对玩家结算。</summary>
    public static GameTables WithHeroEntries(bool pvpOnly = false)
    {
        var heroes = new[]
        {
            new HeroConfig { Id = 1, HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = Array.Empty<int>() },
            new HeroConfig { Id = 2, Name = "吸血鬼", HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = new[] { 2101 } },
            new HeroConfig { Id = 3, Name = "商人", HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = new[] { 2102, 2103, 2104 } },
            new HeroConfig { Id = 4, Name = "武僧", HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = new[] { 2105, 2106 } },
            new HeroConfig { Id = 5, Name = "狂战士", HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = new[] { 2107 } },
            new HeroConfig { Id = 6, Name = "射手", HeroDamage = 10, Hp = 100, Critical = 0f, CriticalDamage = 2f, HeroEntryId = new[] { 2108 } }
        };
        var heroEntries = new[]
        {
            new HeroEntryConfig { Id = 2101, Type = MechanismType.HeroTakeDamagePer, Value = new[] { -0.25f } },
            new HeroEntryConfig { Id = 2102, Type = MechanismType.InitialFunds, Value = new[] { 80f } },
            new HeroEntryConfig { Id = 2103, Type = MechanismType.RelicPricePer, Value = new[] { -0.2f } },
            new HeroEntryConfig { Id = 2104, Type = MechanismType.GetGoldAfterLevel, Value = new[] { 0.2f } },
            new HeroEntryConfig { Id = 2105, Type = MechanismType.MissDamagePer, Value = new[] { 1f } },
            new HeroEntryConfig { Id = 2106, Type = MechanismType.MissGetDamage, Value = new[] { 1f, 2f } },
            new HeroEntryConfig { Id = 2107, Type = MechanismType.BloodSucking, Value = new[] { 0.5f } },
            new HeroEntryConfig { Id = 2108, Type = MechanismType.ExtraAttackOneTime, Value = new[] { 0.35f } }
        };
        var fallback = GameTables.Fallback();
        var talentRows = fallback.TalentRows.Concat(new[]
        {
            new TalentConfig { Id = 10, TalentId = 500, TalentLevel = 1, TalentEntry = 3001 }
        }).ToArray();
        var talentEntries = new[]
        {
            new TalentEntryConfig { Id = 3001, Type = MechanismType.ProOfExtraAttack, Value = 0.25f }
        };
        var rounds = pvpOnly
            ? new[] { Round(1, 1, PvpFightKind.Pvp, 0, 15) }
            : ClassicRounds();
        return Build(
            rounds,
            heroes: heroes,
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

    public static GameTables WithRelics(
        RelicConfig[] relics,
        RelicEntryConfig[]? relicEntries = null,
        BindingConfig[]? bindings = null)
        => Build(ClassicRounds(), relics: relics, relicEntries: relicEntries, bindings: bindings);

    public static GameTables WithPvpRelics(
        RelicConfig[] relics,
        PvpRelicConfig[] pvpRelics,
        RelicEntryConfig[]? relicEntries = null,
        PvpRelicEntryConfig[]? pvpRelicEntries = null)
        => Build(
            ClassicRounds(),
            relics: relics,
            relicEntries: relicEntries,
            pvpRelics: pvpRelics,
            pvpRelicEntries: pvpRelicEntries);

    internal static RelicConfig BondRelic(int id)
        => new RelicConfig
        {
            Id = id,
            Name = "羁绊" + id,
            Type = QualityType.Ordinary,
            Price = 10,
            SellingPrice = 4,
            RefreshProbability = 1f,
            MechanismId = Array.Empty<int>(),
            BindingID = new[] { 101 }
        };

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
        IReadOnlyList<TalentEntryConfig>? talentEntries = null,
        IReadOnlyList<BindingConfig>? bindings = null,
        IReadOnlyList<PvpRelicConfig>? pvpRelics = null,
        IReadOnlyList<PvpRelicEntryConfig>? pvpRelicEntries = null)
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
                    RankScore = new[] { 50, 25, 0, -40 },
                    DamageRoundScale = 0.1f,
                    OpenPhaseSeconds = 20
                }
            },
            rounds,
            bindings: bindings,
            // 测试夹具：未显式给 PvpRelic 时，从本用例的 Relic 行合成进 PVP 通道（生产路径不回落 RelicConfig）。
            pvpRelics: pvpRelics ?? PvpTableHarness.Relics(relics ?? fallback.Relics),
            pvpRelicEntries: pvpRelicEntries ?? PvpTableHarness.Entries(relicEntries ?? fallback.RelicEntries));
    }
}
