using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using PlayingCard = CardShare.Battle.Card;
using Xunit;

namespace CardShare.Domain.Tests;

public class RelicCombatTests
{
    [Fact]
    public void FlushRelicAddsToHandTypeMagNotMultiplies()
    {
        var tables = RelicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();
        var snapshot = new RelicCombatSnapshot { RelicIds = new[] { 10 } };
        var first = RelicCombat.Evaluate(tables, snapshot, score);
        var second = RelicCombat.Evaluate(tables, snapshot, score);
        Assert.Equal(2f, first.MagExtra);
        Assert.Equal(0f, first.AttackExtra);
        Assert.Equal(first.MagExtra, second.MagExtra);
        Assert.Equal(first.AttackExtra, second.AttackExtra);

        var result = CombatDamage.Resolve(new CombatDamageInput
        {
            IsPlayer = true,
            Attack = 30,
            HandTypeMag = score.Multiplier,
            RelicMagExtra = first.MagExtra,
            CritHit = false,
            ExtraAttackHit = false
        });
        Assert.Equal(2.5f, score.Multiplier);
        Assert.Equal(135, result.FormulaDamage);
        Assert.Equal(135, result.Damage);
    }

    [Fact]
    public void PairRelicAddsAttackAndEveryRelicCountsIds()
    {
        var tables = RelicTables();
        HandEvaluator.Tables = tables;
        var score = AcePair();
        var snapshot = new RelicCombatSnapshot { RelicIds = new[] { 11, 12, 10 } };
        var eval = RelicCombat.Evaluate(tables, snapshot, score);
        Assert.Equal(5f, eval.AttackExtra);
        Assert.Equal(3f, eval.MagExtra);
    }

    [Fact]
    public void DisabledRelicIsSkippedAndHeartSuitCounts()
    {
        var tables = RelicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();
        var withHeart = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 13 } },
            score);
        Assert.Equal(3f, withHeart.MagExtra);

        var disabled = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot
            {
                RelicIds = new[] { 10, 13 },
                DisabledRelicIds = new[] { 10, 13 }
            },
            score);
        Assert.Equal(0f, disabled.MagExtra);
        Assert.Equal(0f, disabled.AttackExtra);
    }

    [Fact]
    public void CombatBonusesFillsRelicFieldsFromSeat()
    {
        var tables = RelicTables();
        HandEvaluator.Tables = tables;
        var talents = Array.Empty<CombatTalentCount>();
        var seat = CombatBonuses.BuildSeat(0, "u", "n", 1, talents, tables, new[] { 10, 11 });
        Assert.Equal(new[] { 10, 11 }, seat.RelicIds);
        seat.Attack = 30;
        var score = FlushHearts();
        var input = CombatBonuses.BuildPlayerInput(
            tables,
            seat,
            score,
            new CombatSituation
            {
                Shown = score.UsedCards,
                DefenderIsPlayer = true
            });
        input.CritHit = false;
        input.ExtraAttackHit = false;
        Assert.Equal(2f, input.RelicMagExtra);
        Assert.Equal(0, input.RelicAttackExtra);
        var result = CombatDamage.Resolve(input);
        Assert.Equal(135, result.Damage);
    }

    private static HandScore FlushHearts()
    {
        return HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Heart, Rank.Two),
            new PlayingCard(Suit.Heart, Rank.Four),
            new PlayingCard(Suit.Heart, Rank.Six)
        });
    }

    private static HandScore AcePair()
    {
        return HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Heart, Rank.Ace),
            new PlayingCard(Suit.Spade, Rank.Ace),
            new PlayingCard(Suit.Club, Rank.Three)
        });
    }

    private static GameTables RelicTables()
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
                    HeroDamage = 30,
                    Hp = 100,
                    Critical = 0f,
                    CriticalDamage = 2f,
                    HeroEntryId = Array.Empty<int>()
                }
            },
            new[]
            {
                new RelicConfig { Id = 10, Name = "白银法杖", MechanismId = new[] { 100 } },
                new RelicConfig { Id = 11, Name = "对子拳", MechanismId = new[] { 101 } },
                new RelicConfig { Id = 12, Name = "每件", MechanismId = new[] { 102 } },
                new RelicConfig { Id = 13, Name = "红心", MechanismId = new[] { 103 } }
            },
            fallback.UnlockConditions,
            fallback.TalentRows,
            fallback.Monsters,
            fallback.Items,
            "relic-combat",
            fallback.HeroEntries,
            fallback.TalentEntries,
            new[]
            {
                new RelicEntryConfig { Id = 100, Name = "金花倍率", Type = MechanismType.Flush, Value = new[] { 2f } },
                new RelicEntryConfig { Id = 101, Name = "对子攻击", Type = MechanismType.CoupletAttack, Value = new[] { 5f } },
                new RelicEntryConfig { Id = 102, Name = "每件倍率", Type = MechanismType.EveryRelic, Value = new[] { 1f } },
                new RelicEntryConfig { Id = 103, Name = "红心", Type = MechanismType.RedHeart, Value = new[] { 1f } }
            });
    }
}
