using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 把英雄面板和天赋词条填进 <see cref="CombatDamageInput"/>。
    /// 口径与 Unity <c>TalentMechanics</c> / <c>HeroMechanics</c> / 英雄面板一致：有效值 = Entry.Value × Level。
    /// PVP 带已解锁圣物的本手倍率/加攻；无局内商店叠层、BOSS、燧石；斩杀不对玩家生效。
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

        public IReadOnlyList<Card> Shown { get; set; } = Array.Empty<Card>();

        public IReadOnlyList<Card> Unshown { get; set; } = Array.Empty<Card>();

        public int RubsUsedThisHand { get; set; }

        public int PeekLeft { get; set; }

        public int XRayLeft { get; set; }

        public int ReplaceLeft { get; set; }

        public int LuckySevenHits { get; set; }
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
            IReadOnlyList<CombatTalentCount> talents,
            IGameTables tables)
        {
            return BuildSeat(seatId, userId, nickName, heroId, talents, tables, Array.Empty<int>());
        }

        public static SeatSetup BuildSeat(
            int seatId,
            string userId,
            string nickName,
            int heroId,
            IReadOnlyList<CombatTalentCount> talents,
            IGameTables tables,
            IReadOnlyList<int> relicIds)
        {
            var panel = EvaluatePanel(tables, heroId, talents);
            return new SeatSetup
            {
                SeatId = seatId,
                UserId = userId,
                NickName = nickName,
                IsHuman = true,
                Alive = true,
                HeroId = ResolveHeroId(tables, heroId),
                Attack = panel.Attack,
                Hp = panel.Hp,
                MaxHp = panel.Hp,
                Talents = CloneTalents(talents),
                RelicIds = CloneRelicIds(relicIds)
            };
        }

        public static CombatPanelStats EvaluatePanel(
            IGameTables tables,
            int heroId,
            IReadOnlyList<CombatTalentCount> talents)
        {
            TryResolveHero(tables, heroId, out var hero);
            var attack = 0;
            var hp = 0;
            var crit = 0f;
            var critMul = CombatDamage.DefaultCritMultiplier;
            if (hero != null)
            {
                attack = hero.HeroDamage;
                hp = hero.Hp;
                crit = hero.Critical;
                crit += SumHeroEntry(tables, hero, MechanismType.HeroCritical);
                if (hero.CriticalDamage > 0f)
                {
                    critMul = hero.CriticalDamage;
                }
            }

            crit += SumTalent(tables, talents, MechanismType.HeroCritical);
            attack += (int)Math.Round(SumTalent(tables, talents, MechanismType.HeroAttack));
            hp += (int)Math.Round(SumTalent(tables, talents, MechanismType.HeroHpMax));
            return new CombatPanelStats(attack, hp, crit, critMul);
        }

        public static CombatDamageInput BuildPlayerInput(
            IGameTables tables,
            SeatSetup seat,
            HandScore score,
            CombatSituation situation)
        {
            var talents = seat.Talents;
            var panel = EvaluatePanel(tables, seat.HeroId, talents);
            var attack = seat.Attack > 0 ? seat.Attack : panel.Attack;
            if (attack <= 0)
            {
                attack = 1;
            }

            var attackerHp = situation.AttackerMaxHp > 0 || situation.AttackerHp > 0
                ? situation.AttackerHp
                : seat.Hp;
            var attackerMaxHp = situation.AttackerMaxHp > 0 ? situation.AttackerMaxHp : seat.MaxHp;
            TryResolveHero(tables, seat.HeroId, out var hero);
            var relic = RelicCombat.Evaluate(tables, SnapshotFromSeat(seat, situation), score);

            return new CombatDamageInput
            {
                IsPlayer = true,
                Attack = attack,
                HandTypeMag = score.Multiplier,
                RelicMagExtra = relic.MagExtra,
                RelicAttackExtra = (int)Math.Round(relic.AttackExtra),
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

        /// <summary>每回合技能次数加成（搓牌 RubbingCardsNum / 透视 PerspectiveNum）：圣物 + 英雄 + 天赋词条求和，口径同 PvE ResetSkillCharges。</summary>
        public static int SumSkillCountBonus(
            IGameTables tables,
            IReadOnlyList<int> relicIds,
            int heroId,
            IReadOnlyList<CombatTalentCount> talents,
            MechanismType type)
        {
            var sum = SumRelicEntry(tables, relicIds, type);
            if (TryResolveHero(tables, heroId, out var hero))
            {
                sum += SumHeroEntry(tables, hero, type);
            }

            sum += SumTalent(tables, talents, type);
            return (int)Math.Round(sum);
        }

        public static IReadOnlyList<CombatTalentCount> CloneTalents(IReadOnlyList<CombatTalentCount> talents)
        {
            var copy = new CombatTalentCount[talents.Count];
            for (var i = 0; i < talents.Count; i++)
            {
                var row = talents[i];
                copy[i] = new CombatTalentCount { TalentId = row.TalentId, Count = row.Count };
            }

            return copy;
        }

        public static IReadOnlyList<int> CloneRelicIds(IReadOnlyList<int> relicIds)
        {
            var copy = new int[relicIds.Count];
            for (var i = 0; i < relicIds.Count; i++)
            {
                copy[i] = relicIds[i];
            }

            return copy;
        }

        private static RelicCombatSnapshot SnapshotFromSeat(SeatSetup seat, CombatSituation situation)
        {
            return new RelicCombatSnapshot
            {
                RelicIds = seat.RelicIds,
                Shown = situation.Shown,
                Unshown = situation.Unshown,
                RubsUsedThisHand = situation.RubsUsedThisHand,
                PeekLeft = situation.PeekLeft,
                XRayLeft = situation.XRayLeft,
                ReplaceLeft = situation.ReplaceLeft,
                LuckySevenHits = situation.LuckySevenHits,
                // PVP 调用方填 SeatSetup 追踪字段；未填时遗物叠层为 0（不静默伪造）。
                HandTypeShowCounts = seat.HandTypeShowCounts ?? Array.Empty<int>(),
                RelicSelfDecayMag = seat.RelicSelfDecayMag,
                RelicShopRefreshCounts = seat.RelicShopRefreshCounts,
                RelicWinLoseMag = seat.RelicWinLoseMag,
                ConsumableUsesThisRun = seat.ConsumableUsesThisRun,
                CopiedRelicId = seat.CopiedRelicId
            };
        }

        private static int ResolveHeroId(IGameTables tables, int heroId)
        {
            if (tables.TryGetHero(heroId, out _))
            {
                return heroId;
            }

            var fallback = tables.GameConst.DefaultHeroId;
            if (fallback > 0 && tables.TryGetHero(fallback, out _))
            {
                return fallback;
            }

            return heroId;
        }

        private static bool TryResolveHero(IGameTables tables, int heroId, out HeroConfig hero)
        {
            if (tables.TryGetHero(heroId, out hero))
            {
                return true;
            }

            var fallback = tables.GameConst.DefaultHeroId;
            return fallback > 0 && tables.TryGetHero(fallback, out hero);
        }

        private static float SumHeroEntry(IGameTables tables, HeroConfig hero, MechanismType type)
        {
            var ids = hero.HeroEntryId;
            var sum = 0f;
            for (var i = 0; i < ids.Length; i++)
            {
                if (tables.TryGetHeroEntry(ids[i], out var entry) && entry.Type == type &&
                    entry.Value != null && entry.Value.Length > 0)
                {
                    sum += entry.Value[0];
                }
            }

            return sum;
        }

        private static float SumRelicEntry(IGameTables tables, IReadOnlyList<int> relicIds, MechanismType type)
        {
            var sum = 0f;
            for (var i = 0; i < relicIds.Count; i++)
            {
                if (!tables.TryGetRelic(relicIds[i], out var relic) || relic.MechanismId == null)
                {
                    continue;
                }

                for (var j = 0; j < relic.MechanismId.Length; j++)
                {
                    if (tables.TryGetRelicEntry(relic.MechanismId[j], out var entry)
                        && entry.Type == type
                        && entry.Value != null
                        && entry.Value.Length > 0)
                    {
                        sum += entry.Value[0];
                    }
                }
            }

            return sum;
        }

        private static float SumTalent(
            IGameTables tables,
            IReadOnlyList<CombatTalentCount> talents,
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
            IGameTables tables,
            IReadOnlyList<CombatTalentCount> talents,
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
            IGameTables tables,
            IReadOnlyList<CombatTalentCount> talents,
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
            IGameTables tables,
            IReadOnlyList<CombatTalentCount> talents,
            Action<int, TalentEntryConfig> action)
        {
            for (var i = 0; i < talents.Count; i++)
            {
                var owned = talents[i];
                if (owned.TalentId <= 0 || owned.Count <= 0)
                {
                    continue;
                }

                var maxLevel = tables.GetTalentMaxLevel(owned.TalentId);
                var level = TalentLevel(owned.Count, maxLevel, CopiesPerLevel);
                if (level <= 0 || !tables.TryGetTalentRow(owned.TalentId, level, out var row))
                {
                    continue;
                }

                if (!tables.TryGetTalentEntry(row.TalentEntry, out var entry))
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
}
