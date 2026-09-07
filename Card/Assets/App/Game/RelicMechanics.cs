using System;
using System.Collections.Generic;
using App.Config;

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
            var extra = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                extra += MultiplierFromEntry(entry, score, run, ctx);
            });
            if (!HasMechanism(run, MechanismType.RubbingCardRelic))
            {
                extra += run != null ? run.RubRelicMagForever : 0f;
            }

            if (!HasMechanism(run, MechanismType.ProOfUpCardType) && run != null)
            {
                extra += run.HandTypeMagBonus(score.Type);
            }

            if (!HasMechanism(run, MechanismType.NoKillMonsterGetMagnification) && run != null)
            {
                extra += run.PracticeMagForever;
            }

            return extra;
        }

        public static float SumAttackExtra(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            var extra = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                extra += AttackFromEntry(entry, score, run, ctx);
            });
            if (!HasMechanism(run, MechanismType.EveryCardAttackForever))
            {
                extra += SumRankAttackForever(run, score);
            }

            return extra;
        }

        /// <summary>按装备栏顺序收集本手触发的倍率/攻击加成，一件装备一条。</summary>
        public static void CollectRelicBonuses(
            RunState run,
            HandScore score,
            List<RelicBonusPart> dest,
            RelicCombatContext ctx = default)
        {
            dest?.Clear();
            if (dest == null || run?.RelicConfigIds == null)
            {
                return;
            }

            var ids = run.RelicConfigIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var relicId = ids[i];
                if (relicId <= 0 || IsDisabled(run, relicId))
                {
                    continue;
                }

                var relic = RelicConfig.Get(relicId);
                if (relic == null)
                {
                    continue;
                }

                var multiplierAdd = 0f;
                var attackAdd = 0f;
                ForEachRelicEntry(relic, entry =>
                {
                    multiplierAdd += MultiplierFromEntry(entry, score, run, ctx);
                    attackAdd += AttackFromEntry(entry, score, run, ctx);
                });
                if (multiplierAdd == 0f && attackAdd == 0f)
                {
                    continue;
                }

                dest.Add(new RelicBonusPart(relic.Id, multiplierAdd, attackAdd));
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

            ForEachEntry(run, (relic, entry) =>
            {
                if (relic == null || relic.Id <= 0)
                {
                    return;
                }

                if (MultiplierFromEntry(entry, score, run, ctx) == 0f &&
                    AttackFromEntry(entry, score, run, ctx) == 0f)
                {
                    return;
                }

                if (!dest.Contains(relic.Id))
                {
                    dest.Add(relic.Id);
                }
            });
        }

        /// <summary>已触发的倍率词条，如「粗制长剑+1, 青铜项链+1」。未触发的不写。</summary>
        public static string CollectMultiplierParts(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            string text = null;
            ForEachEntry(run, (relic, entry) =>
            {
                var add = MultiplierFromEntry(entry, score, run, ctx);
                if (add == 0f)
                {
                    return;
                }

                var name = relic != null && !string.IsNullOrEmpty(relic.Name) ? relic.Name : entry.Name;
                var piece = add == (int)add ? $"{name}+{(int)add}" : $"{name}+{add}";
                text = text == null ? piece : text + ", " + piece;
            });
            return text ?? string.Empty;
        }

        public static string CollectAttackParts(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            string text = null;
            ForEachEntry(run, (relic, entry) =>
            {
                var add = AttackFromEntry(entry, score, run, ctx);
                if (add == 0f)
                {
                    return;
                }

                var name = relic != null && !string.IsNullOrEmpty(relic.Name) ? relic.Name : entry.Name;
                var rounded = (int)Math.Round(add);
                var piece = $"{name}+{rounded}";
                text = text == null ? piece : text + ", " + piece;
            });
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

        /// <summary>只改比牌顺位，不改展示牌型和伤害倍率。须在牌型改写之后调用。</summary>
        public static HandScore ApplyCompareRankBonus(RunState run, HandScore score)
        {
            var bonus = (int)Math.Round(SumValue(run, MechanismType.ReduceLevel));
            if (bonus == 0)
            {
                return score;
            }

            return score.WithCompareLevelBonus(bonus);
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
                    type == MechanismType.EveryRoundEndingGetEvade)
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

        private static float MultiplierFromEntry(
            RelicEntryConfig entry,
            HandScore score,
            RunState run,
            RelicCombatContext ctx)
        {
            var value = ValueAt(entry);
            switch (entry.Type)
            {
                case MechanismType.CardMagnification:
                    return value;
                case MechanismType.SquarePlate:
                    return CountSuit(score, Suit.Diamond, run) * value;
                case MechanismType.Spades:
                    return CountSuit(score, Suit.Spade, run) * value;
                case MechanismType.RedHeart:
                    return CountSuit(score, Suit.Heart, run) * value;
                case MechanismType.PlumBlossom:
                    return CountSuit(score, Suit.Club, run) * value;
                case MechanismType.Couplet:
                    return score.Type == HandType.Pair ? value : 0f;
                case MechanismType.Flush:
                    return score.Type == HandType.Flush ? value : 0f;
                case MechanismType.Straight:
                    return score.Type == HandType.Straight ? value : 0f;
                case MechanismType.StraightFlush:
                    return score.Type == HandType.StraightFlush ? value : 0f;
                case MechanismType.Leopard:
                    return score.Type == HandType.ThreeOfAKind ? value : 0f;
                case MechanismType.EvenNumberCard:
                    return CountEven(score) * value;
                case MechanismType.OddNumberCard:
                    return CountOdd(score) * value;
                case MechanismType.HeadCard:
                    return CountFace(score, ctx) * value;
                case MechanismType.SpecialACard:
                    return CountRank(score, Rank.Ace) * value;
                case MechanismType.EveryRubbingNum:
                    return ctx.PeekLeft * value;
                case MechanismType.Camera:
                    return CountUnshown(ctx, black: true) * value;
                case MechanismType.Cupid:
                    return CountUnshown(ctx, black: false) * value;
                case MechanismType.EveryUseRubbingNum:
                    return ctx.RubsUsedThisHand * value;
                case MechanismType.NoSkill:
                    return ctx.PeekLeft == 0 && ctx.XRayLeft == 0 && ctx.ReplaceLeft == 0 ? value : 0f;
                case MechanismType.EveryRelic:
                    return (run?.RelicConfigIds != null ? run.RelicConfigIds.Count : 0) * value;
                case MechanismType.NoUseRubbingEveryRubbingNum:
                    return ctx.RubbedThisHand ? 0f : ctx.PeekLeft * value;
                case MechanismType.AccumulatedNumOfCardType:
                    return (run != null ? run.HandTypeShowCount(score.Type) : 0) * value;
                case MechanismType.RubbingCardRelic:
                    return run != null ? run.RubRelicMagForever : 0f;
                case MechanismType.ProOfUpCardType:
                    return run != null ? run.HandTypeMagBonus(score.Type) : 0f;
                case MechanismType.SpecialSevenCard:
                    return ctx.LuckySevenHits * value;
                case MechanismType.DefeatGetMagnification:
                    return StackedValue(run, MechanismType.DefeatGetMagnification, run != null ? run.DefeatMagStacks : 0);
                case MechanismType.NoKillMonsterGetMagnification:
                    return run != null ? run.PracticeMagForever : 0f;
                default:
                    return 0f;
            }
        }

        private static float AttackFromEntry(
            RelicEntryConfig entry,
            HandScore score,
            RunState run,
            RelicCombatContext ctx)
        {
            var value = ValueAt(entry);
            switch (entry.Type)
            {
                case MechanismType.SquarePlateAttack:
                    return CountSuit(score, Suit.Diamond, run) * value;
                case MechanismType.SpadesAttack:
                    return CountSuit(score, Suit.Spade, run) * value;
                case MechanismType.RedHeartAttack:
                    return CountSuit(score, Suit.Heart, run) * value;
                case MechanismType.PlumBlossomAttack:
                    return CountSuit(score, Suit.Club, run) * value;
                case MechanismType.CoupletAttack:
                    return score.Type == HandType.Pair ? value : 0f;
                case MechanismType.StraightAttack:
                    return score.Type == HandType.Straight ? value : 0f;
                case MechanismType.FlushAttack:
                    return score.Type == HandType.Flush ? value : 0f;
                case MechanismType.StraightFlushAttack:
                    return score.Type == HandType.StraightFlush ? value : 0f;
                case MechanismType.LeopardAttack:
                    return score.Type == HandType.ThreeOfAKind ? value : 0f;
                case MechanismType.SpecialEightCard:
                    return CountRank(score, Rank.Eight) * value;
                case MechanismType.DoubleCardAttack:
                    return Math.Max(0, score.BaseChips) * value;
                case MechanismType.HeadCardAttack:
                    return CountFace(score, ctx) * value;
                case MechanismType.ACardAttack:
                    return CountRank(score, Rank.Ace) * value;
                case MechanismType.TheSwordOfVictory:
                    var maxChip = MaxUnshownChip(ctx);
                    var factor = value == 0f ? 1f : value;
                    return maxChip * factor;
                case MechanismType.ConsumeFundsGetAttack:
                    if (run == null || value <= 0f)
                    {
                        return 0f;
                    }

                    return (float)Math.Floor(run.GoldSpentThisRun / (double)value);
                case MechanismType.EveryCardAttackForever:
                    return SumRankAttackForever(run, score);
                case MechanismType.SpecialSevenCardAttack:
                    return ctx.LuckySevenHits * value;
                case MechanismType.CardProvideAttack:
                    return SumCardProvideAttack(entry, run, ctx);
                default:
                    return 0f;
            }
        }

        private static float SumCardProvideAttack(RelicEntryConfig entry, RunState run, RelicCombatContext ctx)
        {
            if (entry?.Value == null || ctx.Shown == null)
            {
                return 0f;
            }

            var extra = 0f;
            for (var i = 0; i < entry.Value.Length; i++)
            {
                var index = (int)Math.Round(entry.Value[i]) - 1;
                if (index < 0 || index >= ctx.Shown.Length)
                {
                    continue;
                }

                var card = ctx.Shown[index];
                if (!card.IsValid)
                {
                    continue;
                }

                extra += BossMechanics.ChipValueOf(run, card);
            }

            return extra;
        }

        private static float SumRankAttackForever(RunState run, HandScore score)
        {
            if (run == null || score.UsedCards == null)
            {
                return 0f;
            }

            var extra = 0f;
            for (var i = 0; i < score.UsedCards.Length; i++)
            {
                extra += run.RankAttackBonus(score.UsedCards[i].Rank);
            }

            return extra;
        }

        private static int CountSuit(HandScore score, Suit suit, RunState run)
        {
            var cards = score.UsedCards;
            if (cards == null)
            {
                return 0;
            }

            var dual = false;
            var displaySuit = default(Suit);
            if (HasMechanism(run, MechanismType.SpecialFlush) &&
                (score.Type == HandType.Flush || score.Type == HandType.StraightFlush))
            {
                dual = HandEvaluator.TryColorFlushDisplaySuit(cards, out displaySuit);
            }

            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var real = cards[i].Suit;
                if (real == suit)
                {
                    count++;
                }
                else if (dual && displaySuit == suit)
                {
                    count++;
                }
            }

            return count;
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

        private static int CountEven(HandScore score)
        {
            var cards = score.UsedCards;
            if (cards == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var rank = cards[i].Rank;
                if (rank == Rank.Two || rank == Rank.Four || rank == Rank.Six || rank == Rank.Eight || rank == Rank.Ten)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOdd(HandScore score)
        {
            var cards = score.UsedCards;
            if (cards == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var rank = cards[i].Rank;
                if (rank == Rank.Ace || rank == Rank.Three || rank == Rank.Five || rank == Rank.Seven || rank == Rank.Nine)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountUnshown(RelicCombatContext ctx, bool black)
        {
            var cards = ctx.Unshown;
            if (cards == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                if (!card.IsValid)
                {
                    continue;
                }

                var isBlack = card.Suit == Suit.Spade || card.Suit == Suit.Club;
                if (black == isBlack)
                {
                    count++;
                }
            }

            return count;
        }

        private static int MaxUnshownChip(RelicCombatContext ctx)
        {
            var cards = ctx.Unshown;
            if (cards == null || cards.Length == 0)
            {
                return 0;
            }

            var max = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                if (!cards[i].IsValid)
                {
                    continue;
                }

                var chip = cards[i].ChipValue;
                if (chip > max)
                {
                    max = chip;
                }
            }

            return max;
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
