using CardShare.Battle;
using CardShare.Contracts;
using PlayingCard = CardShare.Battle.Card;
using Xunit;

namespace CardShare.Domain.Tests;

public class BattleEngineTests
{
    [Fact]
    public void SameSeedDealIsDeterministicAndFourSeatsGetThreeCards()
    {
        var tables = GameTables.Fallback();
        var seats = new[]
        {
            new SeatSetup { SeatId = 0, IsHuman = true },
            new SeatSetup { SeatId = 1, IsHuman = true },
            new SeatSetup { SeatId = 2, IsHuman = true },
            new SeatSetup { SeatId = 3, IsHuman = true }
        };

        var a = new BattleEngine(new PvpMode(), 42, seats, tables);
        var b = new BattleEngine(new PveMode(), 42, seats, tables);
        a.Apply(new BattleCommand { Type = BattleCommandType.Deal });
        b.Apply(new BattleCommand { Type = BattleCommandType.Deal });

        Assert.Equal(4, a.Snapshot.Hands.Length);
        Assert.Equal(3, a.Snapshot.Hands[0].Count);
        Assert.Equal(a.Snapshot.Hands[0][0], b.Snapshot.Hands[0][0]);
        Assert.Equal(a.Snapshot.Hands[3][2], b.Snapshot.Hands[3][2]);
    }

    [Fact]
    public void ShowdownScoresHandsFromSharedTables()
    {
        var tables = GameTables.Fallback();
        HandEvaluator.Tables = tables;
        var engine = new BattleEngine(new PvpMode(), 7, Array.Empty<SeatSetup>(), tables);
        engine.Apply(new BattleCommand { Type = BattleCommandType.Deal });
        var snapshot = engine.Apply(new BattleCommand { Type = BattleCommandType.Showdown });
        Assert.Equal(4, snapshot.Scores.Length);
        Assert.True(snapshot.Scores[0].Level >= 1);
        Assert.Equal(snapshot.Scores[0].Multiplier, HandEvaluator.TypeMultiplier(snapshot.Scores[0].Type));
    }

    [Fact]
    public void PairBeatsHighCardWithFallbackAndTables()
    {
        HandEvaluator.Tables = GameTables.Fallback();
        var pair = HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Heart, Rank.Ace),
            new PlayingCard(Suit.Spade, Rank.Ace),
            new PlayingCard(Suit.Club, Rank.Three)
        });
        var high = HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Heart, Rank.King),
            new PlayingCard(Suit.Spade, Rank.Nine),
            new PlayingCard(Suit.Club, Rank.Two)
        });
        Assert.Equal(HandType.Pair, pair.Type);
        Assert.True(pair.CompareTo(high) > 0);
    }

    [Fact]
    public void PveLocalSessionDealAndShowdownAssignsWinners()
    {
        var session = PveLocalSession.Create(GameTables.Fallback(), 11);
        session.Deal();
        Assert.Equal(BattlePhase.Dealt, session.Snapshot.Phase);
        Assert.Equal(BattleModeKind.Pve, session.Snapshot.Mode);
        var snap = session.Showdown();
        Assert.Equal(BattlePhase.Showdown, snap.Phase);
        Assert.NotEmpty(snap.Winners);
        Assert.InRange(snap.Winners[0], 0, 3);
    }

    [Fact]
    public void InactiveSeatDoesNotReceiveCardsAndDrawExtraDoesNotDuplicate()
    {
        var tables = GameTables.Fallback();
        var seats = PveLocalSession.DefaultSeats().ToArray();
        seats[2].Alive = false;
        var session = PveLocalSession.Create(tables, 21, seats);
        var snap = session.Deal();
        Assert.True(snap.Hands[0][0].IsValid);
        Assert.False(snap.Hands[2][0].IsValid);

        var seen = new HashSet<PlayingCard>();
        for (var i = 0; i < snap.Hands.Length; i++)
        {
            for (var c = 0; c < snap.Hands[i].Count; c++)
            {
                if (snap.Hands[i][c].IsValid)
                {
                    Assert.True(seen.Add(snap.Hands[i][c]));
                }
            }
        }

        for (var i = 0; i < 8; i++)
        {
            var extra = session.DrawExtra();
            Assert.True(extra.IsValid);
            Assert.True(seen.Add(extra));
        }
    }

    [Fact]
    public void PvpTableHidesOpponentCardsUntilShowdown()
    {
        var players = new[]
        {
            Public("甲"),
            Public("乙"),
            Public("丙"),
            Public("丁")
        };
        var table = PvpBattleTable.Open(Guid.NewGuid(), 8, players, GameTables.Fallback());
        var view = table.ViewFor(players[0].UserId);
        Assert.Equal("dealt", view.Phase);
        Assert.Equal("pvp", view.Mode);
        Assert.Equal(0, view.ViewerSeat);
        Assert.Equal(3, view.Seats[0].Cards!.Count);
        Assert.Null(view.Seats[1].Cards);
        Assert.Null(view.Seats[1].HandType);

        table.Showdown();
        var shown = table.ViewFor(players[1].UserId);
        Assert.Equal("showdown", shown.Phase);
        Assert.Equal(1, shown.ViewerSeat);
        Assert.Equal(3, shown.Seats[0].Cards!.Count);
        Assert.Equal(3, shown.Seats[3].Cards!.Count);
        Assert.False(string.IsNullOrEmpty(shown.Seats[0].HandType));
        Assert.NotEmpty(shown.Winners);
    }

    private static PlayerPublic Public(string nick)
    {
        return new PlayerPublic
        {
            UserId = Guid.NewGuid().ToString("N"),
            NickName = nick
        };
    }
}
