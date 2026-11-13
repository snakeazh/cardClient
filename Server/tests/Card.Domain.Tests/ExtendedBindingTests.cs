using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using Xunit;
using HandType = CardShare.Battle.HandType;

namespace CardShare.Domain.Tests;

public class ExtendedBindingTests
{
    private static Card C(Suit suit, Rank rank) => new Card(suit, rank);

    [Fact]
    public void ExtendedHands_OrderAndWheel()
    {
        var flush = HandEvaluator.EvaluateEcho(new[] { C(Suit.Heart, Rank.Two), C(Suit.Heart, Rank.Four), C(Suit.Heart, Rank.Six), C(Suit.Heart, Rank.Eight), C(Suit.Heart, Rank.Ten) });
        var straight = HandEvaluator.EvaluateEcho(new[] { C(Suit.Spade, Rank.Ace), C(Suit.Heart, Rank.Two), C(Suit.Spade, Rank.Three), C(Suit.Heart, Rank.Four), C(Suit.Spade, Rank.Five) });
        var bomb = HandEvaluator.EvaluateEcho(new[] { C(Suit.Spade, Rank.Two), C(Suit.Heart, Rank.Two), C(Suit.Club, Rank.Two), C(Suit.Diamond, Rank.Two), C(Suit.Spade, Rank.King) });
        Assert.Equal(HandType.FiveFlush, flush.Type);
        Assert.Equal(12, flush.Multiplier);
        Assert.Equal(HandType.FiveStraight, straight.Type);
        Assert.Equal(5, straight.Keys[0]);
        Assert.Equal(16, straight.Multiplier);
        Assert.Equal(HandType.FourOfAKind, bomb.Type);
        Assert.Equal(20, bomb.Multiplier);
        Assert.True(bomb.CompareTo(straight) > 0);
        Assert.True(straight.CompareTo(flush) > 0);
        Assert.Equal(10, HandEvaluator.EchoExtraAttack(bomb.UsedCards.Concat(new[] { C(Suit.Spade, Rank.King) }).ToArray(), bomb));
    }

    [Fact]
    public void Echo_WildAceCreatesBombAndPreservesRealCards()
    {
        var cards = new[] { C(Suit.Heart, Rank.Nine), C(Suit.Spade, Rank.Nine), C(Suit.Club, Rank.Nine), C(Suit.Diamond, Rank.Ace), C(Suit.Heart, Rank.Four) };
        var natural = HandEvaluator.EvaluateEcho(cards);
        var wild = HandEvaluator.EvaluateEcho(cards, rules: new HandEvalRules(false, false, true));
        Assert.NotEqual(HandType.FourOfAKind, natural.Type);
        Assert.Equal(HandType.FourOfAKind, wild.Type);
        Assert.Contains(C(Suit.Diamond, Rank.Ace), wild.UsedCards);
        Assert.Equal(4, HandEvaluator.EchoExtraAttack(cards, wild));
    }

    [Fact]
    public void Echo_FiveOrdinaryCards_UseBestThreeAndRemainingChips()
    {
        var cards = new[]
        {
            C(Suit.Heart, Rank.Nine), C(Suit.Spade, Rank.Nine),
            C(Suit.Club, Rank.Ace), C(Suit.Diamond, Rank.Four), C(Suit.Spade, Rank.Two)
        };
        var score = HandEvaluator.EvaluateEcho(cards);
        Assert.Equal(HandType.Pair, score.Type);
        Assert.Equal(3, score.UsedCards.Length);
        Assert.Equal(6, HandEvaluator.EchoExtraAttack(cards, score));
        Assert.Equal(2, HandEvaluator.EchoExtraAttack(cards, score, Suit.Diamond));
    }

    [Fact]
    public void Echo_DowngradeAndOrdinaryUpgrade_RespectHandLevels()
    {
        var bomb = HandEvaluator.EvaluateEcho(new[]
        {
            C(Suit.Heart, Rank.Nine), C(Suit.Spade, Rank.Nine),
            C(Suit.Club, Rank.Nine), C(Suit.Diamond, Rank.Nine)
        });
        Assert.Equal(9, bomb.CompareLevel);
        Assert.Equal(8, bomb.WithCompareLevelBonus(-1).CompareLevel);
        var ordinary = HandEvaluator.Evaluate(new[]
        {
            C(Suit.Heart, Rank.Nine), C(Suit.Spade, Rank.Nine), C(Suit.Club, Rank.Nine)
        });
        Assert.Equal(6, ordinary.WithCompareLevelBonus(2).CompareLevel);
    }

    [Fact]
    public void ChosenOne_DealAndReplace_KeepTripletsAndUniqueCards()
    {
        var tables = PvpTestTables.WithRelics(
            new[] { new RelicConfig { Id = 1, MechanismId = Array.Empty<int>(), BindingID = new[] { 114 } } },
            bindings: new[] { new BindingConfig { Id = 114, Type = BindingType.TheChosenOne } });
        var sawOnlyThree = false;
        for (var seed = 0; seed < 40; seed++)
        {
            var seats = new[] { new SeatSetup { Alive = true, IsHuman = true, HeroId = 1, RelicIds = new[] { 1 } }, new SeatSetup { Alive = true, IsHuman = true, HeroId = 1, RelicIds = new[] { 1 } } };
            var engine = new BattleEngine(new PvpMode(), seed, seats, tables);
            var state = engine.Apply(new BattleCommand { Type = BattleCommandType.DealHole });
            Assert.All(state.Hands.Take(2), hand => Assert.Contains(hand.GroupBy(c => c.Rank), group => group.Count() >= 3));
            sawOnlyThree |= state.Hands[0].GroupBy(c => c.Rank).Max(group => group.Count()) == 3;
            Assert.Equal(10, state.Hands.Take(2).SelectMany(h => h).Distinct().Count());
            state = engine.Apply(new BattleCommand { Type = BattleCommandType.Replace, SeatId = 0 });
            Assert.Contains(state.Hands[0].GroupBy(c => c.Rank), group => group.Count() >= 3);
            Assert.Equal(10, state.Hands.Take(2).SelectMany(h => h).Distinct().Count());
            for (var index = 0; index < 5; index++)
            {
                state = engine.Apply(new BattleCommand { Type = BattleCommandType.Rub, SeatId = 0, Index = index });
                Assert.Contains(state.Hands[0].GroupBy(c => c.Rank), group => group.Count() >= 3);
                Assert.Equal(10, state.Hands.Take(2).SelectMany(h => h).Distinct().Count());
            }
        }
        Assert.True(sawOnlyThree);
    }

    [Fact]
    public void RealPvpConfig_HasAllMembersForBothNewBindings()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Card/Assets/Res/Config"));
        var tables = PvpGameTables.Wrap(new CardShare.Infrastructure.Config.FileGameConfigLoader(directory).Load());
        Assert.True(PvpRelicBindings.IsTypeComplete(tables, new[] { 232, 233, 234, 302 }, BindingType.CardEcho));
        Assert.False(PvpRelicBindings.IsTypeComplete(tables, new[] { 232, 233, 234 }, BindingType.CardEcho));
        Assert.True(PvpRelicBindings.IsTypeComplete(tables, new[] { 405, 410, 411 }, BindingType.TheChosenOne));
        Assert.False(PvpRelicBindings.IsTypeComplete(tables, new[] { 405, 410 }, BindingType.TheChosenOne));
    }

    [Fact]
    public void PvpMatch_DebugGrantEchoDuringFightAcceptsFiveCardsImmediately()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Card/Assets/Res/Config"));
        var tables = new CardShare.Infrastructure.Config.FileGameConfigLoader(directory).Load();
        var players = Enumerable.Range(0, 4)
            .Select(i => new PlayerPublic { UserId = "u" + i, NickName = "p" + i }).ToArray();
        var seats = players.Select((player, i) => new SeatSetup
        {
            SeatId = i, UserId = player.UserId, NickName = player.NickName,
            IsHuman = true, Alive = true, HeroId = 1, Attack = 100, Hp = 1000, MaxHp = 1000
        }).ToArray();
        var match = PvpMatch.Open(Guid.NewGuid(), 7, players, tables, seats);
        foreach (var id in new[] { 232, 233, 234, 302 })
            match.Act("u0", "debug_grant", id, Array.Empty<int>());
        match.Act("u0", PvpActions.Pick, 0, new[] { 0, 1, 2, 3, 4 });
        var view = match.ViewFor("u0");
        var mySeat = view.Duel!.Seats.Single(seat => seat.UserId == "u0");
        Assert.Equal(5, mySeat.Selected!.Count);
    }

    [Fact]
    public void RealConfig_PvpDuel_DealsSelectsAndResolvesBothBindings()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Card/Assets/Res/Config"));
        var tables = PvpGameTables.Wrap(new CardShare.Infrastructure.Config.FileGameConfigLoader(directory).Load());
        for (var seed = 0; seed < 20; seed++)
        {
            var a = new SeatSetup { Alive = true, IsHuman = true, UserId = "a", HeroId = 1, Attack = 100, RelicIds = new[] { 232, 233, 234, 302 } };
            var b = new SeatSetup { Alive = true, IsHuman = true, UserId = "b", HeroId = 1, Attack = 100, RelicIds = new[] { 405, 410, 411 } };
            var duel = PvpDuelTable.Open(seed, a, b, tables, true, false, false);
            var before = duel.ViewFor("b", "test");
            var hand = before.Seats.Single(x => x.SeatId == 1).Cards;
            Assert.NotNull(hand);
            Assert.Contains(hand.GroupBy(c => c.Rank), group => group.Count() >= 3);
            duel.Pick(0, new[] { 0, 1, 2, 3, 4 });
            Assert.False(duel.LockSeat(0));
            Assert.True(duel.LockSeat(1));
            var snap = duel.Engine.Snapshot;
            Assert.Equal(HandType.ThreeOfAKind, snap.Scores[1].Type);
            Assert.Equal(6, snap.Scores[1].Multiplier);
            Assert.Equal(6, snap.Scores[1].Level);
            var after = duel.ViewFor("a", "test");
            var first = after.Seats.Single(x => x.SeatId == 0);
            var second = after.Seats.Single(x => x.SeatId == 1);
            Assert.Equal(5, first.Selected!.Count);
            Assert.Equal("ThreeOfAKind", second.HandType);
            Assert.Equal(6, second.Multiplier);
            Assert.Equal(snap.Damages[1], second.Damage);
            Assert.True(second.Damage > 0);
        }
    }

    [Fact]
    public void ChosenOne_StealLeavesBothTripletsIntact()
    {
        var tables = PvpTestTables.WithRelics(
            new[] { new RelicConfig { Id = 1, MechanismId = Array.Empty<int>(), BindingID = new[] { 114 } } },
            bindings: new[] { new BindingConfig { Id = 114, Type = BindingType.TheChosenOne } });
        var left = new SeatSetup { Alive = true, IsHuman = true, UserId = "a", HeroId = 1, RelicIds = new[] { 1 } };
        var right = new SeatSetup { Alive = true, IsHuman = true, UserId = "b", HeroId = 1, RelicIds = new[] { 1 } };
        var duel = PvpDuelTable.Open(17, left, right, tables, true, false, false);
        duel.Peek(0);
        Assert.True(duel.TryPeekSteal(0, new Random(9)));
        Assert.All(duel.Engine.Snapshot.Hands.Take(2),
            hand => Assert.Contains(hand.GroupBy(c => c.Rank), group => group.Count() >= 3));
        Assert.Equal(10, duel.Engine.Snapshot.Hands.Take(2).SelectMany(h => h).Distinct().Count());
    }

    [Fact]
    public void OrdinarySeatCannotPickFiveCards()
    {
        var tables = PvpTestTables.WithRelics(Array.Empty<RelicConfig>(), bindings: Array.Empty<BindingConfig>());
        var engine = new BattleEngine(new PvpMode(), 17,
            new[] { new SeatSetup { Alive = true, IsHuman = true } }, tables);
        engine.Apply(new BattleCommand { Type = BattleCommandType.DealHole });
        Assert.Throws<InvalidOperationException>(() =>
            engine.Apply(new BattleCommand { Type = BattleCommandType.Pick, SeatId = 0, Indexes = new[] { 0, 1, 2, 3, 4 } }));
    }

    [Fact]
    public void Echo_DuelSnapshotPreservesFiveSelectedCardsAndScore()
    {
        var tables = PvpTestTables.WithRelics(
            new[] { new RelicConfig { Id = 1, MechanismId = Array.Empty<int>(), BindingID = new[] { 113 } } },
            bindings: new[] { new BindingConfig { Id = 113, Type = BindingType.CardEcho } });
        var left = new SeatSetup { Alive = true, IsHuman = true, UserId = "a", HeroId = 1, Attack = 100, RelicIds = new[] { 1 } };
        var right = new SeatSetup { Alive = true, IsHuman = true, UserId = "b", HeroId = 1, Attack = 100 };
        var duel = PvpDuelTable.Open(42, left, right, tables, true, false, false);
        duel.Pick(0, new[] { 0, 1, 2, 3, 4 });
        Assert.False(duel.LockSeat(0));
        Assert.True(duel.LockSeat(1));
        var snapshot = duel.Engine.Snapshot;
        var wire = duel.ViewFor("a", "test");
        var mine = Assert.Single(wire.Seats, seat => seat.SeatId == 0);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, mine.Selected);
        Assert.Equal(snapshot.Scores[0].Type.ToString(), mine.HandType);
        Assert.Equal(snapshot.Scores[0].Multiplier, mine.Multiplier);
        Assert.Equal(snapshot.Damages[0], mine.Damage);
        Assert.Equal(5, duel.Engine.OpenCards(0).Count);
    }

    [Fact]
    public void Echo_UsesFiveCardPickAndBestThreeFallback()
    {
        var tables = PvpTestTables.WithRelics(
            new[] { new RelicConfig { Id = 1, MechanismId = Array.Empty<int>(), BindingID = new[] { 113 } } },
            bindings: new[] { new BindingConfig { Id = 113, Type = BindingType.CardEcho } });
        var seats = new[] { new SeatSetup { Alive = true, IsHuman = true, HeroId = 1, Attack = 100, RelicIds = new[] { 1 } }, new SeatSetup { Alive = true, IsHuman = true, HeroId = 1, Attack = 100 } };
        var engine = new BattleEngine(new PvpMode(), 12, seats, tables);
        engine.Apply(new BattleCommand { Type = BattleCommandType.DealHole });
        engine.Apply(new BattleCommand { Type = BattleCommandType.Pick, SeatId = 0, Indexes = new[] { 0, 1, 2, 3, 4 } });
        var snap = engine.Apply(new BattleCommand { Type = BattleCommandType.Showdown });
        Assert.Equal(5, engine.OpenCards(0).Count);
        Assert.Equal(5, snap.Picked[0].Count);
        Assert.True(snap.Scores[0].UsedCards.Length >= 3);
        var extra = HandEvaluator.EchoExtraAttack(engine.OpenCards(0), snap.Scores[0]);
        var expected = (int)Math.Round((100 + extra) * snap.Scores[0].Multiplier);
        if (!snap.Crits[0])
            Assert.Equal(expected, snap.Damages[0]);
    }
}


