using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using Xunit;

namespace CardShare.Domain.Tests;

public class PvpRelicBindingsTests
{
    [Fact]
    public void IsComplete_RequiresEveryMemberAndAtLeastOne()
    {
        var tables = Tables(
            relics: new[]
            {
                Bond(1, 110),
                Bond(2, 110),
                Bond(3, 111)
            },
            bindings: new[]
            {
                new BindingConfig { Id = 110, Type = BindingType.RandomAttribute, Value = new[] { 3f, 10f, 0.01f } },
                new BindingConfig { Id = 111, Type = BindingType.DisableSkill, Value = new[] { 1f, 1f } }
            });

        var held = new List<PvpRelicBindings.BindingHold>();
        PvpRelicBindings.CollectHeld(tables, new[] { 1 }, held);
        Assert.Single(held);
        Assert.Equal(110, held[0].Binding.Id);
        Assert.False(held[0].Complete);
        Assert.Equal(1, held[0].Owned);
        Assert.Equal(2, held[0].Required);
        PvpRelicBindings.CollectHeld(tables, new[] { 1, 2 }, held);
        Assert.True(held[0].Complete);
        PvpRelicBindings.CollectHeld(tables, Array.Empty<int>(), held);
        Assert.Empty(held);

        Assert.False(PvpRelicBindings.IsComplete(tables, new[] { 1 }, 110));
        Assert.True(PvpRelicBindings.IsComplete(tables, new[] { 1, 2 }, 110));
        Assert.False(PvpRelicBindings.IsComplete(tables, new[] { 1, 2 }, 111));
        Assert.True(PvpRelicBindings.IsComplete(tables, new[] { 3 }, 111));
    }

    [Fact]
    public void BindingMembers_UseTheCurrentModeRelicTable()
    {
        var fallback = GameTables.Fallback();
        var tables = new GameTables(
            fallback.GameConst, fallback.HandScores, fallback.Levels, fallback.Heroes,
            new[] { Bond(235, 104) },
            fallback.UnlockConditions, fallback.TalentRows, fallback.Monsters, fallback.Items,
            "binding-mode-test", fallback.HeroEntries, fallback.TalentEntries, fallback.RelicEntries,
            bindings: new[]
            {
                new BindingConfig { Id = 104, Type = BindingType.LuckyNumber, Value = new[] { 1f, 0.1f } }
            },
            pvpRelics: new[]
            {
                new PvpRelicConfig { Id = 232, BindingID = new[] { 104 } }
            });

        var pvpTables = PvpGameTables.Wrap(tables);
        var held = new List<PvpRelicBindings.BindingHold>();
        PvpRelicBindings.CollectHeld(tables, new[] { 235 }, held);
        Assert.True(Assert.Single(held).Complete);
        Assert.True(PvpRelicBindings.IsComplete(tables, new[] { 235 }, 104));
        Assert.False(PvpRelicBindings.IsComplete(tables, new[] { 232 }, 104));
        Assert.Equal(1, PvpRelicBindings.LuckyNumberEnhanceCount(tables, new[] { 235 }));

        PvpRelicBindings.CollectHeld(pvpTables, new[] { 232 }, held);
        Assert.True(Assert.Single(held).Complete);
        Assert.True(PvpRelicBindings.IsComplete(pvpTables, new[] { 232 }, 104));
        Assert.False(PvpRelicBindings.IsComplete(pvpTables, new[] { 235 }, 104));
    }

    [Fact]
    public void LuckyNumber_OnlyMatchesEnhancedSuitAndRank()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 104) },
            bindings: new[]
            {
                new BindingConfig { Id = 104, Type = BindingType.LuckyNumber, Value = new[] { 1f, 1f } }
            });
        var enhanced = new[] { new Card(Suit.Heart, Rank.Five).ResourceId };

        Assert.True(PvpRelicBindings.TryLuckyForceWin(tables, new[] { 1 }, enhanced,
            new[] { new Card(Suit.Heart, Rank.Five) }, new Random(1)));
        Assert.False(PvpRelicBindings.TryLuckyForceWin(tables, new[] { 1 }, enhanced,
            new[] { new Card(Suit.Spade, Rank.Five) }, new Random(1)));
        Assert.False(PvpRelicBindings.TryLuckyForceWin(tables, new[] { 1 }, enhanced,
            new[] { new Card(Suit.Heart, Rank.Six) }, new Random(1)));
    }

    [Fact]
    public void Leiting_OnlyOnCrit_UsesAttackNotDamage()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 103) },
            bindings: new[]
            {
                new BindingConfig { Id = 103, Type = BindingType.Leiting, Value = new[] { 1f, 5f } }
            });
        var owned = new[] { 1 };
        Assert.Equal(0, PvpRelicBindings.BonusDamage(tables, owned, damage: 10, attack: 20, isCrit: false));
        Assert.Equal(100, PvpRelicBindings.BonusDamage(tables, owned, damage: 10, attack: 20, isCrit: true));
    }

    [Fact]
    public void BronzeBookAndMagicCoinAndDoubleAttackHelpers()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 101), Bond(2, 106), Bond(3, 109) },
            bindings: new[]
            {
                new BindingConfig { Id = 101, Type = BindingType.BronzeBook, Value = new[] { 1f } },
                new BindingConfig { Id = 106, Type = BindingType.MagicCoin, Value = new[] { 0.025f } },
                new BindingConfig { Id = 109, Type = BindingType.DoubleAttack, Value = new[] { 1f, -0.4f } }
            });

        Assert.Equal(1, PvpRelicBindings.BronzeBookAttack(tables, new[] { 1 }));
        Assert.Equal(0.025f, PvpRelicBindings.MagicCoinSpendBonus(tables, new[] { 2 }));
        Assert.Equal(1, PvpRelicBindings.DoubleAttackTimes(tables, new[] { 3 }));
        Assert.Equal(0.6f, PvpRelicBindings.DoubleAttackDamageScale(tables, new[] { 3 }), 3);
    }

    [Fact]
    public void DisableSkillDamage_IsChargesTimesAttack()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 111) },
            bindings: new[]
            {
                new BindingConfig { Id = 111, Type = BindingType.DisableSkill, Value = new[] { 1f, 1f } }
            });
        Assert.Equal(0, PvpRelicBindings.DisableSkillDamage(tables, new[] { 1 }, skillCharges: 0, attack: 10));
        Assert.Equal(50, PvpRelicBindings.DisableSkillDamage(tables, new[] { 1 }, skillCharges: 5, attack: 10));
    }

    [Fact]
    public void RoundStartBronzeBook_RaisesCombatAttack()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 101) },
            bindings: new[]
            {
                new BindingConfig { Id = 101, Type = BindingType.BronzeBook, Value = new[] { 2f } }
            });
        var fighter = new PvpFighter();
        fighter.Combat.Attack = 10;
        fighter.OwnedRelicIds.Add(1);
        PvpRelicRuntime.ApplyBronzeBook(tables, fighter);
        Assert.Equal(12, fighter.Combat.Attack);
    }

    [Fact]
    public void OnGoldSpent_StacksMagicCoin()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 106) },
            bindings: new[]
            {
                new BindingConfig { Id = 106, Type = BindingType.MagicCoin, Value = new[] { 0.025f } }
            });
        var fighter = new PvpFighter();
        fighter.OwnedRelicIds.Add(1);
        PvpRelicRuntime.OnGoldSpent(tables, fighter);
        PvpRelicRuntime.OnGoldSpent(tables, fighter);
        Assert.Equal(0.05f, fighter.MagicCoinDamageBonus, 3);
    }

    [Fact]
    public void BloodCeremony_DamagesOpponentByHealedAmount()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 107) },
            bindings: new[]
            {
                new BindingConfig { Id = 107, Type = BindingType.BloodCeremony, Value = new[] { 1f } }
            });
        var healer = new PvpFighter { Hp = 50, MaxHp = 100 };
        healer.OwnedRelicIds.Add(1);
        var foe = new PvpFighter { Hp = 40, MaxHp = 40, Alive = true };
        PvpRelicRuntime.ApplyBloodCeremony(tables, healer, foe, healed: 12);
        Assert.Equal(28, foe.Hp);
    }

    [Fact]
    public void CurseSilenceRevive_ArmsOnceAndConsumes()
    {
        var tables = Tables(
            relics: new[] { Bond(1, 108) },
            bindings: new[]
            {
                new BindingConfig { Id = 108, Type = BindingType.CurseSilence, Value = new[] { 1f, 1f } }
            });
        var fighter = new PvpFighter { Hp = 0, MaxHp = 30, Alive = true };
        fighter.OwnedRelicIds.Add(1);
        Assert.True(PvpRelicRuntime.TryBindingRevive(tables, fighter));
        Assert.Equal(30, fighter.Hp);
        Assert.Equal(0, fighter.BindingReviveLeft);
        Assert.False(PvpRelicRuntime.TryBindingRevive(tables, fighter));
    }

    private static RelicConfig Bond(int id, int bindingId)
        => new RelicConfig
        {
            Id = id,
            Name = "b" + id,
            Type = QualityType.Ordinary,
            Price = 1,
            SellingPrice = 1,
            RefreshProbability = 1f,
            MechanismId = Array.Empty<int>(),
            BindingID = new[] { bindingId }
        };

    private static GameTables Tables(RelicConfig[] relics, BindingConfig[] bindings)
        => PvpTestTables.WithRelics(relics, bindings: bindings);
}
