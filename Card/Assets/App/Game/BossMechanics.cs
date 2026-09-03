using System;
using System.Collections.Generic;
using App.Config;

namespace App.Game
{
    /// <summary>
    /// 按本关 <see cref="App.Level.LevelSnapshot.LevelEntryNum"/> 从 <see cref="BossEntryConfig"/> 随机抽中的机制结算。
    /// </summary>
    public static class BossMechanics
    {
        public static BossEntryConfig Find(RunState run, BossEntryType type)
        {
            if (run?.LevelEntryIds == null)
            {
                return null;
            }

            for (var i = 0; i < run.LevelEntryIds.Count; i++)
            {
                var entry = BossEntryConfig.Get(run.LevelEntryIds[i]);
                if (entry != null && entry.Type == type)
                {
                    return entry;
                }
            }

            return null;
        }

        public static List<BossEntryConfig> ResolveAll(RunState run)
        {
            var list = new List<BossEntryConfig>();
            if (run?.LevelEntryIds == null)
            {
                return list;
            }

            for (var i = 0; i < run.LevelEntryIds.Count; i++)
            {
                var entry = BossEntryConfig.Get(run.LevelEntryIds[i]);
                if (entry != null)
                {
                    list.Add(entry);
                }
            }

            return list;
        }

        public static bool Has(RunState run, BossEntryType type)
        {
            return Find(run, type) != null;
        }

        public static float ValueAt(RunState run, BossEntryType type, int index = 0)
        {
            return ValueAt(Find(run, type), index);
        }

        public static float ValueAt(BossEntryConfig entry, int index = 0)
        {
            if (entry?.Value == null || index < 0 || index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
        }

        public static void PickRandomIds(Random rng, int count, List<int> dest)
        {
            if (dest == null)
            {
                return;
            }

            dest.Clear();
            if (rng == null || count <= 0 || BossEntryConfig.Count <= 0)
            {
                return;
            }

            var pool = new List<int>(BossEntryConfig.Count);
            foreach (var pair in BossEntryConfig.All)
            {
                if (pair.Value != null && pair.Key > 0)
                {
                    pool.Add(pair.Key);
                }
            }

            var usedTypes = new HashSet<BossEntryType>();
            while (dest.Count < count && pool.Count > 0)
            {
                var i = rng.Next(pool.Count);
                var id = pool[i];
                pool.RemoveAt(i);
                var entry = BossEntryConfig.Get(id);
                if (entry == null || !usedTypes.Add(entry.Type))
                {
                    continue;
                }

                dest.Add(id);
            }
        }

        public static int PlayerCardsDealt(RunState run)
        {
            if (!Has(run, BossEntryType.HandCompress))
            {
                return GameBalance.PlayerCardsDealt;
            }

            var n = (int)Math.Round(ValueAt(run, BossEntryType.HandCompress));
            if (n <= 0)
            {
                n = 4;
            }

            return Math.Max(GameBalance.OpenHandSize, Math.Min(GameBalance.MaxCardsPerSeat, n));
        }

        public static float FlintMultiplier(RunState run)
        {
            return Has(run, BossEntryType.Flint) ? 1f + ValueAt(run, BossEntryType.Flint) : 1f;
        }

        public static float PlayerOutgoingDamagePercent(RunState run, HandType type)
        {
            var percent = 0f;
            if (Has(run, BossEntryType.PlayerDamageDown))
            {
                percent += ValueAt(run, BossEntryType.PlayerDamageDown);
            }

            if (type == HandType.Flush && Has(run, BossEntryType.FlushDamage))
            {
                percent += ValueAt(run, BossEntryType.FlushDamage);
            }

            if (type == HandType.Pair && Has(run, BossEntryType.CoupletDamage))
            {
                percent += ValueAt(run, BossEntryType.CoupletDamage);
            }

            if (type == HandType.Straight && Has(run, BossEntryType.StraightDamage))
            {
                percent += ValueAt(run, BossEntryType.StraightDamage);
            }

            return percent;
        }

        public static float MonsterOutgoingDamagePercent(RunState run)
        {
            return Has(run, BossEntryType.MonsterDamageUp) ? ValueAt(run, BossEntryType.MonsterDamageUp) : 0f;
        }

        public static int GoldThornExtra(RunState run)
        {
            if (!Has(run, BossEntryType.GoldThorn) || run == null)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(run.GoldSpentThisRun * ValueAt(run, BossEntryType.GoldThorn)));
        }

        public static float IncomingDamagePercent(RunState run)
        {
            return Has(run, BossEntryType.CurseBody) ? ValueAt(run, BossEntryType.CurseBody) : 0f;
        }

        public static int CurseBodySelfDamage(RunState run, int currentHp)
        {
            if (!Has(run, BossEntryType.CurseBody) || currentHp <= 0)
            {
                return 0;
            }

            var ratio = Math.Abs(ValueAt(run, BossEntryType.CurseBody, 1));
            if (ratio <= 0f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(currentHp * ratio));
        }

        public static int RageAttackBonus(RunState run, SeatState enemy, int coreAttack)
        {
            if (!Has(run, BossEntryType.MonsterRage) || enemy == null || enemy.MaxHp <= 0)
            {
                return 0;
            }

            var step = ValueAt(run, BossEntryType.MonsterRage);
            if (step <= 0f || coreAttack <= 0)
            {
                return 0;
            }

            var lost = 1f - enemy.Hp / (float)enemy.MaxHp;
            var stacks = (int)Math.Floor(lost / step + 1e-5f);
            if (stacks <= 0)
            {
                return 0;
            }

            return (int)Math.Round(coreAttack * stacks * ValueAt(run, BossEntryType.MonsterRage, 1));
        }

        public static bool IsRubBanned(RunState run, Card card)
        {
            if (!card.IsValid)
            {
                return false;
            }

            if (Has(run, BossEntryType.DisableHead) && card.IsFace)
            {
                return true;
            }

            if (Has(run, BossEntryType.DisableHeart) && card.Suit == Suit.Heart)
            {
                return true;
            }

            if (Has(run, BossEntryType.DisableSpade) && card.Suit == Suit.Spade)
            {
                return true;
            }

            if (Has(run, BossEntryType.DisableDiamond) && card.Suit == Suit.Diamond)
            {
                return true;
            }

            return Has(run, BossEntryType.DisablePlumBlossom) && card.Suit == Suit.Club;
        }

        public static void GetScoreBan(RunState run, out Suit? banned, out Suit? banned2, out bool banFaces)
        {
            banned = null;
            banned2 = null;
            banFaces = false;
            if (Has(run, BossEntryType.DisableRedSuit))
            {
                banned = Suit.Diamond;
                banned2 = Suit.Heart;
            }

            if (Has(run, BossEntryType.DisableBlackSuit))
            {
                if (!banned.HasValue)
                {
                    banned = Suit.Spade;
                    banned2 = Suit.Club;
                }
            }
        }

        public static bool SkillsDisabled(RunState run)
        {
            return Has(run, BossEntryType.SkillDisable);
        }

        public static bool HealBlocked(RunState run)
        {
            return Has(run, BossEntryType.FragileBody);
        }

        public static bool RoundLimitExceeded(RunState run, int stageRound)
        {
            if (!Has(run, BossEntryType.RoundLimit))
            {
                return false;
            }

            var limit = (int)Math.Round(ValueAt(run, BossEntryType.RoundLimit));
            return limit > 0 && stageRound > limit;
        }

        public static float FragileBodyMaxHpPercent(RunState run)
        {
            return Has(run, BossEntryType.FragileBody) ? ValueAt(run, BossEntryType.FragileBody) : 0f;
        }

        public static float MonsterEvadeChance(RunState run)
        {
            return Has(run, BossEntryType.MonsterEvade) ? ValueAt(run, BossEntryType.MonsterEvade) : 0f;
        }

        public static float MonsterEvadeCounterFactor(RunState run)
        {
            return Has(run, BossEntryType.MonsterEvade) ? ValueAt(run, BossEntryType.MonsterEvade, 1) : 0f;
        }

        public static int RelicDisableCount(RunState run)
        {
            if (!Has(run, BossEntryType.RelicDisable))
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(ValueAt(run, BossEntryType.RelicDisable)));
        }

        public static int ShieldHits(RunState run)
        {
            if (!Has(run, BossEntryType.MonsterShield))
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(ValueAt(run, BossEntryType.MonsterShield)));
        }

        public static float AttackStealRatio(RunState run)
        {
            return Has(run, BossEntryType.AttackSteal) ? ValueAt(run, BossEntryType.AttackSteal) : 0f;
        }
    }
}
