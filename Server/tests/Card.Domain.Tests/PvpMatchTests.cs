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
        Assert.Equal(new[] { 0, 1, 2 }, view.Duel.Seats[0].Selected);
        Assert.Null(view.Duel.Seats[1].Cards);
        Assert.Equal(a, view.Duel.Seats[0].UserId);
        Assert.Equal(3, view.Players[0].RubLeft);
        Assert.Equal(1, view.Players[0].ReplaceLeft);
        Assert.Equal(1, view.Players[0].PeekLeft);
    }

    [Fact]
    public void PeekRevealsOpponentHoleOnlyToViewer()
    {
        var match = OpenClassic();
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

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
        Assert.Equal(2, match.Round);
        Assert.Equal(PvpFightKind.Pvp, match.FightKind);
        Assert.Equal(1, match.Duels.Count(d => !d.Resolved));
        Assert.True(match.Duels.First(d => !d.Resolved).Involves(you.UserId));
    }

    [Fact]
    public void BothHumansMustLockBeforeCompareThenLoserTakesScaledDamage()
    {
        var match = OpenClassic();
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

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

    private static PvpMatch OpenClassic() => Open(PvpTestTables.Classic());

    private static PvpMatch Open(GameTables tables)
        => Open(tables, new[] { Pub("甲"), Pub("乙"), Pub("丙"), Pub("丁") });

    private static PvpMatch Open(GameTables tables, PlayerPublic[] players, int hp = 0)
    {
        var seats = new SeatSetup[players.Length];
        for (var i = 0; i < players.Length; i++)
        {
            seats[i] = CombatBonuses.BuildSeat(i, players[i].UserId, players[i].NickName, 1, Array.Empty<CombatTalentCount>(), tables);
            seats[i].IsHuman = !players[i].IsBot;
            if (hp > 0)
            {
                seats[i].Hp = hp;
                seats[i].MaxHp = hp;
            }
        }

        return PvpMatch.Open(Guid.NewGuid(), 42, players, tables, seats, 1);
    }

    private static PlayerPublic Pub(string nick)
        => new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = nick };

    private static PlayerPublic Bot(string nick)
        => new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = nick, IsBot = true };
}

internal static class PvpTestTables
{
    public static GameTables Classic()
    {
        return Build(
            new PvpRoundConfig[]
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
            });
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

    private static GameTables Build(IReadOnlyList<PvpRoundConfig> rounds)
    {
        var fallback = GameTables.Fallback();
        return new GameTables(
            fallback.GameConst,
            fallback.HandScores,
            fallback.Levels,
            new[]
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
            fallback.Relics,
            fallback.UnlockConditions,
            fallback.TalentRows,
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
            fallback.HeroEntries,
            fallback.TalentEntries,
            fallback.RelicEntries,
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
                    DamageRoundScale = 0.1f
                }
            },
            rounds);
    }
}
