using CardShare.Contracts;
using CardShare.Contracts.Config;
using CardShare.Domain.Players;

namespace CardShare.Domain;

public static class UnlockRules
{
    public static void Apply(
        UnlockState unlock,
        PveSettleStats stats,
        IReadOnlyList<UnlockConditionConfig> conditions)
    {
        foreach (var condition in conditions)
        {
            var delta = AmountFor((int)condition.Type, stats);
            if (delta <= 0)
            {
                continue;
            }

            var current = unlock.GetAmount(condition.Id);
            int next;
            if (ConditionTypes.UsesMax((int)condition.Type))
            {
                next = Math.Max(current, delta);
            }
            else
            {
                if (condition.StackedValue <= 0)
                {
                    continue;
                }

                next = current + delta * condition.StackedValue;
            }

            if (next != current)
            {
                unlock.SetAmount(condition.Id, next);
            }
        }
    }

    public static int AmountFor(int type, PveSettleStats stats)
    {
        return type switch
        {
            ConditionTypes.KillMonster => stats.KillMonster,
            ConditionTypes.ShuffleCard => stats.ShuffleCard,
            ConditionTypes.RefreshStore => stats.RefreshStore,
            ConditionTypes.Straight => stats.Straight,
            ConditionTypes.TwoThreeFive => stats.TwoThreeFive,
            ConditionTypes.ShuffleCardAndVictory => stats.ShuffleCardAndVictory,
            ConditionTypes.Seven => stats.Seven,
            ConditionTypes.Flush => stats.Flush,
            ConditionTypes.ClearDifficulty => stats.ClearDifficulty,
            ConditionTypes.AccumulateGold => stats.AccumulateGold,
            ConditionTypes.SingleDamage => stats.SingleDamage,
            ConditionTypes.Couplet => stats.Couplet,
            ConditionTypes.Failure => stats.Failure,
            ConditionTypes.Defeat => stats.Defeat,
            ConditionTypes.LuxuryGoods => stats.LuxuryGoods,
            ConditionTypes.Angel => stats.Angel,
            ConditionTypes.DeathNum => stats.DeathNum,
            ConditionTypes.Perspective => stats.Perspective,
            ConditionTypes.OneDamage => stats.OneDamage,
            ConditionTypes.NumberOfCoinsOwned => stats.NumberOfCoinsOwned,
            ConditionTypes.CriticalNum => stats.CriticalNum,
            ConditionTypes.ThreeCardAttack => stats.ThreeCardAttack,
            _ => 0
        };
    }
}
