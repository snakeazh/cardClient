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

        public bool IsValid => Suit >= Suit.Heart && Suit <= Suit.Spade && Rank >= Rank.Ace && Rank <= Rank.King;

        public bool IsFace => Rank == Rank.Jack || Rank == Rank.Queen || Rank == Rank.King;

        /// <summary>
        /// 图集精灵名：1红心 2方片 3草花 4黑桃，个位点数 01=A … 13=K。
        /// 例：101=红心A，113=红心K，201=方片A，301=草花A，401=黑桃A。
        /// </summary>
        public int ResourceId => (int)Suit * 100 + (int)Rank;

        /// <summary>伤害点数：A=11，J/Q/K=10，2~10 为面值。比牌大小仍用 RankKey（A=14）。</summary>
        public int ChipValue
        {
            get
            {
                if (Rank == Rank.Ace)
                {
                    return 11;
                }

                if (IsFace)
                {
                    return 10;
                }

                return (int)Rank;
            }
        }

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

    /// <summary>52 张标准扑克。桌上已发的牌不得再抽，抽空也不会把已发牌塞回牌堆。</summary>
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

        public bool Contains(Card card)
        {
            for (var i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Equals(card))
                {
                    return true;
                }
            }

            return false;
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

        public void Return(Card card)
        {
            if (!card.IsValid || Contains(card))
            {
                return;
            }

            _cards.Add(card);
        }

        /// <summary>牌堆重建为 52 张减去桌上已发牌，避免抽到重复牌。</summary>
        public void RestoreUnused(IEnumerable<Card> dealt)
        {
            var used = new HashSet<Card>();
            if (dealt != null)
            {
                foreach (var card in dealt)
                {
                    if (card.IsValid)
                    {
                        used.Add(card);
                    }
                }
            }

            _cards.Clear();
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                {
                    var card = new Card(suit, rank);
                    if (!used.Contains(card))
                    {
                        _cards.Add(card);
                    }
                }
            }

            Shuffle();
        }

        public Card Draw()
        {
            return TryDraw(out var card) ? card : default;
        }

        public bool TryDraw(out Card card)
        {
            if (_cards.Count == 0)
            {
                card = default;
                return false;
            }

            var last = _cards.Count - 1;
            card = _cards[last];
            _cards.RemoveAt(last);
            return true;
        }

        public Card DrawMatching(Func<Card, bool> predicate)
        {
            return TryDrawMatching(predicate, out var card) ? card : Draw();
        }

        public bool TryDrawMatching(Func<Card, bool> predicate, out Card card)
        {
            if (predicate == null)
            {
                return TryDraw(out card);
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
                card = default;
                return false;
            }

            var pick = matches[_rng.Next(matches.Count)];
            card = _cards[pick];
            _cards.RemoveAt(pick);
            return true;
        }
    }

    /// <summary>
    /// 牌型评估结果。Type 决定大小，Keys 拆同分；BaseChips 为牌面点数，参与攻击力结算。
    /// </summary>
    public readonly struct HandScore : IComparable<HandScore>
    {
        public HandScore(HandType type, int baseChips, float multiplier, int[] keys, Card[] usedCards, string label, bool beatsAll = false)
        {
            Type = type;
            BaseChips = baseChips;
            Multiplier = multiplier;
            Keys = keys ?? Array.Empty<int>();
            UsedCards = usedCards ?? Array.Empty<Card>();
            Label = label ?? string.Empty;
            BeatsAll = beatsAll;
        }

        public HandType Type { get; }
        public int BaseChips { get; }
        public float Multiplier { get; }
        public int[] Keys { get; }
        public Card[] UsedCards { get; }
        public string Label { get; }
        /// <summary>散牌 235 遗物：比牌时胜过任何未通杀的牌型。</summary>
        public bool BeatsAll { get; }

        public int CompareTo(HandScore other)
        {
            if (BeatsAll != other.BeatsAll)
            {
                return BeatsAll ? 1 : -1;
            }

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

    /// <summary>遗物改写的比牌规则。花色计数类遗物仍看真实花色。</summary>
    public readonly struct HandEvalRules
    {
        public HandEvalRules(bool colorFlush, bool gappedStraight)
        {
            ColorFlush = colorFlush;
            GappedStraight = gappedStraight;
        }

        /// <summary>老花眼：红桃=方片、黑桃=梅花，仅金花/同花顺判定。</summary>
        public bool ColorFlush { get; }

        /// <summary>错峰出行：排序后相邻点差为 1 或 2 即成顺子。</summary>
        public bool GappedStraight { get; }
    }

    /// <summary>
    /// 三张牌炸金花评估。BOSS 失效花色通过 bannedSuit / bannedSuit2 / banFaces 过滤后再比牌。
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

        public static HandScore Evaluate(
            IReadOnlyList<Card> cards,
            Suit? bannedSuit = null,
            bool banFaces = false,
            HandEvalRules rules = default,
            Suit? bannedSuit2 = null)
        {
            var filtered = Filter(cards, bannedSuit, banFaces, bannedSuit2);
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
            var flush = IsFlush(a.Suit, b.Suit, c.Suit, rules.ColorFlush);
            var three = a.Rank == b.Rank && b.Rank == c.Rank;
            var pair = a.Rank == b.Rank || b.Rank == c.Rank || a.Rank == c.Rank;
            var straight = IsStraight(a.Rank, b.Rank, c.Rank, rules.GappedStraight, out var straightHigh);

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

                var chips = a.ChipValue + b.ChipValue + c.ChipValue;
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
        public static HandScore SelectBestOpen(
            Card[] hand,
            bool[] selected,
            int dealt,
            Suit? bannedSuit = null,
            bool banFaces = false,
            HandEvalRules rules = default,
            Suit? bannedSuit2 = null)
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
                return Evaluate(Array.Empty<Card>(), bannedSuit, banFaces, rules, bannedSuit2);
            }

            dealt = Math.Min(dealt, hand.Length);
            if (dealt < 3)
            {
                return Evaluate(Array.Empty<Card>(), bannedSuit, banFaces, rules, bannedSuit2);
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
                        var score = Evaluate(trio, bannedSuit, banFaces, rules, bannedSuit2);
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

            return any ? best : Evaluate(Array.Empty<Card>(), bannedSuit, banFaces, rules, bannedSuit2);
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

        public static Card[] CopyBestOpenCards(
            Card[] hand,
            int dealt,
            Suit? bannedSuit = null,
            bool banFaces = false,
            HandEvalRules rules = default,
            Suit? bannedSuit2 = null)
        {
            var flags = new bool[GameBalance.MaxCardsPerSeat];
            SelectBestOpen(hand, flags, dealt, bannedSuit, banFaces, rules, bannedSuit2);
            return CopySelectedCards(hand, flags);
        }

        /// <summary>伤害 ≈ 牌面筹码 × 底池 × 牌型倍率 × 遗物倍率。</summary>
        public static int ComputeDamage(HandScore score, int pot, float relicMultiplier)
        {
            var value = score.BaseChips * pot * score.Multiplier * relicMultiplier;
            return Math.Max(1, (int)Math.Round(value));
        }

        /// <summary>伤害 = (攻击力 + 牌面点数) × 总倍率（牌型倍率 + 遗物加成）。</summary>
        public static int ComputeAttackDamage(int attack, int cardPoints, float magnification)
        {
            var effectiveAttack = Math.Max(0, attack) + Math.Max(0, cardPoints);
            var value = effectiveAttack * Math.Max(0f, magnification);
            return Math.Max(1, (int)Math.Round(value));
        }

        private static HandScore HighCard(List<Card> filtered)
        {
            var keys = new int[filtered.Count];
            var chips = 0;
            for (var i = 0; i < filtered.Count; i++)
            {
                keys[i] = RankKey(filtered[i].Rank);
                chips += filtered[i].ChipValue;
            }

            return new HandScore(
                HandType.HighCard,
                chips,
                1f,
                keys,
                filtered.ToArray(),
                $"散牌 {filtered[0].DisplayName}");
        }

        private static List<Card> Filter(
            IReadOnlyList<Card> cards,
            Suit? bannedSuit,
            bool banFaces,
            Suit? bannedSuit2 = null)
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

                if (bannedSuit2.HasValue && card.Suit == bannedSuit2.Value)
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

        /// <summary>
        /// 老花眼金花展示花色：同色三张里张数多的那门。张数不够或不是同一色则 false。
        /// </summary>
        public static bool TryColorFlushDisplaySuit(IReadOnlyList<Card> cards, out Suit displaySuit)
        {
            displaySuit = default;
            if (cards == null)
            {
                return false;
            }

            var n = 0;
            var color = -1;
            var first = Suit.Heart;
            var counts = new int[5];
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (!card.IsValid)
                {
                    continue;
                }

                var group = FlushColor(card.Suit);
                if (n == 0)
                {
                    color = group;
                    first = card.Suit;
                }
                else if (group != color)
                {
                    return false;
                }

                counts[(int)card.Suit]++;
                n++;
            }

            if (n != GameBalance.OpenHandSize)
            {
                return false;
            }

            var best = 0;
            var bestSuit = first;
            for (var s = (int)Suit.Heart; s <= (int)Suit.Spade; s++)
            {
                if (counts[s] > best)
                {
                    best = counts[s];
                    bestSuit = (Suit)s;
                }
            }

            displaySuit = bestSuit;
            return true;
        }

        public static Card WithSuit(Card card, Suit suit)
        {
            if (!card.IsValid || card.Suit == suit)
            {
                return card;
            }

            return new Card(suit, card.Rank);
        }

        private static bool IsFlush(Suit a, Suit b, Suit c, bool colorFlush)
        {
            if (colorFlush)
            {
                return FlushColor(a) == FlushColor(b) && FlushColor(b) == FlushColor(c);
            }

            return a == b && b == c;
        }

        private static int FlushColor(Suit suit)
        {
            return suit == Suit.Heart || suit == Suit.Diamond ? 0 : 1;
        }

        private static bool IsStraight(Rank a, Rank b, Rank c, bool gapped, out int high)
        {
            var keys = new[] { RankKey(a), RankKey(b), RankKey(c) };
            Array.Sort(keys);
            if (TryStraightKeys(keys, gapped, out high))
            {
                return true;
            }

            if (keys[2] == 14)
            {
                var wheel = new[] { 1, keys[0], keys[1] };
                Array.Sort(wheel);
                if (TryStraightKeys(wheel, gapped, out high))
                {
                    return true;
                }
            }

            high = 0;
            return false;
        }

        private static bool TryStraightKeys(int[] keys, bool gapped, out int high)
        {
            high = 0;
            if (keys == null || keys.Length < 3 || keys[0] == keys[1] || keys[1] == keys[2])
            {
                return false;
            }

            var d1 = keys[1] - keys[0];
            var d2 = keys[2] - keys[1];
            var maxGap = gapped ? 2 : 1;
            if (d1 >= 1 && d1 <= maxGap && d2 >= 1 && d2 <= maxGap)
            {
                high = keys[2];
                return true;
            }

            return false;
        }

        private static int RankKey(Rank rank) => rank == Rank.Ace ? 14 : (int)rank;
    }
}
