using System;
using App.Config;

namespace App.Game
{
    /// <summary>
    /// 按 RelicConfig / RelicEntryConfig 结算已购商品效果。锋芒禁用的商品整件跳过。
    /// </summary>
    public static class RelicMechanics
    {
        public static bool IsDisabled(RunState run, int relicId)
        {
            return run != null && relicId > 0 && run.DisabledRelicConfigId == relicId;
        }

        public static float SumMultiplierExtra(RunState run, HandScore score)
        {
            var extra = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                extra += MultiplierFromEntry(entry, score);
            });
            return extra;
        }

        /// <summary>已触发的倍率词条，如「粗制长剑+1, 青铜项链+1」。未触发的不写。</summary>
        public static string CollectMultiplierParts(RunState run, HandScore score)
        {
            string text = null;
            ForEachEntry(run, (relic, entry) =>
            {
                var add = MultiplierFromEntry(entry, score);
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

        private static float MultiplierFromEntry(RelicEntryConfig entry, HandScore score)
        {
            switch (entry.Type)
            {
                case MechanismType.CardMagnification:
                    return entry.Value;
                case MechanismType.SquarePlate:
                    return ContainsSuit(score, Suit.Diamond) ? entry.Value : 0f;
                case MechanismType.Spades:
                    return ContainsSuit(score, Suit.Spade) ? entry.Value : 0f;
                case MechanismType.RedHeart:
                    return ContainsSuit(score, Suit.Heart) ? entry.Value : 0f;
                case MechanismType.PlumBlossom:
                    return ContainsSuit(score, Suit.Club) ? entry.Value : 0f;
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
                default:
                    return 0f;
            }
        }

        private static bool ContainsSuit(HandScore score, Suit suit)
        {
            var cards = score.UsedCards;
            if (cards == null)
            {
                return false;
            }

            for (var i = 0; i < cards.Length; i++)
            {
                if (cards[i].Suit == suit)
                {
                    return true;
                }
            }

            return false;
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
}
