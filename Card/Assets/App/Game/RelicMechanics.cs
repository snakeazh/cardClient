using System;
using System.Collections.Generic;
using App.Config;

namespace App.Game
{
    /// <summary>本手结算上下文：未亮出牌、搓牌次数、幸运七掷骰等。不要只靠 HandScore。</summary>
    public readonly struct RelicCombatContext
    {
        public RelicCombatContext(
            Card[] unshown,
            int rubsUsedThisHand,
            bool rubbedThisHand,
            int peekLeft,
            int xrayLeft,
            int replaceLeft,
            int luckySevenHits,
            bool treatAllAsFace)
        {
            Unshown = unshown ?? Array.Empty<Card>();
            RubsUsedThisHand = Math.Max(0, rubsUsedThisHand);
            RubbedThisHand = rubbedThisHand;
            PeekLeft = Math.Max(0, peekLeft);
            XRayLeft = Math.Max(0, xrayLeft);
            ReplaceLeft = Math.Max(0, replaceLeft);
            LuckySevenHits = Math.Max(0, luckySevenHits);
            TreatAllAsFace = treatAllAsFace;
        }

        public Card[] Unshown { get; }
        public int RubsUsedThisHand { get; }
        public bool RubbedThisHand { get; }
        public int PeekLeft { get; }
        public int XRayLeft { get; }
        public int ReplaceLeft { get; }
        public int LuckySevenHits { get; }
        public bool TreatAllAsFace { get; }

        public static RelicCombatContext Empty { get; } = new RelicCombatContext(
            Array.Empty<Card>(), 0, false, 0, 0, 0, 0, false);
    }

    /// <summary>
    /// 按 RelicConfig / RelicEntryConfig 结算已购商品效果。锋芒禁用的商品整件跳过。
    /// </summary>
    public static class RelicMechanics
    {
        public static bool IsDisabled(RunState run, int relicId)
        {
            return run != null && relicId > 0 && run.DisabledRelicConfigId == relicId;
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

        public static float SumValue(RunState run, MechanismType type)
        {
            var sum = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    sum += entry.Value;
                }
            });
            return sum;
        }

        public static float SumValueForRelic(int relicId, MechanismType type)
        {
            var relic = RelicConfig.Get(relicId);
            var sum = 0f;
            ForEachRelicEntry(relic, entry =>
            {
                if (entry.Type == type)
                {
                    sum += entry.Value;
                }
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
            if (score.Type != HandType.HighCard || !IsTwoThreeFive(score.UsedCards))
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

        private static void ForEachRelicEntry(RelicConfig relic, Action<RelicEntryConfig> action)
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
            switch (entry.Type)
            {
                case MechanismType.CardMagnification:
                    return entry.Value;
                case MechanismType.SquarePlate:
                    return CountSuit(score, Suit.Diamond) * entry.Value;
                case MechanismType.Spades:
                    return CountSuit(score, Suit.Spade) * entry.Value;
                case MechanismType.RedHeart:
                    return CountSuit(score, Suit.Heart) * entry.Value;
                case MechanismType.PlumBlossom:
                    return CountSuit(score, Suit.Club) * entry.Value;
                case MechanismType.Couplet:
                    return score.Type == HandType.Pair ? entry.Value : 0f;
                case MechanismType.Flush:
                    return score.Type == HandType.Flush ? entry.Value : 0f;
                case MechanismType.Straight:
                    return score.Type == HandType.Straight ? entry.Value : 0f;
                case MechanismType.StraightFlush:
                    return score.Type == HandType.StraightFlush ? entry.Value : 0f;
                case MechanismType.Leopard:
                    return score.Type == HandType.ThreeOfAKind ? entry.Value : 0f;
                case MechanismType.EvenNumberCard:
                    return CountEven(score) * entry.Value;
                case MechanismType.OddNumberCard:
                    return CountOdd(score) * entry.Value;
                case MechanismType.HeadCard:
                    return CountFace(score, ctx) * entry.Value;
                case MechanismType.SpecialACard:
                    return CountRank(score, Rank.Ace) * entry.Value;
                case MechanismType.EveryRubbingNum:
                    return ctx.PeekLeft * entry.Value;
                case MechanismType.Camera:
                    return CountUnshown(ctx, black: true) * entry.Value;
                case MechanismType.Cupid:
                    return CountUnshown(ctx, black: false) * entry.Value;
                case MechanismType.EveryUseRubbingNum:
                    return ctx.RubsUsedThisHand * entry.Value;
                case MechanismType.NoSkill:
                    return ctx.PeekLeft == 0 && ctx.XRayLeft == 0 && ctx.ReplaceLeft == 0 ? entry.Value : 0f;
                case MechanismType.EveryRelic:
                    return (run?.RelicConfigIds != null ? run.RelicConfigIds.Count : 0) * entry.Value;
                case MechanismType.NoUseRubbingEveryRubbingNum:
                    return ctx.RubbedThisHand ? 0f : ctx.PeekLeft * entry.Value;
                case MechanismType.AccumulatedNumOfCardType:
                    return (run != null ? run.HandTypeShowCount(score.Type) : 0) * entry.Value;
                case MechanismType.RubbingCardRelic:
                    return run != null ? run.RubRelicMagForever : 0f;
                case MechanismType.ProOfUpCardType:
                    return run != null ? run.HandTypeMagBonus(score.Type) : 0f;
                case MechanismType.SpecialSevenCard:
                    return ctx.LuckySevenHits * entry.Value;
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
            switch (entry.Type)
            {
                case MechanismType.SquarePlateAttack:
                    return CountSuit(score, Suit.Diamond) * entry.Value;
                case MechanismType.SpadesAttack:
                    return CountSuit(score, Suit.Spade) * entry.Value;
                case MechanismType.RedHeartAttack:
                    return CountSuit(score, Suit.Heart) * entry.Value;
                case MechanismType.PlumBlossomAttack:
                    return CountSuit(score, Suit.Club) * entry.Value;
                case MechanismType.CoupletAttack:
                    return score.Type == HandType.Pair ? entry.Value : 0f;
                case MechanismType.StraightAttack:
                    return score.Type == HandType.Straight ? entry.Value : 0f;
                case MechanismType.FlushAttack:
                    return score.Type == HandType.Flush ? entry.Value : 0f;
                case MechanismType.StraightFlushAttack:
                    return score.Type == HandType.StraightFlush ? entry.Value : 0f;
                case MechanismType.LeopardAttack:
                    return score.Type == HandType.ThreeOfAKind ? entry.Value : 0f;
                case MechanismType.SpecialEightCard:
                    return CountRank(score, Rank.Eight) * entry.Value;
                case MechanismType.DoubleCardAttack:
                    return Math.Max(0, score.BaseChips) * entry.Value;
                case MechanismType.HeadCardAttack:
                    return CountFace(score, ctx) * entry.Value;
                case MechanismType.ACardAttack:
                    return CountRank(score, Rank.Ace) * entry.Value;
                case MechanismType.TheSwordOfVictory:
                    var maxChip = MaxUnshownChip(ctx);
                    var factor = entry.Value == 0f ? 1f : entry.Value;
                    return maxChip * factor;
                case MechanismType.ConsumeFundsGetAttack:
                    if (run == null || entry.Value <= 0f)
                    {
                        return 0f;
                    }

                    return (float)Math.Floor(run.GoldSpentThisRun / (double)entry.Value);
                case MechanismType.EveryCardAttackForever:
                    return SumRankAttackForever(run, score);
                case MechanismType.SpecialSevenCardAttack:
                    return ctx.LuckySevenHits * entry.Value;
                default:
                    return 0f;
            }
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

        private static int CountSuit(HandScore score, Suit suit)
        {
            var cards = score.UsedCards;
            if (cards == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                if (cards[i].Suit == suit)
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

        private static bool IsTwoThreeFive(Card[] cards)
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
