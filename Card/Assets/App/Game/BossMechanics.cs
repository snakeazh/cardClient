using System;
using System.Collections.Generic;
using App.Config;
using CardShare.Contracts.Config;

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

        public static void GetScoreBan(
            RunState run,
            out Suit? banned,
            out Suit? banned2,
            out bool banFaces,
            out Suit? banned3)
        {
            banned = null;
            banned2 = null;
            banned3 = null;
            banFaces = Has(run, BossEntryType.DisableHeadUesd);
            var suits = new List<Suit>(4);
            AddScoreBanSuit(suits, run, BossEntryType.DisableRedSuit, Suit.Diamond);
            AddScoreBanSuit(suits, run, BossEntryType.DisableRedSuit, Suit.Heart);
            AddScoreBanSuit(suits, run, BossEntryType.DisableBlackSuit, Suit.Spade);
            AddScoreBanSuit(suits, run, BossEntryType.DisableBlackSuit, Suit.Club);
            AddScoreBanSuit(suits, run, BossEntryType.DisableHeartUesd, Suit.Heart);
            AddScoreBanSuit(suits, run, BossEntryType.DisableSpadeUesd, Suit.Spade);
            AddScoreBanSuit(suits, run, BossEntryType.DisableDiamonUesdd, Suit.Diamond);
            AddScoreBanSuit(suits, run, BossEntryType.DisablePlumBlossomUesd, Suit.Club);
            if (suits.Count > 0)
            {
                banned = suits[0];
            }

            if (suits.Count > 1)
            {
                banned2 = suits[1];
            }

            if (suits.Count > 2)
            {
                banned3 = suits[2];
            }
        }

        private static void AddScoreBanSuit(List<Suit> suits, RunState run, BossEntryType type, Suit suit)
        {
            if (suits == null || !Has(run, type) || suits.Contains(suit))
            {
                return;
            }

            suits.Add(suit);
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

        public static int ChipValueOf(RunState run, Card card)
        {
            if (!card.IsValid)
            {
                return 0;
            }

            if (card.Rank == Rank.Ace && Has(run, BossEntryType.AceDown))
            {
                return Math.Max(0, (int)Math.Round(ValueAt(run, BossEntryType.AceDown)));
            }

            if (card.IsFace && Has(run, BossEntryType.FaceDevalue))
            {
                return Math.Max(0, (int)Math.Round(ValueAt(run, BossEntryType.FaceDevalue)));
            }

            return card.ChipValue;
        }

        public static HandScore ApplyChipOverride(RunState run, HandScore score)
        {
            if (!Has(run, BossEntryType.FaceDevalue) && !Has(run, BossEntryType.AceDown))
            {
                return score;
            }

            var used = score.UsedCards;
            if (used == null || used.Length == 0)
            {
                return score;
            }

            var chips = 0;
            for (var i = 0; i < used.Length; i++)
            {
                chips += ChipValueOf(run, used[i]);
            }

            return score.WithBaseChips(chips);
        }

        public static int RubChargeDelta(RunState run)
        {
            if (!Has(run, BossEntryType.RubDown))
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(ValueAt(run, BossEntryType.RubDown)));
        }

        public static bool SwapLocked(RunState run)
        {
            return Has(run, BossEntryType.SwapLock);
        }

        public static int RubFeeGold(RunState run)
        {
            if (!Has(run, BossEntryType.RubFee))
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(ValueAt(run, BossEntryType.RubFee)));
        }

        public static bool CanAffordRub(RunState run)
        {
            var fee = RubFeeGold(run);
            return fee <= 0 || (run != null && run.Gold >= fee);
        }

        public static float MonsterRegenRatio(RunState run)
        {
            return Has(run, BossEntryType.MonsterRegen) ? ValueAt(run, BossEntryType.MonsterRegen) : 0f;
        }

        public static int MonsterGrowAttack(RunState run)
        {
            if (!Has(run, BossEntryType.MonsterGrow))
            {
                return 0;
            }

            return (int)Math.Round(ValueAt(run, BossEntryType.MonsterGrow));
        }

        public static int ThornShellSelfDamage(RunState run, int dealt)
        {
            if (!Has(run, BossEntryType.ThornShell) || dealt <= 0)
            {
                return 0;
            }

            var ratio = ValueAt(run, BossEntryType.ThornShell);
            if (ratio <= 0f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(dealt * ratio));
        }

        public static int VengefulSoulAttackDelta(RunState run)
        {
            if (!Has(run, BossEntryType.VengefulSoul))
            {
                return 0;
            }

            return (int)Math.Round(ValueAt(run, BossEntryType.VengefulSoul));
        }

        public static float HealMultiplier(RunState run)
        {
            if (!Has(run, BossEntryType.HealBan))
            {
                return 1f;
            }

            return Math.Max(0f, 1f + ValueAt(run, BossEntryType.HealBan));
        }

        public static bool TieLoses(RunState run)
        {
            return Has(run, BossEntryType.TieLose);
        }

        public static bool ShouldTriggerPhaseRage(RunState run, SeatState enemy)
        {
            if (!Has(run, BossEntryType.PhaseRage) ||
                enemy == null ||
                enemy.IsPlayer ||
                enemy.PhaseRageTriggered ||
                enemy.MaxHp <= 0 ||
                enemy.Hp <= 0)
            {
                return false;
            }

            var threshold = ValueAt(run, BossEntryType.PhaseRage);
            if (threshold <= 0f)
            {
                return false;
            }

            return enemy.Hp / (float)enemy.MaxHp < threshold;
        }

        public static int PhaseRageAttackBonus(RunState run, int baseAttack)
        {
            if (!Has(run, BossEntryType.PhaseRage) || baseAttack <= 0)
            {
                return 0;
            }

            var bonus = ValueAt(run, BossEntryType.PhaseRage, 1);
            if (bonus <= 0f)
            {
                return 0;
            }

            return (int)Math.Round(baseAttack * bonus);
        }

        public static bool CanSecondWind(RunState run, SeatState enemy)
        {
            return Has(run, BossEntryType.SecondWind) &&
                   enemy != null &&
                   !enemy.IsPlayer &&
                   !enemy.SecondWindUsed &&
                   enemy.MaxHp > 0;
        }

        public static int SecondWindHp(RunState run, int maxHp)
        {
            if (maxHp <= 0)
            {
                return 1;
            }

            var ratio = ValueAt(run, BossEntryType.SecondWind);
            return Math.Max(1, (int)Math.Round(maxHp * Math.Max(0f, ratio)));
        }

        public static int LifeSiphonHeal(RunState run, int dealt)
        {
            if (!Has(run, BossEntryType.LifeSiphon) || dealt <= 0)
            {
                return 0;
            }

            var ratio = ValueAt(run, BossEntryType.LifeSiphon);
            if (ratio <= 0f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(dealt * ratio));
        }
    }
}
