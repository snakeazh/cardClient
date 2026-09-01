using System;
using System.Collections.Generic;
using App.Config;

namespace App.Game
{
    /// <summary>
    /// 按本关随机抽中的 <see cref="BossEntryConfig"/> 结算 BOSS 机制。非 BOSS 关没有词条。
    /// </summary>
    public static class BossMechanics
    {
        public static BossEntryConfig Resolve(RunState run)
        {
            return run != null && run.BossEntryId > 0 ? BossEntryConfig.Get(run.BossEntryId) : null;
        }

        public static bool Has(RunState run, BossEntryType type)
        {
            var entry = Resolve(run);
            return entry != null && entry.Type == type;
        }

        public static float ValueAt(RunState run, int index = 0)
        {
            return ValueAt(Resolve(run), index);
        }

        public static float ValueAt(BossEntryConfig entry, int index = 0)
        {
            if (entry?.Value == null || index < 0 || index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
        }

        public static int PickRandomId(Random rng)
        {
            if (rng == null || BossEntryConfig.Count <= 0)
            {
                return 0;
            }

            var ids = new List<int>(BossEntryConfig.Count);
            foreach (var pair in BossEntryConfig.All)
            {
                if (pair.Value != null && pair.Key > 0)
                {
                    ids.Add(pair.Key);
                }
            }

            if (ids.Count == 0)
            {
                return 0;
            }

            return ids[rng.Next(ids.Count)];
        }

        public static int PlayerCardsDealt(RunState run)
        {
            if (!Has(run, BossEntryType.HandCompress))
            {
                return GameBalance.PlayerCardsDealt;
            }

            var n = (int)Math.Round(ValueAt(run));
            if (n <= 0)
            {
                n = 4;
            }

            return Math.Max(GameBalance.OpenHandSize, Math.Min(GameBalance.MaxCardsPerSeat, n));
        }

        public static float FlintMultiplier(RunState run)
        {
            return Has(run, BossEntryType.Flint) ? 1f + ValueAt(run) : 1f;
        }

        public static float PlayerOutgoingDamagePercent(RunState run, HandType type)
        {
            var percent = 0f;
            if (Has(run, BossEntryType.PlayerDamageDown))
            {
                percent += ValueAt(run);
            }

            if (type == HandType.Flush && Has(run, BossEntryType.FlushDamage))
            {
                percent += ValueAt(run);
            }

            if (type == HandType.StraightFlush && Has(run, BossEntryType.FlushStraightDamage))
            {
                percent += ValueAt(run);
            }

            if (type == HandType.Straight && Has(run, BossEntryType.StraightDamage))
            {
                percent += ValueAt(run);
            }

            return percent;
        }

        public static float MonsterOutgoingDamagePercent(RunState run)
        {
            return Has(run, BossEntryType.MonsterDamageUp) ? ValueAt(run) : 0f;
        }

        public static int GoldThornExtra(RunState run)
        {
            if (!Has(run, BossEntryType.GoldThorn) || run == null)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(run.GoldSpentThisRun * ValueAt(run)));
        }

        public static float IncomingDamagePercent(RunState run)
        {
            return Has(run, BossEntryType.CurseBody) ? ValueAt(run) : 0f;
        }

        public static int CurseBodySelfDamage(RunState run, int currentHp)
        {
            if (!Has(run, BossEntryType.CurseBody) || currentHp <= 0)
            {
                return 0;
            }

            var ratio = Math.Abs(ValueAt(run, 1));
            if (ratio <= 0f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(currentHp * ratio));
        }

        public static int RageAttackBonus(RunState run, SeatState boss, int coreAttack)
        {
            if (!Has(run, BossEntryType.BossRage) || boss == null || !boss.IsBoss || boss.MaxHp <= 0)
            {
                return 0;
            }

            var step = ValueAt(run);
            if (step <= 0f || coreAttack <= 0)
            {
                return 0;
            }

            var lost = 1f - boss.Hp / (float)boss.MaxHp;
            var stacks = (int)Math.Floor(lost / step + 1e-5f);
            if (stacks <= 0)
            {
                return 0;
            }

            return (int)Math.Round(coreAttack * stacks * ValueAt(run, 1));
        }

        public static void GetRubBan(RunState run, out Suit? bannedSuit, out bool banFaces)
        {
            bannedSuit = null;
            banFaces = Has(run, BossEntryType.DisableHead);
            if (Has(run, BossEntryType.DisableHeart))
            {
                bannedSuit = Suit.Heart;
            }
            else if (Has(run, BossEntryType.DisableSpade))
            {
                bannedSuit = Suit.Spade;
            }
            else if (Has(run, BossEntryType.DisableDiamond))
            {
                bannedSuit = Suit.Diamond;
            }
            else if (Has(run, BossEntryType.DisablePlumBlossom))
            {
                bannedSuit = Suit.Club;
            }
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
            else if (Has(run, BossEntryType.DisableBlackSuit))
            {
                banned = Suit.Spade;
                banned2 = Suit.Club;
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

            var limit = (int)Math.Round(ValueAt(run));
            return limit > 0 && stageRound > limit;
        }

        public static float FragileBodyMaxHpPercent(RunState run)
        {
            return Has(run, BossEntryType.FragileBody) ? ValueAt(run) : 0f;
        }

        public static float MonsterEvadeChance(RunState run)
        {
            return Has(run, BossEntryType.MonsterEvade) ? ValueAt(run) : 0f;
        }

        public static float MonsterEvadeCounterFactor(RunState run)
        {
            return Has(run, BossEntryType.MonsterEvade) ? ValueAt(run, 1) : 0f;
        }

        public static int RelicDisableCount(RunState run)
        {
            if (!Has(run, BossEntryType.RelicDisable))
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(ValueAt(run)));
        }

        public static int ShieldHits(RunState run)
        {
            if (!Has(run, BossEntryType.BossShield))
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(ValueAt(run)));
        }

        public static float AttackStealRatio(RunState run)
        {
            return Has(run, BossEntryType.AttackSteal) ? ValueAt(run) : 0f;
        }
    }
}
