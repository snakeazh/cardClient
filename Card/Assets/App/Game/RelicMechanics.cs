using System;
using System.Collections.Generic;
using App.Config;
using CardShare.Contracts.Config;

namespace App.Game
{
    /// <summary>本手结算上下文：亮出/未亮出牌、搓牌次数、幸运七掷骰等。不要只靠 HandScore。</summary>
    public readonly struct RelicCombatContext
    {
        public RelicCombatContext(
            Card[] unshown,
            Card[] shown,
            int rubsUsedThisHand,
            bool rubbedThisHand,
            int peekLeft,
            int xrayLeft,
            int replaceLeft,
            int luckySevenHits,
            bool treatAllAsFace)
        {
            Unshown = unshown ?? Array.Empty<Card>();
            Shown = shown ?? Array.Empty<Card>();
            RubsUsedThisHand = Math.Max(0, rubsUsedThisHand);
            RubbedThisHand = rubbedThisHand;
            PeekLeft = Math.Max(0, peekLeft);
            XRayLeft = Math.Max(0, xrayLeft);
            ReplaceLeft = Math.Max(0, replaceLeft);
            LuckySevenHits = Math.Max(0, luckySevenHits);
            TreatAllAsFace = treatAllAsFace;
        }

        public Card[] Unshown { get; }
        /// <summary>亮出牌，手牌槽位从左到右，与 CopySelectedCards 一致。</summary>
        public Card[] Shown { get; }
        public int RubsUsedThisHand { get; }
        public bool RubbedThisHand { get; }
        public int PeekLeft { get; }
        public int XRayLeft { get; }
        public int ReplaceLeft { get; }
        public int LuckySevenHits { get; }
        public bool TreatAllAsFace { get; }

        public static RelicCombatContext Empty { get; } = new RelicCombatContext(
            Array.Empty<Card>(), Array.Empty<Card>(), 0, false, 0, 0, 0, 0, false);
    }

    /// <summary>
    /// 按 RelicConfig / RelicEntryConfig 结算已购商品效果。收藏禁用的商品整件跳过。
    /// </summary>
    public static class RelicMechanics
    {
        public static bool IsDisabled(RunState run, int relicId)
        {
            return run != null && relicId > 0 && run.DisabledRelicIds != null && run.DisabledRelicIds.Contains(relicId);
        }

        public static float SumMultiplierExtra(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            return EvaluateShared(run, score, ctx).MagExtra;
        }

        public static float SumAttackExtra(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            return EvaluateShared(run, score, ctx).AttackExtra;
        }

        /// <summary>按装备栏顺序收集本手触发的倍率/攻击加成，一件装备一条。</summary>
        public static void CollectRelicBonuses(
            RunState run,
            HandScore score,
            List<RelicBonusPart> dest,
            RelicCombatContext ctx = default)
        {
            dest?.Clear();
            if (dest == null)
            {
                return;
            }

            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                dest.Add(new RelicBonusPart(part.RelicId, part.MagAdd, part.AttackAdd));
            }
        }

        /// <summary>本手触发了倍率加成的遗物配置 Id，槽位可据此播 NumberShackLow。</summary>
        public static void CollectContributingRelicIds(
            RunState run,
            HandScore score,
            List<int> dest,
            RelicCombatContext ctx = default)
        {
            dest?.Clear();
            if (dest == null)
            {
                return;
            }

            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if ((part.MagAdd != 0f || part.AttackAdd != 0f) && !dest.Contains(part.RelicId))
                {
                    dest.Add(part.RelicId);
                }
            }
        }

        /// <summary>已触发的倍率词条，如「粗制长剑+1, 青铜项链+1」。未触发的不写。</summary>
        public static string CollectMultiplierParts(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            string text = null;
            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.MagAdd == 0f)
                {
                    continue;
                }

                var add = part.MagAdd;
                var name = !string.IsNullOrEmpty(part.Name) ? part.Name : part.RelicId.ToString();
                var piece = add == (int)add ? $"{name}+{(int)add}" : $"{name}+{add}";
                text = text == null ? piece : text + ", " + piece;
            });

            // 与 SumMultiplierExtra 一致：对应圣物已不在栏时，永久累计仍计入遗物加成。
            if (!HasMechanism(run, MechanismType.RubbingCardRelic) &&
                run != null &&
                run.RubRelicMagForever != 0f)
            {
                var add = run.RubRelicMagForever;
                var piece = add == (int)add ? $"老搓家+{(int)add}" : $"老搓家+{add}";
                text = text == null ? piece : text + ", " + piece;
            }

            if (!HasMechanism(run, MechanismType.ProOfUpCardType) &&
                run != null)
            {
                var add = run.HandTypeMagBonus(score.Type);
                if (add != 0f)
                {
                    var piece = add == (int)add ? $"牌型永久+{(int)add}" : $"牌型永久+{add}";
                    text = text == null ? piece : text + ", " + piece;
                }
            }

            if (!HasMechanism(run, MechanismType.NoKillMonsterGetMagnification) &&
                run != null &&
                run.PracticeMagForever != 0f)
            {
                var add = run.PracticeMagForever;
                var piece = add == (int)add ? $"练习卷+{(int)add}" : $"练习卷+{add}";
                text = text == null ? piece : text + ", " + piece;
            }

            return text ?? string.Empty;
        }

        public static string CollectAttackParts(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            string text = null;
            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.AttackAdd == 0f)
                {
                    continue;
                }

                var name = !string.IsNullOrEmpty(part.Name) ? part.Name : part.RelicId.ToString();
                var piece = $"{name}+{(int)Math.Round(part.AttackAdd)}";
                text = text == null ? piece : text + ", " + piece;
            }

            return text ?? string.Empty;
        }

        public static float SumValue(RunState run, MechanismType type, int index = 0)
        {
            var sum = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    sum += ValueAt(entry, index);
                }
            });
            return sum;
        }

        public static float SumValueForRelic(int relicId, MechanismType type, int index = 0)
        {
            var relic = RelicConfig.Get(relicId);
            var sum = 0f;
            ForEachRelicEntry(relic, entry =>
            {
                if (entry.Type == type)
                {
                    sum += ValueAt(entry, index);
                }
            });
            return sum;
        }

        /// <summary>词条 <c>Value</c> 已是数组；缺项或越界返回 0。</summary>
        public static float ValueAt(RelicEntryConfig entry, int index = 0)
        {
            if (entry?.Value == null || index < 0 || index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
        }

        public static bool Roll(RunState run, MechanismType type, Random rng)
        {
            var chance = SumValue(run, type);
            if (chance <= 0f || rng == null)
            {
                return false;
            }

            return rng.NextDouble() < chance;
        }

        public static HandScore WithType(HandScore score, HandType type)
        {
            if (score.Type == type)
            {
                return score;
            }

            return new HandScore(
                type,
                score.BaseChips,
                HandEvaluator.TypeMultiplier(type),
                score.Keys,
                score.UsedCards,
                HandEvaluator.TypeName(type),
                score.BeatsAll);
        }

        public static HandType ClampType(HandType type)
        {
            return HandEvaluator.TypeByLevel(HandEvaluator.TypeLevel(type));
        }

        public static HandType ShiftType(HandType type, int delta)
        {
            return HandEvaluator.TypeByLevel(HandEvaluator.TypeLevel(type) + delta);
        }

        public static HandScore ApplyPlayerTypeRewrite(RunState run, HandScore score)
        {
            var steps = (int)Math.Round(
                SumValue(run, MechanismType.CardUpGrade) +
                SumValue(run, MechanismType.UpLevel));
            if (steps == 0)
            {
                return score;
            }

            return WithType(score, ShiftType(score.Type, steps));
        }

        /// <summary>
        /// 降低：敌人牌型按 Level +Value 改写（比牌、提示、出伤倍率一起变）。
        /// 散牌已是最低，再降无效。须在好运来等牌型改写之后调用。
        /// </summary>
        public static HandScore ApplyCompareRankBonus(RunState run, HandScore score)
        {
            var steps = (int)Math.Round(SumValue(run, MechanismType.ReduceLevel));
            if (steps == 0)
            {
                return score;
            }

            var type = ShiftType(score.Type, steps);
            if (type == score.Type)
            {
                return score;
            }

            return new HandScore(
                type,
                score.BaseChips,
                HandEvaluator.TypeMultiplier(type),
                Array.Empty<int>(),
                score.UsedCards,
                HandEvaluator.TypeName(type),
                score.BeatsAll);
        }

        public static HandScore ApplyEnemyTypeRewrite(RunState run, HandScore score, int downgradeSteps)
        {
            var type = score.Type;
            if (downgradeSteps > 0)
            {
                type = ShiftType(type, -downgradeSteps);
            }

            if (HasMechanism(run, MechanismType.LuckyFlush) &&
                HandEvaluator.TypeLevel(type) > HandEvaluator.TypeLevel(HandType.Flush))
            {
                type = HandType.Flush;
            }

            if (HasMechanism(run, MechanismType.LuckyStraight) &&
                HandEvaluator.TypeLevel(type) > HandEvaluator.TypeLevel(HandType.Straight))
            {
                type = HandType.Straight;
            }

            if (HasMechanism(run, MechanismType.LuckyCouplet) &&
                HandEvaluator.TypeLevel(type) > HandEvaluator.TypeLevel(HandType.Pair))
            {
                type = HandType.Pair;
            }

            return WithType(score, type);
        }

        /// <summary>玩家相对敌人的牌型差：正数表示玩家更大。差值为配置 Level。</summary>
        public static int TypeGap(HandType playerType, HandType enemyType)
        {
            return HandEvaluator.TypeLevel(playerType) - HandEvaluator.TypeLevel(enemyType);
        }

        public static float GapDamagePercent(RunState run, HandType playerType, HandType enemyType, bool outgoing)
        {
            var unit = SumValue(run, MechanismType.GapDamage);
            var per = SumValue(run, MechanismType.GapDamage, 1);
            if (unit <= 0f || per == 0f)
            {
                return 0f;
            }

            var ranks = TypeGap(playerType, enemyType) / unit;
            return (outgoing ? ranks : -ranks) * per;
        }

        /// <summary>贪欲之冠：floor(金币 / Value[0]) × Value[1]，层数上限 Value[2]。</summary>
        public static float GoldDamageScalePercent(RunState run)
        {
            var percent = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type != MechanismType.GoldDamageScale || run == null)
                {
                    return;
                }

                var unit = ValueAt(entry);
                var per = ValueAt(entry, 1);
                if (unit <= 0f || per == 0f)
                {
                    return;
                }

                var stacks = (int)Math.Floor(run.Gold / (double)unit);
                var cap = entry.Value != null && entry.Value.Length > 2
                    ? (int)Math.Round(ValueAt(entry, 2))
                    : int.MaxValue;
                if (cap > 0)
                {
                    stacks = Math.Min(stacks, cap);
                }

                percent += stacks * per;
            });
            return percent;
        }

        /// <summary>「自身攻击力」：次数 Value[0]，倍率缺省为 1。</summary>
        public static int AttackPowerDamage(RelicEntryConfig entry, int attack)
        {
            var hits = Math.Max(0, (int)Math.Round(ValueAt(entry, 0)));
            var factor = entry?.Value != null && entry.Value.Length > 1 ? ValueAt(entry, 1) : 1f;
            if (hits <= 0 || attack <= 0)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(attack * factor * hits));
        }

        public static float StackedValue(RunState run, MechanismType type, int stacks)
        {
            if (run == null || stacks <= 0)
            {
                return 0f;
            }

            var sum = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type != type)
                {
                    return;
                }

                var unit = ValueAt(entry);
                if (unit <= 0f)
                {
                    unit = 1f;
                }

                var per = entry.Value != null && entry.Value.Length > 1 ? ValueAt(entry, 1) : unit;
                var cap = entry.Value != null && entry.Value.Length > 1
                    ? (int)Math.Round(ValueAt(entry, 1))
                    : int.MaxValue;
                if (type == MechanismType.EveryRoundEndingGetCritical ||
                    type == MechanismType.EveryRoundEndingGetEvade ||
                    type == MechanismType.LossRampDamage)
                {
                    var used = Math.Min(stacks, cap > 0 ? cap : stacks);
                    sum += used * unit;
                    return;
                }

                sum += (float)Math.Floor(stacks / (double)unit) * per;
            });
            return sum;
        }

        public static bool HasMechanism(RunState run, MechanismType type)
        {
            var found = false;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    found = true;
                }
            });
            return found;
        }

        public static int SellPrice(RunState run, int relicId)
        {
            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                return 0;
            }

            var bonus = run != null ? run.RelicSellPriceBonus(relicId) : 0;
            return Math.Max(0, relic.SellingPrice + bonus);
        }

        public static int GoldFloor(RunState run)
        {
            if (!HasMechanism(run, MechanismType.Liability))
            {
                return 0;
            }

            return -(int)Math.Round(SumValue(run, MechanismType.Liability));
        }

        public static bool CanAfford(RunState run, int cost)
        {
            var gold = run != null ? run.Gold : 0;
            return gold - Math.Max(0, cost) >= GoldFloor(run);
        }

        public static RelicCombatContext BuildCombatContext(
            RunState run,
            HandScore score,
            Card[] unshown,
            Card[] shown,
            int rubsUsedThisHand,
            bool rubbedThisHand,
            Random rng)
        {
            var treatAllAsFace = HasMechanism(run, MechanismType.AllCardIsHeadCard);
            var luckyHits = 0;
            var chance = SumValue(run, MechanismType.SpecialSevenCardPro);
            if (chance > 0f && score.UsedCards != null && rng != null)
            {
                for (var i = 0; i < score.UsedCards.Length; i++)
                {
                    if (score.UsedCards[i].Rank != Rank.Seven)
                    {
                        continue;
                    }

                    if (rng.NextDouble() < chance)
                    {
                        luckyHits++;
                    }
                }
            }

            return new RelicCombatContext(
                unshown,
                shown,
                rubsUsedThisHand,
                rubbedThisHand,
                run != null ? Math.Max(0, run.PeekGoodCharges) : 0,
                run != null ? Math.Max(0, run.ChaKanGoodCharges) : 0,
                run != null ? Math.Max(0, run.TiHuanGoodCharges) : 0,
                luckyHits,
                treatAllAsFace);
        }

        /// <summary>散牌 2+3+5 视为豹子，且比牌通杀。</summary>
        public static HandScore ApplyTwoThreeFive(HandScore score)
        {
            if (score.Type != HandType.HighCard || !IsNaturalTwoThreeFive(score.UsedCards))
            {
                return score;
            }

            return new HandScore(
                HandType.ThreeOfAKind,
                score.BaseChips,
                HandEvaluator.TypeMultiplier(HandType.ThreeOfAKind),
                score.Keys,
                score.UsedCards,
                "豹子 235",
                true);
        }

        public static void ForEachEntry(RunState run, Action<RelicConfig, RelicEntryConfig> action)
        {
            if (run == null || action == null)
            {
                return;
            }

            var ids = run.RelicConfigIds;
            if (ids == null)
            {
                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                var relicId = ids[i];
                if (IsDisabled(run, relicId))
                {
                    continue;
                }

                var relic = RelicConfig.Get(relicId);
                ForEachRelicEntry(relic, entry => action(relic, entry));
            }
        }

        public static RelicUseType UseTypeOf(RelicConfig relic)
        {
            if (relic == null)
            {
                return RelicUseType.Passive;
            }

            switch (relic.UseType)
            {
                case (int)RelicUseType.InstantConsume:
                    return RelicUseType.InstantConsume;
                case (int)RelicUseType.PermanentConsume:
                    return RelicUseType.PermanentConsume;
                default:
                    return RelicUseType.Passive;
            }
        }

        public static bool IsConsumable(RelicConfig relic)
        {
            var type = UseTypeOf(relic);
            return type == RelicUseType.InstantConsume || type == RelicUseType.PermanentConsume;
        }

        public static bool RequiresComparePhase(RelicConfig relic)
        {
            var required = false;
            ForEachRelicEntry(relic, entry =>
            {
                switch (entry.Type)
                {
                    case MechanismType.UseDamageMul:
                    case MechanismType.UseDamageFixed:
                    case MechanismType.UseRoundNullify:
                        required = true;
                        break;
                }
            });
            return required;
        }

        public static void ForEachRelicEntry(RelicConfig relic, Action<RelicEntryConfig> action)
        {
            if (relic?.MechanismId == null || action == null)
            {
                return;
            }

            for (var i = 0; i < relic.MechanismId.Length; i++)
            {
                var entry = RelicEntryConfig.Get(relic.MechanismId[i]);
                if (entry != null)
                {
                    action(entry);
                }
            }
        }

        private static CardShare.Battle.RelicCombatResult EvaluateShared(
            RunState run,
            HandScore score,
            RelicCombatContext ctx)
        {
            return CardShare.Battle.RelicCombat.Evaluate(
                UnityGameConfigLoader.Current,
                ToSnapshot(run, score, ctx),
                SharedBattleBridge.ToShared(score));
        }

        private static CardShare.Battle.RelicCombatSnapshot ToSnapshot(
            RunState run,
            HandScore score,
            RelicCombatContext ctx)
        {
            var ids = run != null && run.RelicConfigIds != null
                ? (IReadOnlyList<int>)run.RelicConfigIds
                : Array.Empty<int>();
            var disabled = run != null && run.DisabledRelicIds != null && run.DisabledRelicIds.Count > 0
                ? new List<int>(run.DisabledRelicIds)
                : (IReadOnlyList<int>)Array.Empty<int>();
            var rankBonus = new int[14];
            if (run != null)
            {
                for (var i = 0; i < rankBonus.Length; i++)
                {
                    rankBonus[i] = run.RankAttackBonus((Rank)i);
                }
            }

            return new CardShare.Battle.RelicCombatSnapshot
            {
                RelicIds = ids,
                DisabledRelicIds = disabled,
                Shown = SharedBattleBridge.ToShared(ctx.Shown),
                Unshown = SharedBattleBridge.ToShared(ctx.Unshown),
                RubsUsedThisHand = ctx.RubsUsedThisHand,
                PeekLeft = ctx.PeekLeft,
                XRayLeft = ctx.XRayLeft,
                ReplaceLeft = ctx.ReplaceLeft,
                LuckySevenHits = ctx.LuckySevenHits,
                HandTypeShowCount = run != null ? run.HandTypeShowCount(score.Type) : 0,
                RubRelicMagForever = run != null ? run.RubRelicMagForever : 0f,
                HandTypeMagBonus = run != null ? run.HandTypeMagBonus(score.Type) : 0f,
                PracticeMagForever = run != null ? run.PracticeMagForever : 0f,
                DefeatMagStacks = run != null ? run.DefeatMagStacks : 0,
                GoldSpentThisRun = run != null ? run.GoldSpentThisRun : 0,
                RankAttackBonus = rankBonus
            };
        }

        /// <summary>亮出用牌恰好是 2+3+5，不依赖遗物改牌型。</summary>
        public static bool IsNaturalTwoThreeFive(Card[] cards)
        {
            if (cards == null || cards.Length != GameBalance.OpenHandSize)
            {
                return false;
            }

            var two = false;
            var three = false;
            var five = false;
            for (var i = 0; i < cards.Length; i++)
            {
                switch (cards[i].Rank)
                {
                    case Rank.Two:
                        two = true;
                        break;
                    case Rank.Three:
                        three = true;
                        break;
                    case Rank.Five:
                        five = true;
                        break;
                    default:
                        return false;
                }
            }

            return two && three && five;
        }
    }

    public readonly struct RelicBonusPart
    {
        public RelicBonusPart(int relicId, float multiplierAdd, float attackAdd)
        {
            RelicId = relicId;
            MultiplierAdd = multiplierAdd;
            AttackAdd = attackAdd;
        }

        public int RelicId { get; }
        public float MultiplierAdd { get; }
        public float AttackAdd { get; }
    }
}
