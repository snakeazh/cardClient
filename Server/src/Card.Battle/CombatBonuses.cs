using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

namespace CardShare.Battle;

/// <summary>
/// 把英雄面板和天赋词条填进 <see cref="CombatDamageInput"/>。
/// 口径与 Unity <c>TalentMechanics</c> / <c>HeroMechanics</c> / 英雄面板一致：有效值 = Entry.Value × Level。
/// PVP 无局内圣物、BOSS、燧石；斩杀不对玩家生效。
/// </summary>
public sealed class CombatSituation
{
    public bool FirstShow { get; set; }

    public bool RubbedThisHand { get; set; }

    public bool TreatAllAsFace { get; set; }

    public int AliveOpponents { get; set; }

    public int AttackerHp { get; set; }

    public int AttackerMaxHp { get; set; }

    public bool DefenderIsPlayer { get; set; }

    public bool DefenderIsBoss { get; set; }

    public int DefenderHp { get; set; }

    public int DefenderMaxHp { get; set; }
}

public readonly struct CombatPanelStats
{
    public CombatPanelStats(int attack, int hp, float critRate, float critMultiplier)
    {
        Attack = Math.Max(0, attack);
        Hp = Math.Max(0, hp);
        CritRate = Math.Max(0f, critRate);
        CritMultiplier = critMultiplier > 0f ? critMultiplier : CombatDamage.DefaultCritMultiplier;
    }

    public int Attack { get; }

    public int Hp { get; }

    public float CritRate { get; }

    public float CritMultiplier { get; }
}

public static class CombatBonuses
{
    public const int CopiesPerLevel = 1;

    public const float ExtraAttackDamageRatio = CombatDamage.ExtraAttackDamageRatio;

    public const float ExecuteHpRatio = 0.1f;

    public const float LowHpRatio = 0.2f;

    public static SeatSetup BuildSeat(
        int seatId,
        string userId,
        string nickName,
        int heroId,
        IReadOnlyList<CombatTalentCount>? talents,
        IGameTables tables)
    {
        var panel = EvaluatePanel(tables, heroId, talents);
        return new SeatSetup
        {
            SeatId = seatId,
            UserId = userId ?? string.Empty,
            NickName = nickName ?? string.Empty,
            IsHuman = true,
            Alive = true,
            HeroId = ResolveHeroId(tables, heroId),
            Attack = panel.Attack,
            Hp = panel.Hp,
            MaxHp = panel.Hp,
            Talents = CloneTalents(talents)
        };
    }

    public static CombatPanelStats EvaluatePanel(
        IGameTables tables,
        int heroId,
        IReadOnlyList<CombatTalentCount>? talents)
    {
        TryResolveHero(tables, heroId, out var hero);
        var attack = hero != null ? Math.Max(0, hero.HeroDamage) : 0;
        var hp = hero != null ? Math.Max(0, hero.Hp) : 0;
        var crit = hero != null ? hero.Critical : 0f;
        crit += SumHeroEntry(tables, hero, MechanismType.HeroCritical);
        crit += SumTalent(tables, talents, MechanismType.HeroCritical);
        attack += (int)Math.Round(SumTalent(tables, talents, MechanismType.HeroAttack));
        hp += (int)Math.Round(SumTalent(tables, talents, MechanismType.HeroHpMax));
        var critMul = hero != null && hero.CriticalDamage > 0f
            ? hero.CriticalDamage
            : CombatDamage.DefaultCritMultiplier;
        return new CombatPanelStats(attack, hp, crit, critMul);
    }

    public static CombatDamageInput BuildPlayerInput(
        IGameTables tables,
        SeatSetup seat,
        HandScore score,
        CombatSituation? situation)
    {
        situation ??= new CombatSituation();
        var talents = seat?.Talents;
        var panel = EvaluatePanel(tables, seat?.HeroId ?? 0, talents);
        var attack = seat != null && seat.Attack > 0 ? seat.Attack : panel.Attack;
        if (attack <= 0)
        {
            attack = 1;
        }

        var attackerHp = situation.AttackerMaxHp > 0 || situation.AttackerHp > 0
            ? situation.AttackerHp
            : seat?.Hp ?? 0;
        var attackerMaxHp = situation.AttackerMaxHp > 0 ? situation.AttackerMaxHp : seat?.MaxHp ?? 0;
        TryResolveHero(tables, seat?.HeroId ?? 0, out var hero);

        return new CombatDamageInput
        {
            IsPlayer = true,
            Attack = attack,
            HandTypeMag = score.Multiplier,
            TalentMagExtra = situation.FirstShow
                ? SumTalent(tables, talents, MechanismType.FirstShowCardEveryLevel)
                : 0f,
            TalentAttackExtra = (int)Math.Round(SumAttackExtra(tables, talents, score, situation)),
            FlintMultiplier = 1f,
            OutgoingDamagePercent = SumDamagePercent(tables, talents, situation, attackerHp, attackerMaxHp)
                + SumHeroEntry(tables, hero, MechanismType.Damage),
            CritRate = panel.CritRate,
            CritMultiplier = panel.CritMultiplier,
            ExtraAttackChance = SumTalent(tables, talents, MechanismType.ProOfExtraAttack),
            ExtraAttackDamageRatio = ExtraAttackDamageRatio,
            CanExecute = !situation.DefenderIsPlayer &&
                         !situation.DefenderIsBoss &&
                         IsBelowHpRatio(situation.DefenderHp, situation.DefenderMaxHp, ExecuteHpRatio),
            ExecuteChance = SumTalent(tables, talents, MechanismType.KillingProbabilityTen),
            DefenderHp = situation.DefenderHp
        };
    }

    public static IReadOnlyList<CombatTalentCount> CloneTalents(IReadOnlyList<CombatTalentCount>? talents)
    {
        if (talents == null || talents.Count == 0)
        {
            return Array.Empty<CombatTalentCount>();
        }

        var copy = new CombatTalentCount[talents.Count];
        for (var i = 0; i < talents.Count; i++)
        {
            var row = talents[i];
            copy[i] = row == null
                ? new CombatTalentCount()
                : new CombatTalentCount { TalentId = row.TalentId, Count = row.Count };
        }

        return copy;
    }

    private static int ResolveHeroId(IGameTables? tables, int heroId)
    {
        if (tables != null && tables.TryGetHero(heroId, out _))
        {
            return heroId;
        }

        var fallback = tables?.GameConst != null ? tables.GameConst.DefaultHeroId : 0;
        if (fallback > 0 && tables != null && tables.TryGetHero(fallback, out _))
        {
            return fallback;
        }

        return heroId;
    }

    private static bool TryResolveHero(IGameTables? tables, int heroId, out HeroConfig? hero)
    {
        hero = null;
        if (tables == null)
        {
            return false;
        }

        if (tables.TryGetHero(heroId, out hero))
        {
            return true;
        }

        var fallback = tables.GameConst != null ? tables.GameConst.DefaultHeroId : 0;
        return fallback > 0 && tables.TryGetHero(fallback, out hero);
    }

    private static float SumHeroEntry(IGameTables? tables, HeroConfig? hero, MechanismType type)
    {
        if (tables == null || hero?.HeroEntryId == null)
        {
            return 0f;
        }

        var sum = 0f;
        for (var i = 0; i < hero.HeroEntryId.Length; i++)
        {
            if (tables.TryGetHeroEntry(hero.HeroEntryId[i], out var entry) && entry.Type == type)
            {
                sum += entry.Value;
            }
        }

        return sum;
    }

    private static float SumTalent(
        IGameTables? tables,
        IReadOnlyList<CombatTalentCount>? talents,
        MechanismType type)
    {
        var sum = 0f;
        ForEachOwned(tables, talents, (level, entry) =>
        {
            if (entry.Type == type)
            {
                sum += entry.Value * level;
            }
        });
        return sum;
    }

    private static float SumAttackExtra(
        IGameTables? tables,
        IReadOnlyList<CombatTalentCount>? talents,
        HandScore score,
        CombatSituation situation)
    {
        var extra = 0f;
        ForEachOwned(tables, talents, (level, entry) =>
        {
            extra += AttackFromEntry(entry, level, score, situation);
        });
        return extra;
    }

    private static float SumDamagePercent(
        IGameTables? tables,
        IReadOnlyList<CombatTalentCount>? talents,
        CombatSituation situation,
        int attackerHp,
        int attackerMaxHp)
    {
        var percent = 0f;
        if (situation.DefenderIsBoss)
        {
            percent += SumTalent(tables, talents, MechanismType.AttackBossDamage);
        }

        if (situation.AliveOpponents > 1)
        {
            percent += SumTalent(tables, talents, MechanismType.ManyMonsterDamage);
        }
        else if (situation.AliveOpponents == 1)
        {
            percent += SumTalent(tables, talents, MechanismType.OneMonsterDamage);
        }

        if (attackerMaxHp > 0 && (float)attackerHp / attackerMaxHp < LowHpRatio)
        {
            percent += SumTalent(tables, talents, MechanismType.HpUnderDamage);
        }

        return percent;
    }

    private static float AttackFromEntry(TalentEntryConfig entry, int level, HandScore score, CombatSituation situation)
    {
        var value = entry.Value * level;
        if (value == 0f)
        {
            return 0f;
        }

        switch (entry.Type)
        {
            case MechanismType.TwoCardAttack:
                return CountRank(score, Rank.Two) * value;
            case MechanismType.ThreeCardAttack:
                return CountRank(score, Rank.Three) * value;
            case MechanismType.FourCardAttack:
                return CountRank(score, Rank.Four) * value;
            case MechanismType.FiveCardAttack:
                return CountRank(score, Rank.Five) * value;
            case MechanismType.SixCardAttack:
                return CountRank(score, Rank.Six) * value;
            case MechanismType.SevenCardAttack:
                return CountRank(score, Rank.Seven) * value;
            case MechanismType.EightCardAttack:
                return CountRank(score, Rank.Eight) * value;
            case MechanismType.NineCardAttack:
                return CountRank(score, Rank.Nine) * value;
            case MechanismType.TenCardAttack:
                return CountRank(score, Rank.Ten) * value;
            case MechanismType.ACardAttack:
                return CountRank(score, Rank.Ace) * value;
            case MechanismType.HeadCardAttack:
                return CountFace(score, situation.TreatAllAsFace) * value;
            case MechanismType.UsingRubbingCards:
                return situation.RubbedThisHand ? value : 0f;
            default:
                return 0f;
        }
    }

    private static int CountRank(HandScore score, Rank rank)
    {
        var cards = score.UsedCards;
        if (cards == null)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < cards.Length; i++)
        {
            if (cards[i].Rank == rank)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountFace(HandScore score, bool treatAllAsFace)
    {
        var cards = score.UsedCards;
        if (cards == null)
        {
            return 0;
        }

        if (treatAllAsFace)
        {
            return cards.Length;
        }

        var count = 0;
        for (var i = 0; i < cards.Length; i++)
        {
            if (cards[i].IsFace)
            {
                count++;
            }
        }

        return count;
    }

    private static void ForEachOwned(
        IGameTables? tables,
        IReadOnlyList<CombatTalentCount>? talents,
        Action<int, TalentEntryConfig> action)
    {
        if (tables == null || talents == null || action == null)
        {
            return;
        }

        for (var i = 0; i < talents.Count; i++)
        {
            var owned = talents[i];
            if (owned == null || owned.TalentId <= 0 || owned.Count <= 0)
            {
                continue;
            }

            var maxLevel = tables.GetTalentMaxLevel(owned.TalentId);
            var level = TalentLevel(owned.Count, maxLevel, CopiesPerLevel);
            if (level <= 0 || !tables.TryGetTalentRow(owned.TalentId, level, out var row))
            {
                continue;
            }

            if (!tables.TryGetTalentEntry(row.TalentEntry, out var entry) || entry == null)
            {
                continue;
            }

            action(level, entry);
        }
    }

    private static bool IsBelowHpRatio(int hp, int maxHp, float ratio)
    {
        return maxHp > 0 && (float)hp / maxHp < ratio;
    }

    private static int TalentLevel(int count, int maxLevel, int copiesPerLevel)
    {
        if (maxLevel <= 0 || copiesPerLevel <= 0)
        {
            return 0;
        }

        var raw = count / copiesPerLevel;
        return raw > maxLevel ? maxLevel : raw;
    }
}
