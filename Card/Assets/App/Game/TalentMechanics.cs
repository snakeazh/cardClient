using System;
using App.Config;
using App.Talent;

namespace App.Game
{
    /// <summary>
    /// 按已拥有天赋当前等级结算局内效果。有效值 = Entry.Value × Level。
    /// 锋芒不禁用天赋。
    /// </summary>
    public static class TalentMechanics
    {
        public static float SumValue(ITalentService talent, MechanismType type)
        {
            var sum = 0f;
            ForEachOwned(talent, snapshot =>
            {
                if (snapshot.Entry != null && snapshot.Entry.Type == type)
                {
                    sum += snapshot.EffectiveValue;
                }
            });
            return sum;
        }

        public static float SumAttackExtra(ITalentService talent, HandScore score, RelicCombatContext ctx)
        {
            var extra = 0f;
            ForEachOwned(talent, snapshot =>
            {
                extra += AttackFromSnapshot(snapshot, score, ctx);
            });
            return extra;
        }

        public static float SumMultiplierExtra(ITalentService talent, bool firstShowThisStage)
        {
            return firstShowThisStage
                ? SumValue(talent, MechanismType.FirstShowCardEveryLevel)
                : 0f;
        }

        public static float SumDamagePercent(
            ITalentService talent,
            SeatState defender,
            SeatState player,
            int aliveEnemies)
        {
            var percent = 0f;
            if (defender != null && defender.IsBoss)
            {
                percent += SumValue(talent, MechanismType.AttackBossDamage);
            }

            if (aliveEnemies > 1)
            {
                percent += SumValue(talent, MechanismType.ManyMonsterDamage);
            }
            else if (aliveEnemies == 1)
            {
                percent += SumValue(talent, MechanismType.OneMonsterDamage);
            }

            if (IsBelowHpRatio(player, TalentBalance.LowHpRatio))
            {
                percent += SumValue(talent, MechanismType.HpUnderDamage);
            }

            return percent;
        }

        public static bool Roll(ITalentService talent, MechanismType type, Random rng)
        {
            var chance = SumValue(talent, type);
            if (chance <= 0f || rng == null)
            {
                return false;
            }

            return rng.NextDouble() < chance;
        }

        public static bool IsBelowHpRatio(SeatState seat, float ratio)
        {
            return seat != null && seat.MaxHp > 0 && (float)seat.Hp / seat.MaxHp < ratio;
        }

        public static float CriticalRate(ITalentService talent, HeroConfig hero)
        {
            var rate = hero != null ? hero.Critical : 0f;
            return rate + SumValue(talent, MechanismType.HeroCritical);
        }

        public static float CriticalDamageMultiplier(HeroConfig hero)
        {
            return hero != null && hero.CriticalDamage > 0f
                ? hero.CriticalDamage
                : TalentBalance.DefaultCriticalDamage;
        }

        public static string CollectAttackParts(ITalentService talent, HandScore score, RelicCombatContext ctx)
        {
            string text = null;
            ForEachOwned(talent, snapshot =>
            {
                var add = AttackFromSnapshot(snapshot, score, ctx);
                if (add == 0f)
                {
                    return;
                }

                var name = ResolveName(snapshot);
                var rounded = (int)Math.Round(add);
                var piece = $"{name}+{rounded}";
                text = text == null ? piece : text + ", " + piece;
            });
            return text ?? string.Empty;
        }

        public static string CollectMultiplierParts(ITalentService talent, bool firstShowThisStage)
        {
            if (!firstShowThisStage)
            {
                return string.Empty;
            }

            string text = null;
            ForEachOwned(talent, snapshot =>
            {
                if (snapshot.Entry == null ||
                    snapshot.Entry.Type != MechanismType.FirstShowCardEveryLevel)
                {
                    return;
                }

                var add = snapshot.EffectiveValue;
                if (add == 0f)
                {
                    return;
                }

                var name = ResolveName(snapshot);
                var piece = add == (int)add ? $"{name}+{(int)add}" : $"{name}+{add}";
                text = text == null ? piece : text + ", " + piece;
            });
            return text ?? string.Empty;
        }

        public static string CollectDamagePercentParts(
            ITalentService talent,
            SeatState defender,
            SeatState player,
            int aliveEnemies)
        {
            string text = null;
            ForEachOwned(talent, snapshot =>
            {
                var add = DamagePercentFromSnapshot(snapshot, defender, player, aliveEnemies);
                if (add == 0f)
                {
                    return;
                }

                var name = ResolveName(snapshot);
                var piece = add == (int)add ? $"{name}+{(int)add}" : $"{name}+{add}";
                text = text == null ? piece : text + ", " + piece;
            });
            return text ?? string.Empty;
        }

        private static float DamagePercentFromSnapshot(
            TalentSnapshot snapshot,
            SeatState defender,
            SeatState player,
            int aliveEnemies)
        {
            var value = snapshot.EffectiveValue;
            if (value == 0f || snapshot.Entry == null)
            {
                return 0f;
            }

            switch (snapshot.Entry.Type)
            {
                case MechanismType.AttackBossDamage:
                    return defender != null && defender.IsBoss ? value : 0f;
                case MechanismType.ManyMonsterDamage:
                    return aliveEnemies > 1 ? value : 0f;
                case MechanismType.OneMonsterDamage:
                    return aliveEnemies == 1 ? value : 0f;
                case MechanismType.HpUnderDamage:
                    return IsBelowHpRatio(player, TalentBalance.LowHpRatio) ? value : 0f;
                default:
                    return 0f;
            }
        }

        private static void ForEachOwned(ITalentService talent, Action<TalentSnapshot> action)
        {
            if (talent == null || action == null)
            {
                return;
            }

            var owned = talent.GetOwned();
            for (var i = 0; i < owned.Count; i++)
            {
                var snapshot = owned[i];
                if (snapshot == null || !snapshot.IsOwned || snapshot.Entry == null)
                {
                    continue;
                }

                action(snapshot);
            }
        }

        private static float AttackFromSnapshot(
            TalentSnapshot snapshot,
            HandScore score,
            RelicCombatContext ctx)
        {
            var value = snapshot.EffectiveValue;
            if (value == 0f)
            {
                return 0f;
            }

            switch (snapshot.Entry.Type)
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
                    return CountFace(score, ctx) * value;
                case MechanismType.UsingRubbingCards:
                    return ctx.RubbedThisHand ? value : 0f;
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

        private static int CountFace(HandScore score, RelicCombatContext ctx)
        {
            var cards = score.UsedCards;
            if (cards == null)
            {
                return 0;
            }

            if (ctx.TreatAllAsFace)
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

        private static string ResolveName(TalentSnapshot snapshot)
        {
            if (snapshot.Config != null && !string.IsNullOrEmpty(snapshot.Config.Name))
            {
                return snapshot.Config.Name;
            }

            return snapshot.Entry != null && !string.IsNullOrEmpty(snapshot.Entry.Name)
                ? snapshot.Entry.Name
                : snapshot.TalentId.ToString();
        }
    }
}
