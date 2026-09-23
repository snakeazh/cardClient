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

    [Fact]
    public void FixedTypeCountMultScalesWithRunWideShowCounts()
    {
        var tables = NewMechanicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();
        var counts = new int[7];
        counts[(int)CardShare.Battle.HandType.ThreeOfAKind] = 2;

        var result = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 435 }, HandTypeShowCounts = counts },
            score);
        Assert.Equal(20f, result.MagExtra);

        var empty = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 435 } },
            score);
        Assert.Equal(0f, empty.MagExtra);
    }

    [Fact]
    public void ShopRefreshGetMultReadsPerRelicCount()
    {
        var tables = NewMechanicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();

        var result = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot
            {
                RelicIds = new[] { 436 },
                RelicShopRefreshCounts = new Dictionary<int, int> { [436] = 3 }
            },
            score);
        Assert.Equal(3f, result.MagExtra);

        var missing = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 436 } },
            score);
        Assert.Equal(0f, missing.MagExtra);
    }

    [Fact]
    public void SelfDecayMultReadsRemainingMag()
    {
        var tables = NewMechanicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();

        var result = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot
            {
                RelicIds = new[] { 437 },
                RelicSelfDecayMag = new Dictionary<int, float> { [437] = 17f }
            },
            score);
        Assert.Equal(17f, result.MagExtra);
    }

    [Fact]
    public void WildAceCountsAceAsEverySuit()
    {
        var tables = NewMechanicTables();
        HandEvaluator.Tables = tables;
        // 两张红心 + 一张黑桃 A。
        var score = HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Spade, Rank.Ace),
            new PlayingCard(Suit.Heart, Rank.Four),
            new PlayingCard(Suit.Heart, Rank.Six)
        });

        var noWild = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 13 } },
            score);
        Assert.Equal(2f, noWild.MagExtra);

        var wild = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 13, 439 } },
            score);
        Assert.Equal(3f, wild.MagExtra);
    }

    private static GameTables NewMechanicTables()
    {
        var fallback = GameTables.Fallback();
        return new GameTables(
            fallback.GameConst,
            fallback.HandScores,
            fallback.Levels,
            fallback.Heroes,
            new[]
            {
                new RelicConfig { Id = 13, Name = "红心", MechanismId = new[] { 103 } },
                new RelicConfig { Id = 334, Name = "国王领域", MechanismId = new[] { 30036 } },
                new RelicConfig { Id = 331, Name = "倍率叠加", MechanismId = new[] { 30033 } },
                new RelicConfig { Id = 237, Name = "贪婪", MechanismId = new[] { 20038 } },
                new RelicConfig { Id = 435, Name = "炸弹恶魔", MechanismId = new[] { 40034 } },
                new RelicConfig { Id = 436, Name = "消费主义", MechanismId = new[] { 40035 } },
                new RelicConfig { Id = 437, Name = "高档饮品", MechanismId = new[] { 40036 } },
                new RelicConfig { Id = 439, Name = "万能A", MechanismId = new[] { 40038 } }
            },
            fallback.UnlockConditions,
            fallback.TalentRows,
            fallback.Monsters,
            fallback.Items,
            "relic-new-mechanics",
            fallback.HeroEntries,
            fallback.TalentEntries,
            new[]
            {
                new RelicEntryConfig { Id = 103, Name = "红心", Type = MechanismType.RedHeart, Value = new[] { 1f } },
                new RelicEntryConfig { Id = 30036, Name = "国王领域", Type = MechanismType.UnshownRankMult, Value = new[] { 13f, 9f } },
                new RelicEntryConfig { Id = 30033, Name = "倍率叠加", Type = MechanismType.UseConsumableGetMult, Value = new[] { 0.5f } },
                new RelicEntryConfig { Id = 20038, Name = "贪婪", Type = MechanismType.SelfMultWinLose, Value = new[] { 1f, 1f } },
                new RelicEntryConfig { Id = 40034, Name = "炸弹恶魔", Type = MechanismType.FixedTypeCountMult, Value = new[] { 6f, 10f } },
                new RelicEntryConfig { Id = 40035, Name = "消费主义", Type = MechanismType.ShopRefreshGetMult, Value = new[] { 1f } },
                new RelicEntryConfig { Id = 40036, Name = "高档饮品", Type = MechanismType.SelfDecayMult, Value = new[] { 20f, 1f } },
                new RelicEntryConfig { Id = 40038, Name = "万能A", Type = MechanismType.WildAce, Value = new[] { 1f } }
            });
    }

    [Fact]
    public void UnshownRankMultCountsKingsInUnshown()
    {
        var tables = NewMechanicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();
        var unshown = new[]
        {
            new PlayingCard(Suit.Spade, Rank.King),
            new PlayingCard(Suit.Heart, Rank.Two)
        };

        var result = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 334 }, Unshown = unshown },
            score);
        Assert.Equal(9f, result.MagExtra);
    }

    [Fact]
    public void UseConsumableGetMultScalesWithUses()
    {
        var tables = NewMechanicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();

        var result = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot { RelicIds = new[] { 331 }, ConsumableUsesThisRun = 4 },
            score);
        Assert.Equal(2f, result.MagExtra);
    }

    [Fact]
    public void SelfMultWinLoseReadsPerRelicMag()
    {
        var tables = NewMechanicTables();
        HandEvaluator.Tables = tables;
        var score = FlushHearts();

        var result = RelicCombat.Evaluate(
            tables,
            new RelicCombatSnapshot
            {
                RelicIds = new[] { 237 },
                RelicWinLoseMag = new Dictionary<int, float> { [237] = 3f }
            },
            score);
        Assert.Equal(3f, result.MagExtra);
    }

    [Fact]
    public void WildAceImprovesHandType()
    {
        HandEvaluator.Tables = GameTables.Fallback();
        // 黑桃 A + 红心 4/6：自然散牌；WildAce 可变同花或更好。
        var cards = new[]
        {
            new PlayingCard(Suit.Spade, Rank.Ace),
            new PlayingCard(Suit.Heart, Rank.Four),
            new PlayingCard(Suit.Heart, Rank.Six)
        };
        var natural = HandEvaluator.Evaluate(cards);
        var wild = HandEvaluator.Evaluate(cards, rules: new HandEvalRules(false, false, wildAce: true));
        Assert.True(wild.CompareTo(natural) > 0);
        Assert.True(wild.Type == CardShare.Battle.HandType.Flush ||
                    wild.Type == CardShare.Battle.HandType.StraightFlush);
    }
}
