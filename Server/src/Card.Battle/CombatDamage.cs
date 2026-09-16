using System;

namespace CardShare.Battle;

/// <summary>
/// 出伤公式：攻击力 ×（牌型倍率 + 遗物/天赋倍率）× 燧石，再叠伤害%、暴击、追击、斩杀。
/// PVE 由客户端填入本地加成后调用；PVP 由服务器用 <c>CombatBonuses</c> 从英雄/天赋填同一套字段。
/// </summary>
public sealed class CombatDamageInput
{
    public bool IsPlayer { get; set; }

    public int Attack { get; set; }

    public float HandTypeMag { get; set; }

    public float RelicMagExtra { get; set; }

    public int RelicAttackExtra { get; set; }

    public float TalentMagExtra { get; set; }

    public int TalentAttackExtra { get; set; }

    public float FlintMultiplier { get; set; } = 1f;

    public float OutgoingDamagePercent { get; set; }

    public float CritRate { get; set; }

    public float CritMultiplier { get; set; } = CombatDamage.DefaultCritMultiplier;

    public float ExtraAttackChance { get; set; }

    public float ExtraAttackDamageRatio { get; set; } = CombatDamage.ExtraAttackDamageRatio;

    public bool CanExecute { get; set; }

    public float ExecuteChance { get; set; }

    public int DefenderHp { get; set; }

    public float MonsterOutgoingPercent { get; set; }

    public int GoldThornExtra { get; set; }

    public int UseDamageFixed { get; set; }

    public float PeaceChance { get; set; }

    public bool? CritHit { get; set; }

    public bool? ExtraAttackHit { get; set; }

    public bool? ExecuteHit { get; set; }

    public bool? PeaceHit { get; set; }
}

public sealed class CombatDamageResult
{
    public int Damage { get; init; }

    public int FormulaDamage { get; init; }

    public int WithoutTalent { get; init; }

    public int TalentDamage { get; init; }

    public float TotalMag { get; init; }

    public float RelicMag { get; init; }

    public int EffectiveAttack { get; init; }

    public int AttackExtra { get; init; }

    public bool Crit { get; init; }

    public int ChaseAdd { get; init; }

    public bool Execute { get; init; }

    public bool Peace { get; init; }
}

public static class CombatDamage
{
    public const float ExtraAttackDamageRatio = 0.1f;

    public const float DefaultCritMultiplier = 2f;

    public static CombatDamageResult Resolve(CombatDamageInput input, Random? rng = null)
    {
        if (input == null)
        {
            return new CombatDamageResult { Damage = 1, FormulaDamage = 1, WithoutTalent = 1 };
        }

        var flint = input.FlintMultiplier > 0f ? input.FlintMultiplier : 1f;
        var extra = input.RelicMagExtra + input.TalentMagExtra;
        var attackExtra = input.RelicAttackExtra + input.TalentAttackExtra;
        var totalMag = (input.HandTypeMag + extra) * flint;
        var relicMag = (input.HandTypeMag + input.RelicMagExtra) * flint;
        var effectiveAttack = input.Attack + attackExtra;
        var damage = HandEvaluator.ComputeAttackDamage(effectiveAttack, totalMag);
        var formulaDamage = damage;
        var withoutTalent = HandEvaluator.ComputeAttackDamage(
            input.Attack + input.RelicAttackExtra,
            relicMag);
        var crit = false;
        var chaseAdd = 0;
        var execute = false;
        var peace = false;

        if (input.IsPlayer)
        {
            if (input.OutgoingDamagePercent != 0f)
            {
                damage = Math.Max(1, (int)Math.Round(damage * (1f + input.OutgoingDamagePercent)));
            }

            var critMul = input.CritMultiplier > 0f ? input.CritMultiplier : DefaultCritMultiplier;
            if (Hit(input.CritHit, input.CritRate, rng))
            {
                crit = true;
                damage = Math.Max(1, (int)Math.Round(damage * critMul));
                withoutTalent = Math.Max(1, (int)Math.Round(withoutTalent * critMul));
            }

            var original = damage;
            if (Hit(input.ExtraAttackHit, input.ExtraAttackChance, rng))
            {
                var ratio = input.ExtraAttackDamageRatio > 0f
                    ? input.ExtraAttackDamageRatio
                    : ExtraAttackDamageRatio;
                chaseAdd = (int)Math.Round(original * ratio);
                damage += chaseAdd;
            }

            if (input.CanExecute && Hit(input.ExecuteHit, input.ExecuteChance, rng))
            {
                execute = true;
                damage = Math.Max(damage, Math.Max(0, input.DefenderHp));
            }
        }
        else
        {
            if (input.MonsterOutgoingPercent != 0f)
            {
                damage = Math.Max(1, (int)Math.Round(damage * (1f + input.MonsterOutgoingPercent)));
            }

            damage += Math.Max(0, input.GoldThornExtra);
        }

        if (input.IsPlayer && input.UseDamageFixed > 0)
        {
            damage = input.UseDamageFixed;
        }

        if (input.IsPlayer && Hit(input.PeaceHit, input.PeaceChance, rng))
        {
            peace = true;
            damage = 0;
        }

        var talentDamage = input.IsPlayer ? damage - withoutTalent : 0;
        if (damage <= 0)
        {
            return new CombatDamageResult
            {
                Damage = 0,
                FormulaDamage = formulaDamage,
                WithoutTalent = withoutTalent,
                TalentDamage = talentDamage,
                TotalMag = totalMag,
                RelicMag = relicMag,
                EffectiveAttack = effectiveAttack,
                AttackExtra = attackExtra,
                Crit = crit,
                ChaseAdd = chaseAdd,
                Execute = execute,
                Peace = peace
            };
        }

        return new CombatDamageResult
        {
            Damage = Math.Max(1, damage),
            FormulaDamage = formulaDamage,
            WithoutTalent = withoutTalent,
            TalentDamage = talentDamage,
            TotalMag = totalMag,
            RelicMag = relicMag,
            EffectiveAttack = effectiveAttack,
            AttackExtra = attackExtra,
            Crit = crit,
            ChaseAdd = chaseAdd,
            Execute = execute,
            Peace = peace
        };
    }

    private static bool Hit(bool? forced, float chance, Random? rng)
    {
        if (forced.HasValue)
        {
            return forced.Value;
        }

        if (chance <= 0f || rng == null)
        {
            return false;
        }

        return rng.NextDouble() < chance;
    }
}
