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
    public void DealHoleGivesFiveCardsAndShowdownScoresPickedThree()
    {
        var tables = GameTables.Fallback();
        HandEvaluator.Tables = tables;
        var seats = new[]
        {
            new SeatSetup { SeatId = 0, IsHuman = true, Alive = true },
            new SeatSetup { SeatId = 1, IsHuman = true, Alive = true }
        };
        var engine = new BattleEngine(new PvpMode(), 42, seats, tables);
        engine.Apply(new BattleCommand { Type = BattleCommandType.DealHole });
        Assert.Equal(5, engine.Snapshot.Hands[0].Count);
        Assert.Equal(new[] { 0, 1, 2 }, engine.Snapshot.Picked[0]);

        var hole = engine.Snapshot.Hands[0];
        var pick = new[] { 0, 2, 4 };
        var expected = HandEvaluator.Evaluate(new[] { hole[0], hole[2], hole[4] });
        engine.Apply(new BattleCommand { Type = BattleCommandType.Pick, SeatId = 0, Indexes = pick });
        var snap = engine.Apply(new BattleCommand { Type = BattleCommandType.Showdown });
        Assert.Equal(expected.Type, snap.Scores[0].Type);
        Assert.Equal(expected.Level, snap.Scores[0].Level);
        Assert.Equal(pick, snap.Picked[0]);
    }

    [Fact]
    public void RubReplacesOneHoleCard()
    {
        var tables = GameTables.Fallback();
        var seats = new[]
        {
            new SeatSetup { SeatId = 0, IsHuman = true, Alive = true },
            new SeatSetup { SeatId = 1, IsHuman = true, Alive = true }
        };
        var engine = new BattleEngine(new PvpMode(), 9, seats, tables);
        engine.Apply(new BattleCommand { Type = BattleCommandType.DealHole });
        var before = engine.Snapshot.Hands[0][2];
        engine.Apply(new BattleCommand { Type = BattleCommandType.Rub, SeatId = 0, Index = 2 });
        Assert.NotEqual(before, engine.Snapshot.Hands[0][2]);
        Assert.Equal(5, engine.Snapshot.Hands[0].Count);
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
        Assert.Equal(4, table.Engine.Snapshot.Damages.Count);
        Assert.True(shown.Seats[0].Damage >= 1);
        Assert.True(shown.Seats[1].Damage >= 1);
    }

    [Fact]
    public void CombatDamageStacksAttackMagPercentAndCrit()
    {
        var result = CombatDamage.Resolve(new CombatDamageInput
        {
            IsPlayer = true,
            Attack = 10,
            HandTypeMag = 2f,
            RelicMagExtra = 0.5f,
            TalentMagExtra = 0.5f,
            RelicAttackExtra = 2,
            TalentAttackExtra = 3,
            FlintMultiplier = 1f,
            OutgoingDamagePercent = 0.2f,
            CritHit = true,
            CritMultiplier = 2f
        });
        Assert.Equal(45, result.FormulaDamage);
        Assert.Equal(108, result.Damage);
        Assert.True(result.Crit);
    }

    [Fact]
    public void CombatDamagePeaceZerosPlayerHit()
    {
        var result = CombatDamage.Resolve(new CombatDamageInput
        {
            IsPlayer = true,
            Attack = 10,
            HandTypeMag = 2f,
            PeaceHit = true
        });
        Assert.Equal(0, result.Damage);
        Assert.True(result.Peace);
    }

    [Fact]
    public void CombatDamageMonsterUsesOutgoingPercentAndThorns()
    {
        var result = CombatDamage.Resolve(new CombatDamageInput
        {
            IsPlayer = false,
            Attack = 10,
            HandTypeMag = 2f,
            MonsterOutgoingPercent = 0.5f,
            GoldThornExtra = 3
        });
        Assert.Equal(33, result.Damage);
    }

    [Fact]
    public void CombatBonusesMatchesPvePlayerFormula()
    {
        var tables = CombatTables();
        HandEvaluator.Tables = tables;
        var talents = new[] { new CombatTalentCount { TalentId = 101, Count = 1 } };
        var seat = CombatBonuses.BuildSeat(0, "u", "n", 1, talents, tables);
        Assert.Equal(10, seat.Attack);
        Assert.Equal(100, seat.Hp);

        var score = HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Heart, Rank.Two),
            new PlayingCard(Suit.Spade, Rank.Two),
            new PlayingCard(Suit.Club, Rank.Three)
        });
        var input = CombatBonuses.BuildPlayerInput(
            tables,
            seat,
            score,
            new CombatSituation
            {
                FirstShow = false,
                AliveOpponents = 3,
                AttackerHp = 100,
                AttackerMaxHp = 100,
                DefenderIsPlayer = true
            });
        input.CritHit = false;
        input.ExtraAttackHit = false;
        var result = CombatDamage.Resolve(input);
        Assert.Equal(10, input.TalentAttackExtra);
        Assert.Equal(0.2f, input.OutgoingDamagePercent);
        Assert.False(input.CanExecute);
        Assert.Equal(40, result.FormulaDamage);
        Assert.Equal(48, result.Damage);
    }

    [Fact]
    public void PvpShowdownUsesHeroAndTalentLoadout()
    {
        var tables = CombatTables();
        var players = new[]
        {
            Public("甲"),
            Public("乙"),
            Public("丙"),
            Public("丁")
        };
        var talents = new[] { new CombatTalentCount { TalentId = 101, Count = 1 } };
        var seats = new SeatSetup[4];
        for (var i = 0; i < seats.Length; i++)
        {
            seats[i] = CombatBonuses.BuildSeat(i, players[i].UserId, players[i].NickName, 1, talents, tables);
        }

        var table = PvpBattleTable.Open(Guid.NewGuid(), 8, players, tables, seats);
        table.Showdown();
        Assert.Equal(10, table.Engine.Snapshot.Seats[0].Attack);
        Assert.Equal(101, table.Engine.Snapshot.Seats[0].Talents[0].TalentId);
        Assert.True(table.Engine.Snapshot.Damages[0] >= 10);
    }

    private static GameTables CombatTables()
    {
        var fallback = GameTables.Fallback();
        return new GameTables(
            fallback.GameConst,
            fallback.HandScores,
            fallback.Levels,
            new[]
            {
                new CardShare.Contracts.Config.HeroConfig
                {
                    Id = 1,
                    HeroDamage = 10,
                    Hp = 100,
                    Critical = 0f,
                    CriticalDamage = 2f,
                    HeroEntryId = new[] { 1 }
                }
            },
            fallback.Relics,
            fallback.UnlockConditions,
            new[]
            {
                new CardShare.Contracts.Config.TalentConfig
                {
                    Id = 1,
                    TalentId = 101,
                    TalentLevel = 1,
                    TalentEntry = 2
                }
            },
            fallback.Monsters,
            fallback.Items,
            "test-combat",
            new[]
            {
                new CardShare.Contracts.Config.HeroEntryConfig
                {
                    Id = 1,
                    Type = CardShare.Contracts.Config.MechanismType.Damage,
                    Value = 0.2f
                }
            },
            new[]
            {
                new CardShare.Contracts.Config.TalentEntryConfig
                {
                    Id = 2,
                    Type = CardShare.Contracts.Config.MechanismType.TwoCardAttack,
                    Value = 5f
                }
            },
            fallback.RelicEntries);
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
