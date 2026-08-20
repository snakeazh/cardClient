using System;
using System.Collections.Generic;

namespace App.Game
{
    /// <summary>花色。ResourceId 用 1~4 对应红桃/方片/梅花/黑桃。</summary>
    public enum Suit
    {
        Heart = 1,
        Diamond = 2,
        Club = 3,
        Spade = 4
    }

    public enum Rank
    {
        Ace = 1,
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13
    }

    /// <summary>炸金花牌型，数值越大越强。豹子 > 顺金 > 金花 > 顺子 > 对子 > 散牌。</summary>
    public enum HandType
    {
        HighCard = 0,
        Pair = 1,
        Straight = 2,
        Flush = 3,
        StraightFlush = 4,
        ThreeOfAKind = 5
    }

    public readonly struct Card : IEquatable<Card>
    {
        public Card(Suit suit, Rank rank)
        {
            Suit = suit;
            Rank = rank;
        }

        public Suit Suit { get; }
        public Rank Rank { get; }

        public bool IsFace => Rank == Rank.Jack || Rank == Rank.Queen || Rank == Rank.King;

        /// <summary>
        /// 图集精灵名：1红心 2方片 3草花 4黑桃，个位点数 01=A … 13=K。
        /// 例：101=红心A，113=红心K，201=方片A，301=草花A，401=黑桃A。
        /// </summary>
        public int ResourceId => (int)Suit * 100 + (int)Rank;

        public int ChipValue => Rank == Rank.Ace ? 14 : (int)Rank;

        public string DisplayName => $"{SuitName(Suit)}{RankName(Rank)}";

        public bool Equals(Card other) => Suit == other.Suit && Rank == other.Rank;

        public override bool Equals(object obj) => obj is Card other && Equals(other);

        public override int GetHashCode() => ((int)Suit << 8) | (int)Rank;

        public override string ToString() => DisplayName;

        public static string SuitName(Suit suit)
        {
            switch (suit)
            {
                case Suit.Heart: return "红桃";
                case Suit.Diamond: return "方片";
                case Suit.Club: return "梅花";
                default: return "黑桃";
            }
        }

        public static string RankName(Rank rank)
        {
            switch (rank)
            {
                case Rank.Ace: return "A";
                case Rank.Jack: return "J";
                case Rank.Queen: return "Q";
                case Rank.King: return "K";
                default: return ((int)rank).ToString();
            }
        }
    }

    /// <summary>52 张标准扑克。抽空会自动重置。</summary>
    public sealed class Deck
    {
        public const int Size = 52;

        private readonly List<Card> _cards = new List<Card>(Size);
        private readonly Random _rng;

        public Deck(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Reset();
        }

        public int Remaining => _cards.Count;

        public void Reset()
        {
            _cards.Clear();
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                {
                    _cards.Add(new Card(suit, rank));
                }
            }

            Shuffle();
        }

        public void Shuffle()
        {
            for (var i = _cards.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                var tmp = _cards[i];
                _cards[i] = _cards[j];
                _cards[j] = tmp;
            }
        }

        public void Remove(Card card)
        {
            for (var i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Equals(card))
                {
                    _cards.RemoveAt(i);
                    return;
                }
            }
        }

        public Card Draw()
        {
            if (_cards.Count == 0)
            {
                Reset();
            }

            var last = _cards.Count - 1;
            var card = _cards[last];
            _cards.RemoveAt(last);
            return card;
        }

        public Card DrawMatching(Func<Card, bool> predicate)
        {
            if (predicate == null)
            {
                return Draw();
            }

            var matches = new List<int>();
            for (var i = 0; i < _cards.Count; i++)
            {
                if (predicate(_cards[i]))
                {
                    matches.Add(i);
                }
            }

            if (matches.Count == 0)
            {
                return Draw();
            }

            var pick = matches[_rng.Next(matches.Count)];
            var card = _cards[pick];
            _cards.RemoveAt(pick);
            return card;
        }
    }

    /// <summary>
    /// 牌型评估结果。Type 决定大小，Keys 拆同分，BaseChips×Multiplier 用于伤害。
    /// </summary>
    public readonly struct HandScore : IComparable<HandScore>
    {
        public HandScore(HandType type, int baseChips, float multiplier, int[] keys, Card[] usedCards, string label)
        {
            Type = type;
            BaseChips = baseChips;
            Multiplier = multiplier;
            Keys = keys ?? Array.Empty<int>();
            UsedCards = usedCards ?? Array.Empty<Card>();
            Label = label ?? string.Empty;
        }

        public HandType Type { get; }
        public int BaseChips { get; }
        public float Multiplier { get; }
        public int[] Keys { get; }
        public Card[] UsedCards { get; }
        public string Label { get; }

        public int CompareTo(HandScore other)
        {
            var type = Type.CompareTo(other.Type);
            if (type != 0)
            {
                return type;
            }

            var n = Math.Max(Keys.Length, other.Keys.Length);
            for (var i = 0; i < n; i++)
            {
                var a = i < Keys.Length ? Keys[i] : 0;
                var b = i < other.Keys.Length ? other.Keys[i] : 0;
                if (a != b)
                {
                    return a.CompareTo(b);
                }
            }

            return 0;
        }
    }

    /// <summary>
    /// 三张牌炸金花评估。BOSS「禁用花色/人头」通过 bannedSuit / banFaces 过滤后再比牌。
    /// </summary>
    public static class HandEvaluator
    {
        public static string TypeName(HandType type)
        {
            switch (type)
            {
                case HandType.ThreeOfAKind: return "豹子";
                case HandType.StraightFlush: return "顺金";
                case HandType.Flush: return "金花";
                case HandType.Straight: return "顺子";
                case HandType.Pair: return "对子";
                default: return "散牌";
            }
        }

        public static float TypeMultiplier(HandType type)
        {
            switch (type)
            {
                case HandType.ThreeOfAKind: return 6f;
                case HandType.StraightFlush: return 4f;
                case HandType.Flush: return 2.5f;
                case HandType.Straight: return 2f;
                case HandType.Pair: return 1.5f;
                default: return 1f;
            }
        }

        public static HandScore Evaluate(IReadOnlyList<Card> cards, Suit? bannedSuit = null, bool banFaces = false)
        {
            var filtered = Filter(cards, bannedSuit, banFaces);
            if (filtered.Count == 0)
            {
                return new HandScore(HandType.HighCard, 0, 1f, new[] { 0 }, Array.Empty<Card>(), "无有效牌");
            }

            filtered.Sort((a, b) => RankKey(b.Rank).CompareTo(RankKey(a.Rank)));

            if (filtered.Count == 1)
            {
                var only = filtered[0];
                return new HandScore(
                    HandType.HighCard,
                    only.ChipValue,
                    1f,
                    new[] { RankKey(only.Rank) },
                    filtered.ToArray(),
                    $"散牌 {only.DisplayName}");
            }

            if (filtered.Count == 2)
            {
                if (filtered[0].Rank == filtered[1].Rank)
                {
                    var chips = filtered[0].ChipValue + filtered[1].ChipValue;
                    return new HandScore(
                        HandType.Pair,
                        chips,
                        1.5f,
                        new[] { RankKey(filtered[0].Rank) },
                        filtered.ToArray(),
                        $"对子 {Card.RankName(filtered[0].Rank)}");
                }

                return HighCard(filtered);
            }

            var a = filtered[0];
            var b = filtered[1];
            var c = filtered[2];
            var flush = a.Suit == b.Suit && b.Suit == c.Suit;
            var three = a.Rank == b.Rank && b.Rank == c.Rank;
            var pair = a.Rank == b.Rank || b.Rank == c.Rank || a.Rank == c.Rank;
            var straight = IsStraight(a.Rank, b.Rank, c.Rank, out var straightHigh);

            if (three)
            {
                var chips = a.ChipValue + b.ChipValue + c.ChipValue;
                return new HandScore(
                    HandType.ThreeOfAKind,
                    chips,
                    6f,
                    new[] { RankKey(a.Rank) },
                    filtered.ToArray(),
                    $"豹子 {Card.RankName(a.Rank)}");
            }

            if (flush && straight)
            {
                var chips = a.ChipValue + b.ChipValue + c.ChipValue;
                return new HandScore(
                    HandType.StraightFlush,
                    chips,
                    4f,
                    new[] { straightHigh },
                    filtered.ToArray(),
                    $"顺金 {straightHigh}");
            }

            if (flush)
            {
                var chips = a.ChipValue + b.ChipValue + c.ChipValue;
                return new HandScore(
                    HandType.Flush,
                    chips,
                    2.5f,
                    new[] { RankKey(a.Rank), RankKey(b.Rank), RankKey(c.Rank) },
                    filtered.ToArray(),
                    $"金花 {a.DisplayName}");
            }

            if (straight)
            {
                var chips = a.ChipValue + b.ChipValue + c.ChipValue;
                return new HandScore(
                    HandType.Straight,
                    chips,
                    2f,
                    new[] { straightHigh },
                    filtered.ToArray(),
                    $"顺子 {straightHigh}");
            }

            if (pair)
            {
                Rank pairRank;
                Rank kicker;
                if (a.Rank == b.Rank)
                {
                    pairRank = a.Rank;
                    kicker = c.Rank;
                }
                else if (b.Rank == c.Rank)
                {
                    pairRank = b.Rank;
                    kicker = a.Rank;
                }
                else
                {
                    pairRank = a.Rank;
                    kicker = b.Rank;
                }

                var chips = ChipOf(pairRank) * 2;
                return new HandScore(
                    HandType.Pair,
                    chips,
                    1.5f,
                    new[] { RankKey(pairRank), RankKey(kicker) },
                    filtered.ToArray(),
                    $"对子 {Card.RankName(pairRank)}");
            }

            return HighCard(filtered);
        }

        /// <summary>从已发手牌中选出炸金花最大的 3 张，写入 <paramref name="selected"/>。</summary>
        public static HandScore SelectBestOpen(Card[] hand, bool[] selected, int dealt, Suit? bannedSuit = null, bool banFaces = false)
        {
            if (selected != null)
            {
                for (var i = 0; i < selected.Length; i++)
                {
                    selected[i] = false;
                }
            }

            if (hand == null)
            {
                return Evaluate(Array.Empty<Card>(), bannedSuit, banFaces);
            }

            dealt = Math.Min(dealt, hand.Length);
            if (dealt < 3)
            {
                return Evaluate(Array.Empty<Card>(), bannedSuit, banFaces);
            }

            var trio = new Card[3];
            HandScore best = default;
            var bestI = 0;
            var bestJ = 1;
            var bestK = 2;
            var any = false;
            for (var i = 0; i < dealt - 2; i++)
            {
                for (var j = i + 1; j < dealt - 1; j++)
                {
                    for (var k = j + 1; k < dealt; k++)
                    {
                        trio[0] = hand[i];
                        trio[1] = hand[j];
                        trio[2] = hand[k];
                        var score = Evaluate(trio, bannedSuit, banFaces);
                        if (!any || score.CompareTo(best) > 0)
                        {
                            best = score;
                            bestI = i;
                            bestJ = j;
                            bestK = k;
                            any = true;
                        }
                    }
                }
            }

            if (selected != null && any)
            {
                if (bestI < selected.Length)
                {
                    selected[bestI] = true;
                }

                if (bestJ < selected.Length)
                {
                    selected[bestJ] = true;
                }

                if (bestK < selected.Length)
                {
                    selected[bestK] = true;
                }
            }

            return any ? best : Evaluate(Array.Empty<Card>(), bannedSuit, banFaces);
        }

        public static Card[] CopySelectedCards(Card[] hand, bool[] selected)
        {
            if (hand == null || selected == null)
            {
                return Array.Empty<Card>();
            }

            var picked = new List<Card>(GameBalance.OpenHandSize);
            var limit = Math.Min(hand.Length, selected.Length);
            for (var i = 0; i < limit; i++)
            {
                if (selected[i])
                {
                    picked.Add(hand[i]);
                }
            }

            return picked.ToArray();
        }

        public static Card[] CopyBestOpenCards(Card[] hand, int dealt, Suit? bannedSuit = null, bool banFaces = false)
        {
            var flags = new bool[GameBalance.MaxCardsPerSeat];
            SelectBestOpen(hand, flags, dealt, bannedSuit, banFaces);
            return CopySelectedCards(hand, flags);
        }

        /// <summary>伤害 ≈ 牌面筹码 × 底池 × 牌型倍率 × 遗物倍率。</summary>
        public static int ComputeDamage(HandScore score, int pot, float relicMultiplier)
        {
            var value = score.BaseChips * pot * score.Multiplier * relicMultiplier;
            return Math.Max(1, (int)Math.Round(value));
        }

        /// <summary>伤害 = 攻击力 × 牌型倍率 × 遗物倍率。</summary>
        public static int ComputeAttackDamage(int attack, float handMagnification, float relicMultiplier)
        {
            var value = Math.Max(0, attack) * Math.Max(0f, handMagnification) * Math.Max(0f, relicMultiplier);
            return Math.Max(1, (int)Math.Round(value));
        }

        private static HandScore HighCard(List<Card> filtered)
        {
            var keys = new int[filtered.Count];
            var maxChip = 0;
            for (var i = 0; i < filtered.Count; i++)
            {
                keys[i] = RankKey(filtered[i].Rank);
                if (filtered[i].ChipValue > maxChip)
                {
                    maxChip = filtered[i].ChipValue;
                }
            }

            return new HandScore(
                HandType.HighCard,
                maxChip,
                1f,
                keys,
                filtered.ToArray(),
                $"散牌 {filtered[0].DisplayName}");
        }

        private static List<Card> Filter(IReadOnlyList<Card> cards, Suit? bannedSuit, bool banFaces)
        {
            var list = new List<Card>(3);
            if (cards == null)
            {
                return list;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (bannedSuit.HasValue && card.Suit == bannedSuit.Value)
                {
                    continue;
                }

                if (banFaces && card.IsFace)
                {
                    continue;
                }

                list.Add(card);
            }

            return list;
        }

        private static bool IsStraight(Rank a, Rank b, Rank c, out int high)
        {
            var keys = new[] { RankKey(a), RankKey(b), RankKey(c) };
            Array.Sort(keys);
            if (keys[0] + 1 == keys[1] && keys[1] + 1 == keys[2])
            {
                high = keys[2];
                return true;
            }

            // A-2-3 wheel. Ace is stored as 14.
            if (keys[0] == 2 && keys[1] == 3 && keys[2] == 14)
            {
                high = 3;
                return true;
            }

            high = 0;
            return false;
        }

        private static int RankKey(Rank rank) => rank == Rank.Ace ? 14 : (int)rank;

        private static int ChipOf(Rank rank) => rank == Rank.Ace ? 14 : (int)rank;
    }
}
