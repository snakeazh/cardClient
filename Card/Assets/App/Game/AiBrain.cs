using System;
using System.Collections.Generic;

namespace App.Game
{
    /// <summary>AI 风格标签。真正调参看 <see cref="AiProfile"/> 四个浮点。</summary>
    public enum AiStyle
    {
        Conservative = 0,
        Aggressive = 1,
        Balanced = 2,
        Recreational = 3
    }

    /// <summary>
    /// 敌人人格。决策树共用，只靠这四个参数拉开风格：
    /// 诈唬率、随机扰动、激进度、开牌耐心。
    /// </summary>
    public sealed class AiProfile
    {
        public AiStyle Style;
        public string Label;
        /// <summary>弱牌诈唬上限，实际还会被读线、短筹、恐吓面具压低，封顶 12%。</summary>
        public float BluffRate;
        /// <summary>胜率扰动幅度。越大越不像「算得太准」。</summary>
        public float Randomness;
        /// <summary>0~1。越高越爱加注、全下、开牌，强/中档阈值也越低。</summary>
        public float Aggression;
        /// <summary>开牌意愿乘数。中牌开牌极严，强/超强才明显生效。</summary>
        public float OpenPatience;

        /// <summary>敌人B 默认人格。</summary>
        public static AiProfile BalancedAggressive => new AiProfile
        {
            Style = AiStyle.Balanced,
            Label = "平衡偏激进",
            BluffRate = 0.10f,
            Randomness = 0.15f,
            Aggression = 0.62f,
            OpenPatience = 0.46f
        };

        /// <summary>敌人A。</summary>
        public static AiProfile Conservative => new AiProfile
        {
            Style = AiStyle.Conservative,
            Label = "保守型",
            BluffRate = 0.05f,
            Randomness = 0.10f,
            Aggression = 0.28f,
            OpenPatience = 0.40f
        };

        /// <summary>敌人C。</summary>
        public static AiProfile Aggressive => new AiProfile
        {
            Style = AiStyle.Aggressive,
            Label = "激进型",
            BluffRate = 0.12f,
            Randomness = 0.18f,
            Aggression = 0.82f,
            OpenPatience = 0.58f
        };

        /// <summary>已定义未使用。</summary>
        public static AiProfile Recreational => new AiProfile
        {
            Style = AiStyle.Recreational,
            Label = "娱乐型",
            BluffRate = 0.12f,
            Randomness = 0.22f,
            Aggression = 0.50f,
            OpenPatience = 0.40f
        };

        /// <summary>BOSS 关覆盖敌人A。</summary>
        public static AiProfile Expert => new AiProfile
        {
            Style = AiStyle.Balanced,
            Label = "高手·平衡偏激进",
            BluffRate = 0.10f,
            Randomness = 0.20f,
            Aggression = 0.68f,
            OpenPatience = 0.50f
        };
    }

    /// <summary>AI 本手动作。Call 在无人下注时等于过牌。</summary>
    public enum AiAction
    {
        Fold = 0,
        Call = 1,
        Raise = 2,
        AllIn = 3,
        /// <summary>付双倍注额，强制与玩家比牌。</summary>
        Open = 4
    }

    /// <summary>按胜率+成牌把牌力分成四档，再走不同决策。</summary>
    public enum HandBand
    {
        Weak = 0,
        Medium = 1,
        Strong = 2,
        Super = 3
    }

    /// <summary>一次决策结果。Reason 会写进对局日志。</summary>
    public readonly struct AiDecision
    {
        public AiDecision(AiAction action, float winRate, string reason)
        {
            Action = action;
            WinRate = winRate;
            Reason = reason ?? string.Empty;
        }

        public AiAction Action { get; }
        public float WinRate { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// AI 单次决策上下文。
    /// 一手最多只能从对手那里赢到 min(自己, 对手) 的血量。
    /// SPR = 有效剩余血量 / 当前底池，决定愿不愿意打大底池。
    /// </summary>
    public sealed class AiContext
    {
        public SeatState Ai;
        public HandScore Score;
        public float WinRate;
        /// <summary>未成同花/顺金，但两张同花且点数接近，弱牌可便宜跟注。</summary>
        public bool StraightFlushDraw;
        public int Pot;
        public int CallCost;
        public int AiChips;
        /// <summary>min(自己血量, 最短对手血量)，一手能赢的上限。</summary>
        public int EffectiveStack;
        public int ShortestOpponent;
        public int PlayerChips;
        /// <summary>用 MinBet 当大盲，换算 BB 数。</summary>
        public int BigBlind;
        public int BettingRound;
        public int RemainingPlayers;
        /// <summary>在仍存活的 AI 里的行动顺序，越大越后位。</summary>
        public int PositionAmongAi;
        public int RemainingAiCount;
        /// <summary>玩家持有恐吓面具。</summary>
        public bool Scare;
        public bool CanOpen;
        public bool CanRaise;
        public bool CanAllIn;
        public PlayerHistory History;
        public Random Rng;

        public float Spr => Pot <= 0 ? 99f : EffectiveStack / (float)Pot;
        public float BbCount => BigBlind <= 0 ? 0f : EffectiveStack / (float)BigBlind;
        public float OwnBbCount => BigBlind <= 0 ? 0f : AiChips / (float)BigBlind;
        public float OppBbCount => BigBlind <= 0 ? 0f : ShortestOpponent / (float)BigBlind;
        public bool IsLate => RemainingAiCount > 0 && PositionAmongAi >= RemainingAiCount - 1;
        public bool OpponentShort => OppBbCount < 10f;
        public bool HeroShort => OwnBbCount < 10f;
        public bool HeroLeads => PlayerChips > 0 && AiChips >= PlayerChips * 2;

        public bool PlayerFolded;
        public bool PlayerLooked;
        public int PlayerStreetCalls;
        public int PlayerLookedCalls;
        public int PlayerBlindCalls;
        public int PlayerConsecutiveCalls;
        public int PlayerConsecutiveBlindCalls;
        public int PlayerHandRaises;
        public bool PlayerCalledFacingRaise;
        public bool PlayerOnlyCalled;
        public bool PlayerDeep;
        public bool PlayerShort;
        public float PlayerMaxCallStackFrac;
        /// <summary>根据本手线 + 历史估算的玩家牌力，0~1。</summary>
        public float PlayerStrength;
    }

    /// <summary>
    /// 玩家行为档案。跨手累计弃/加/看/闷，本手再细记跟注线，用来估牌力。
    /// </summary>
    public sealed class PlayerHistory
    {
        public int Hands;
        public int Folds;
        public int Raises;
        public int Calls;
        public int Looks;
        public int Blinds;
        public int Opens;
        public int FacedRaiseContinues;
        public int FacedRaiseChances;

        public bool LookedThisHand;
        public int StreetCalls;
        public int BlindCalls;
        public int LookedCalls;
        public int HandRaises;
        public int ConsecutiveCalls;
        public int ConsecutiveBlindCalls;
        public bool CalledFacingRaise;
        public float MaxCallStackFrac;
        public int StartChips;

        public float FoldRate => Hands <= 0 ? 0.28f : (float)Folds / Hands;
        public float RaiseRate => Hands <= 0 ? 0.22f : (float)Raises / Hands;
        public float LookRate => Hands <= 0 ? 0.40f : (float)Looks / Hands;
        public float BlindRate => Hands <= 0 ? 0.50f : (float)Blinds / Hands;
        public float ContinueVsRaiseRate => FacedRaiseChances <= 0 ? 0.40f : (float)FacedRaiseContinues / FacedRaiseChances;
        public bool OnlyCalledThisHand => StreetCalls > 0 && HandRaises <= 0;

        /// <summary>新手开始：累计手数 +1，清空本手看牌/跟注线。</summary>
        public void BeginHand(int chips)
        {
            Hands++;
            LookedThisHand = false;
            StreetCalls = 0;
            BlindCalls = 0;
            LookedCalls = 0;
            HandRaises = 0;
            ConsecutiveCalls = 0;
            ConsecutiveBlindCalls = 0;
            CalledFacingRaise = false;
            MaxCallStackFrac = 0f;
            StartChips = Math.Max(0, chips);
        }

        public void NoteFold() => Folds++;

        public void NoteLook()
        {
            Looks++;
            LookedThisHand = true;
            ConsecutiveBlindCalls = 0;
        }

        public void NoteOpen() => Opens++;

        public void NotePlayerBet(bool raise, bool looked, int paid, int chipsBefore, bool facingRaise)
        {
            if (facingRaise)
            {
                FacedRaiseChances++;
                if (!raise)
                {
                    FacedRaiseContinues++;
                    CalledFacingRaise = true;
                }
            }

            if (raise)
            {
                Raises++;
                HandRaises++;
                ConsecutiveCalls = 0;
                ConsecutiveBlindCalls = 0;
                return;
            }

            Calls++;
            StreetCalls++;
            ConsecutiveCalls++;
            var frac = chipsBefore <= 0 ? 1f : paid / (float)chipsBefore;
            if (frac > MaxCallStackFrac)
            {
                MaxCallStackFrac = frac;
            }

            if (looked)
            {
                LookedCalls++;
                ConsecutiveBlindCalls = 0;
            }
            else
            {
                Blinds++;
                BlindCalls++;
                ConsecutiveBlindCalls++;
            }
        }

        /// <summary>
        /// 根据本手看牌/闷跟/跟加注/投入比例，估一个 0~1 的玩家牌力。
        /// 看牌后持续跟、跟加注、大额投入都当强；深筹只跟小注会往回调。
        /// </summary>
        public float EstimateStrength(int playerChips, int bigBlind)
        {
            var strength = 0.30f;
            if (LookedThisHand)
            {
                strength += 0.08f;
            }
            else
            {
                strength -= 0.06f * (1f - Clamp01(BlindRate));
            }

            strength += Math.Min(0.18f, ConsecutiveCalls * 0.05f);
            if (!LookedThisHand && ConsecutiveBlindCalls >= 3)
            {
                strength += 0.10f;
            }

            if (LookedThisHand && LookedCalls > 0)
            {
                strength += 0.10f + Math.Min(0.12f, (LookedCalls - 1) * 0.05f);
            }

            if (CalledFacingRaise)
            {
                strength += LookedThisHand ? 0.12f : 0.05f;
            }

            strength += MaxCallStackFrac * (LookedThisHand ? 0.24f : 0.10f);
            if (HandRaises > 0)
            {
                strength += LookedThisHand ? 0.12f : 0.05f;
            }

            var bb = bigBlind <= 0 ? 0f : playerChips / (float)bigBlind;
            if (LookedThisHand && OnlyCalledThisHand && bb > 40f && MaxCallStackFrac < 0.20f)
            {
                strength = strength * 0.65f + 0.42f * 0.35f;
            }

            if (bb < 10f && StreetCalls > 0)
            {
                strength += 0.12f;
            }

            if (FoldRate > 0.40f && StreetCalls > 0)
            {
                strength += 0.06f;
            }

            if (RaiseRate > 0.35f && OnlyCalledThisHand && LookedThisHand)
            {
                strength -= 0.05f;
            }

            if (LookRate < 0.25f && LookedThisHand)
            {
                strength += 0.04f;
            }

            if (ContinueVsRaiseRate > 0.70f && CalledFacingRaise)
            {
                strength += 0.04f;
            }

            return Clamp01(strength);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }

    /// <summary>
    /// 炸金花胜率估计：已知自己三张，随机补对手手牌做蒙特卡洛。
    /// 牌不够抽时退回按牌型的经验胜率。
    /// </summary>
    public static class ZhaJinHuaOdds
    {
        private static readonly Card[] FullDeck = BuildFullDeck();

        /// <param name="samples">默认 220 次，局内实时决策用。</param>
        public static float EstimateWinRate(
            IReadOnlyList<Card> hero,
            IReadOnlyList<Card> visibleDead,
            int opponents,
            Random rng,
            int samples = 220)
        {
            if (hero == null || hero.Count < 3 || opponents <= 0)
            {
                return 1f;
            }

            var heroScore = HandEvaluator.Evaluate(hero);
            var dead = new HashSet<int>();
            AddDead(dead, hero);
            AddDead(dead, visibleDead);

            var live = new List<Card>(52);
            for (var i = 0; i < FullDeck.Length; i++)
            {
                if (!dead.Contains(Key(FullDeck[i])))
                {
                    live.Add(FullDeck[i]);
                }
            }

            var need = opponents * 3;
            if (live.Count < need)
            {
                return TypeFallback(heroScore.Type, opponents);
            }

            var wins = 0f;
            var buffer = new Card[3];
            for (var s = 0; s < samples; s++)
            {
                ShuffleTail(live, rng);
                var lost = false;
                var tied = false;
                for (var o = 0; o < opponents; o++)
                {
                    buffer[0] = live[o * 3];
                    buffer[1] = live[o * 3 + 1];
                    buffer[2] = live[o * 3 + 2];
                    var opp = HandEvaluator.Evaluate(buffer);
                    var cmp = heroScore.CompareTo(opp);
                    if (cmp < 0)
                    {
                        lost = true;
                        break;
                    }

                    if (cmp == 0)
                    {
                        tied = true;
                    }
                }

                if (!lost)
                {
                    wins += tied ? 0.5f : 1f;
                }
            }

            return Clamp01(wins / samples);
        }

        /// <summary>两张同花且点数差 1~2（含 A-2），未成金花/顺金时视为听牌。</summary>
        public static bool IsStraightFlushDraw(IReadOnlyList<Card> cards)
        {
            if (cards == null || cards.Count < 3)
            {
                return false;
            }

            var made = HandEvaluator.Evaluate(cards);
            if (made.Type >= HandType.Flush)
            {
                return false;
            }

            for (var i = 0; i < 3; i++)
            {
                for (var j = i + 1; j < 3; j++)
                {
                    if (cards[i].Suit != cards[j].Suit)
                    {
                        continue;
                    }

                    var a = RankKey(cards[i].Rank);
                    var b = RankKey(cards[j].Rank);
                    var gap = Math.Abs(a - b);
                    if (gap == 1 || gap == 2 || (a == 14 && b == 2) || (b == 14 && a == 2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static float TypeFallback(HandType type, int opponents)
        {
            float p;
            switch (type)
            {
                case HandType.ThreeOfAKind: p = 0.98f; break;
                case HandType.StraightFlush: p = 0.92f; break;
                case HandType.Flush: p = 0.78f; break;
                case HandType.Straight: p = 0.62f; break;
                case HandType.Pair: p = 0.38f; break;
                default: p = 0.18f; break;
            }

            var field = p;
            for (var i = 1; i < opponents; i++)
            {
                field *= p;
            }

            return field;
        }

        private static void AddDead(HashSet<int> dead, IReadOnlyList<Card> cards)
        {
            if (cards == null)
            {
                return;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                dead.Add(Key(cards[i]));
            }
        }

        private static int Key(Card card) => ((int)card.Suit << 8) | (int)card.Rank;

        private static int RankKey(Rank rank) => rank == Rank.Ace ? 14 : (int)rank;

        private static void ShuffleTail(List<Card> cards, Random rng)
        {
            var n = Math.Min(cards.Count, 16);
            for (var i = 0; i < n; i++)
            {
                var j = rng.Next(i, cards.Count);
                var tmp = cards[i];
                cards[i] = cards[j];
                cards[j] = tmp;
            }
        }

        private static Card[] BuildFullDeck()
        {
            var deck = new Card[52];
            var n = 0;
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                {
                    deck[n++] = new Card(suit, rank);
                }
            }

            return deck;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }

    /// <summary>
    /// 敌人下注决策。流程：扰动胜率 → 分档 → 短筹走 shove，否则按弱/中/强/超强出招。
    /// AI 始终知道自己的牌，没有「闷牌看不见」这一层。
    /// </summary>
    public static class AiBrain
    {
        /// <summary>根据当前桌面状态选出 Fold / Call / Raise / AllIn / Open。</summary>
        public static AiDecision Decide(AiContext ctx)
        {
            var ai = ctx.Ai;
            var profile = ai.Profile ?? AiProfile.BalancedAggressive;
            var rng = ctx.Rng;
            var score = ctx.Score;
            var noise = ((float)rng.NextDouble() * 2f - 1f) * profile.Randomness;
            var wr = Clamp01(ctx.WinRate + noise);

            var potOdds = (ctx.Pot + ctx.CallCost) <= 0 ? 1f : ctx.CallCost / (float)(ctx.Pot + ctx.CallCost);
            var stackOdds = ctx.EffectiveStack <= 0 ? 1f : ctx.CallCost / (float)ctx.EffectiveStack;
            var foldPressure = ctx.History != null ? ctx.History.FoldRate : 0.28f;
            var playerAggro = ctx.History != null ? ctx.History.RaiseRate : 0.22f;
            var read = ctx.PlayerFolded ? 0.30f : ctx.PlayerStrength;

            var sprLow = ctx.Spr < 3f;
            var sprHigh = ctx.Spr > 6f;
            var shortStack = ctx.BbCount < 10f;

            // 恐吓面具 / 前位 / 自己短筹 / 读你偏强 → 下调有效胜率。
            if (ctx.Scare)
            {
                wr = Clamp01(wr - 0.04f);
            }

            if (!ctx.IsLate)
            {
                wr = Clamp01(wr - 0.02f);
            }

            if (ctx.HeroShort)
            {
                wr = Clamp01(wr - 0.03f);
            }

            if (!ctx.PlayerFolded)
            {
                wr = Clamp01(wr - (read - 0.32f) * 0.28f);
            }

            // 激进人格把强/中档门槛压低；SPR 低更容易打大，SPR 高更谨慎。
            var high = 0.58f - (profile.Aggression - 0.5f) * 0.12f;
            var mid = 0.32f - (profile.Aggression - 0.5f) * 0.08f;
            if (sprLow)
            {
                high -= 0.06f;
                mid -= 0.05f;
            }
            else if (sprHigh)
            {
                high += 0.05f;
                mid += 0.04f;
            }

            if (!ctx.IsLate)
            {
                high += 0.03f;
                mid += 0.02f;
            }

            if (!ctx.PlayerFolded && ctx.PlayerLooked && ctx.PlayerLookedCalls > 0)
            {
                high += 0.03f;
                mid += 0.03f;
            }

            var bluff = Math.Min(profile.BluffRate, 0.12f);
            if (ctx.Scare)
            {
                bluff *= 0.7f;
            }

            // 你爱弃就多诈；短筹对局几乎不诈。
            bluff += (foldPressure - 0.28f) * 0.18f;
            if (ctx.IsLate)
            {
                bluff += 0.02f;
            }

            if (ctx.OpponentShort || ctx.PlayerShort)
            {
                bluff *= 0.30f;
            }

            var blockAirBluff = false;
            ApplyPlayerLineToBluff(ctx, read, ref bluff, ref blockAirBluff);
            bluff = Clamp01(Math.Min(bluff, 0.12f));

            var band = ClassifyHand(score, wr, high, mid);
            // 看牌后连续跟 ≥2：粘着，关掉诈唬、开牌更谨慎。
            var lookedSticky = !ctx.PlayerFolded && ctx.PlayerLooked && ctx.PlayerConsecutiveCalls >= 2;
            var playerStrong = !ctx.PlayerFolded && (read >= 0.55f || lookedSticky);
            // 闷着且没连跟 3 次：当弱，中牌会压你。
            var playerWeak = !ctx.PlayerFolded && !ctx.PlayerLooked && ctx.PlayerConsecutiveBlindCalls < 3 && read < 0.48f;
            var canBluff = !blockAirBluff && !ctx.OpponentShort && !ctx.PlayerShort && playerAggro < 0.45f && foldPressure >= 0.22f;
            if (lookedSticky || playerStrong)
            {
                canBluff = false;
            }

            if (shortStack)
            {
                return DecideShort(ctx, wr, band, potOdds, profile, rng);
            }

            switch (band)
            {
                case HandBand.Weak:
                    return DecideWeak(ctx, wr, potOdds, stackOdds, bluff, canBluff, rng);
                case HandBand.Medium:
                    return DecideMedium(ctx, wr, potOdds, stackOdds, sprHigh, playerStrong, playerWeak, profile, rng);
                case HandBand.Strong:
                    return DecideStrong(ctx, wr, lookedSticky, profile, rng);
                default:
                    return DecideSuper(ctx, wr, lookedSticky, profile, rng);
            }
        }

        /// <summary>同花顺/高胜率→超强；金花→强；对子→中；其余弱。门槛随激进和 SPR 浮动。</summary>
        private static HandBand ClassifyHand(HandScore score, float wr, float high, float mid)
        {
            if (score.Type >= HandType.StraightFlush || wr >= 0.78f)
            {
                return HandBand.Super;
            }

            if (wr >= high || score.Type >= HandType.Flush)
            {
                return HandBand.Strong;
            }

            if (wr >= mid || score.Type >= HandType.Pair)
            {
                return HandBand.Medium;
            }

            return HandBand.Weak;
        }

        /// <summary>弱牌：过牌或便宜跟，默认弃；仅在你看起来虚时偶发诈唬。</summary>
        private static AiDecision DecideWeak(
            AiContext ctx,
            float wr,
            float potOdds,
            float stackOdds,
            float bluff,
            bool canBluff,
            Random rng)
        {
            if (ctx.CallCost <= 0)
            {
                return new AiDecision(AiAction.Call, wr, "弱牌过牌");
            }

            if (ctx.StraightFlushDraw && potOdds <= 0.18f && stackOdds <= 0.12f)
            {
                return new AiDecision(AiAction.Call, wr, "弱牌听牌便宜跟");
            }

            if (wr >= potOdds && stackOdds <= 0.12f && potOdds <= 0.16f)
            {
                return new AiDecision(AiAction.Call, wr, "弱牌便宜跟注");
            }

            if (canBluff && ctx.CanRaise && stackOdds < 0.12f && rng.NextDouble() < bluff * 0.45f)
            {
                return new AiDecision(AiAction.Raise, wr, "弱牌偶发诈唬");
            }

            return new AiDecision(AiAction.Fold, wr, "弱牌弃牌");
        }

        /// <summary>中牌：遇强则控池或弃，遇弱则小注；默认跟注控池，开牌极严。</summary>
        private static AiDecision DecideMedium(
            AiContext ctx,
            float wr,
            float potOdds,
            float stackOdds,
            bool sprHigh,
            bool playerStrong,
            bool playerWeak,
            AiProfile profile,
            Random rng)
        {
            if (playerStrong)
            {
                if (sprHigh && stackOdds > 0.18f && wr < potOdds + 0.08f)
                {
                    return new AiDecision(AiAction.Fold, wr, "中牌遇强势弃牌");
                }

                if (wr >= potOdds || stackOdds <= 0.22f)
                {
                    return new AiDecision(AiAction.Call, wr, "中牌遇强势跟注控池");
                }

                return new AiDecision(AiAction.Fold, wr, "中牌遇强势弃牌");
            }

            if (playerWeak && ctx.CanRaise && !ctx.OpponentShort && !ctx.PlayerShort
                && stackOdds < 0.20f && WantBet(0.20f * profile.Aggression, ctx, rng))
            {
                return new AiDecision(AiAction.Raise, wr, "中牌对弱势小注");
            }

            if (ctx.CanRaise && !ctx.OpponentShort && !sprHigh && stackOdds < 0.18f
                && WantBet(0.16f * profile.Aggression, ctx, rng))
            {
                return new AiDecision(AiAction.Raise, wr, "中牌小注控池");
            }

            if (ctx.CanOpen && ShouldOpen(ctx, HandBand.Medium, wr, profile, rng))
            {
                return new AiDecision(AiAction.Open, wr, "中牌短筹谨慎开牌");
            }

            if (ctx.CallCost <= 0 || wr >= potOdds || stackOdds <= 0.22f)
            {
                return new AiDecision(AiAction.Call, wr, "中牌跟注控池");
            }

            return new AiDecision(AiAction.Fold, wr, "中牌停手");
        }

        /// <summary>强牌：优先考虑开牌；你短筹或 SPR 低可全下；否则价值加注或控池。</summary>
        private static AiDecision DecideStrong(AiContext ctx, float wr, bool lookedSticky, AiProfile profile, Random rng)
        {
            if (ctx.CanOpen && ShouldOpen(ctx, HandBand.Strong, wr, profile, rng))
            {
                return new AiDecision(AiAction.Open, wr, lookedSticky ? "强牌对持续跟注开牌" : "强牌开牌");
            }

            if (!ctx.PlayerFolded && ctx.PlayerShort && ctx.PlayerStreetCalls > 0 && ctx.CanAllIn && wr >= 0.62f)
            {
                return new AiDecision(AiAction.AllIn, wr, "强牌逼短筹全下");
            }

            if (ctx.Spr < 3f && ctx.CanAllIn && wr >= 0.70f)
            {
                return new AiDecision(AiAction.AllIn, wr, "强牌+SPR低全下");
            }

            var betChance = 0.52f + (profile.Aggression - 0.5f) * 0.16f;
            if (lookedSticky)
            {
                betChance *= 0.85f;
            }

            if (ctx.CanRaise && WantBet(betChance, ctx, rng))
            {
                return new AiDecision(AiAction.Raise, wr, lookedSticky ? "强牌价值加注" : "强牌加注");
            }

            return new AiDecision(AiAction.Call, wr, "强牌停手控池");
        }

        /// <summary>超强牌：更爱开牌；短筹全下；前两轮可能故意跟注诱导。</summary>
        private static AiDecision DecideSuper(AiContext ctx, float wr, bool lookedSticky, AiProfile profile, Random rng)
        {
            var potBig = IsPotLarge(ctx);
            if (ctx.CanOpen && ShouldOpen(ctx, HandBand.Super, wr, profile, rng))
            {
                return new AiDecision(AiAction.Open, wr, potBig ? "超强牌底池足够，主动开牌" : "超强牌开牌");
            }

            if (ctx.HeroShort && ctx.CanAllIn)
            {
                return new AiDecision(AiAction.AllIn, wr, "超强牌短筹全下");
            }

            var induce = !potBig && ctx.BettingRound <= 2 && ctx.PlayerStreetCalls > 0 && !ctx.HeroShort;
            if (induce && rng.NextDouble() < 0.38f)
            {
                return new AiDecision(AiAction.Call, wr, "超强牌诱导继续下注");
            }

            var betChance = 0.58f;
            if (ctx.CanRaise && WantBet(betChance, ctx, rng))
            {
                return new AiDecision(AiAction.Raise, wr, "超强牌价值加注");
            }

            return new AiDecision(AiAction.Call, wr, "超强牌停手诱导");
        }

        /// <summary>按你本手线改诈唬：闷跟少则加诈，看牌大额跟则禁止空气诈唬。</summary>
        private static void ApplyPlayerLineToBluff(AiContext ctx, float read, ref float bluff, ref bool blockAirBluff)
        {
            if (ctx.PlayerFolded)
            {
                return;
            }

            if (!ctx.PlayerLooked)
            {
                if (ctx.PlayerConsecutiveBlindCalls < 3)
                {
                    bluff += 0.05f;
                }
                else
                {
                    bluff *= 0.55f;
                }
            }
            else
            {
                bluff *= 0.75f;
                if (ctx.PlayerLookedCalls > 0)
                {
                    bluff *= 0.70f;
                }

                if (ctx.PlayerLookedCalls > 0 && ctx.PlayerMaxCallStackFrac >= 0.22f)
                {
                    bluff *= 0.35f;
                    blockAirBluff = true;
                }

                if (ctx.PlayerCalledFacingRaise)
                {
                    bluff *= 0.65f;
                }
            }

            if (ctx.PlayerShort && ctx.PlayerStreetCalls > 0)
            {
                bluff *= 0.15f;
                blockAirBluff = true;
            }

            if (ctx.PlayerConsecutiveCalls >= 3)
            {
                bluff *= 0.70f;
            }

            if (read >= 0.62f)
            {
                bluff *= 0.50f;
            }
        }

        /// <summary>有效筹码 &lt; 10BB：弱牌弃，中牌以上倾向全下或开牌拼一手。</summary>
        private static AiDecision DecideShort(
            AiContext ctx,
            float wr,
            HandBand band,
            float potOdds,
            AiProfile profile,
            Random rng)
        {
            if (band == HandBand.Weak)
            {
                if (ctx.CallCost <= 0)
                {
                    return new AiDecision(AiAction.Call, wr, "短筹弱牌过牌");
                }

                if (wr >= potOdds && ctx.CallCost <= ctx.EffectiveStack * 0.25f)
                {
                    return new AiDecision(AiAction.Call, wr, "短筹弱牌便宜跟");
                }

                return new AiDecision(AiAction.Fold, wr, "短筹弱牌弃牌");
            }

            if (ctx.CanOpen && ShouldOpen(ctx, band, wr, profile, rng))
            {
                return new AiDecision(AiAction.Open, wr, "短筹拼一手开牌");
            }

            if (band >= HandBand.Strong)
            {
                return ShoveOrRaise(ctx, wr, "短筹强牌全下");
            }

            if (ctx.CallCost <= 0)
            {
                return new AiDecision(AiAction.Call, wr, "短筹中牌过牌");
            }

            return ShoveOrRaise(ctx, wr, "短筹中牌拼一手");
        }

        /// <summary>能全下就全下，否则加注，再不行跟注。</summary>
        private static AiDecision ShoveOrRaise(AiContext ctx, float wr, string reason)
        {
            if (ctx.CanAllIn)
            {
                return new AiDecision(AiAction.AllIn, wr, reason);
            }

            if (ctx.CanRaise)
            {
                return new AiDecision(AiAction.Raise, wr, reason);
            }

            return new AiDecision(AiAction.Call, wr, reason);
        }

        /// <summary>
        /// 是否主动开牌单挑玩家。弱牌永不；中牌仅短筹+大底池；
        /// 强/超强要底池够大、自己短筹、或你持续看牌跟。
        /// </summary>
        private static bool ShouldOpen(AiContext ctx, HandBand band, float wr, AiProfile profile, Random rng)
        {
            if (!ctx.CanOpen || band == HandBand.Weak)
            {
                return false;
            }

            var lookedSticky = !ctx.PlayerFolded && ctx.PlayerLooked && ctx.PlayerConsecutiveCalls >= 2;
            var blindLine = !ctx.PlayerFolded && !ctx.PlayerLooked;
            var potBig = IsPotLarge(ctx);
            var shortAi = ctx.HeroShort || ctx.BbCount < 12f;
            var chance = 0f;

            if (band == HandBand.Medium)
            {
                if (!shortAi || !potBig || lookedSticky)
                {
                    return false;
                }

                return rng.NextDouble() < 0.16f * profile.OpenPatience;
            }

            if (band == HandBand.Strong)
            {
                if (lookedSticky && wr < 0.62f)
                {
                    return false;
                }

                if (blindLine && !potBig && !shortAi)
                {
                    return false;
                }

                if (!potBig && !shortAi && ctx.PlayerConsecutiveCalls < 2)
                {
                    return false;
                }

                chance = 0.22f;
                if (potBig)
                {
                    chance += 0.16f;
                }

                if (shortAi)
                {
                    chance += 0.20f;
                }

                if (lookedSticky)
                {
                    chance *= 0.50f;
                }

                return rng.NextDouble() < chance * profile.OpenPatience;
            }

            if (ctx.BettingRound <= 1 && !shortAi && !potBig)
            {
                return false;
            }

            chance = potBig ? 0.48f : 0.18f;
            if (shortAi)
            {
                chance = 0.62f;
            }

            if (lookedSticky)
            {
                chance *= 0.70f;
            }

            if (blindLine && !potBig)
            {
                chance *= 0.40f;
            }

            return rng.NextDouble() < chance * profile.OpenPatience;
        }

        /// <summary>加注意愿。短筹对局概率打 55 折，且不超过 58%。</summary>
        private static bool WantBet(float chance, AiContext ctx, Random rng)
        {
            if (!ctx.CanRaise)
            {
                return false;
            }

            chance = Math.Min(chance, 0.58f);
            if (ctx.OpponentShort || ctx.PlayerShort)
            {
                chance *= 0.55f;
            }

            return rng.NextDouble() < chance;
        }

        /// <summary>SPR &lt; 2.5 或底池已接近有效筹码 / 8BB，视为大底池。</summary>
        private static bool IsPotLarge(AiContext ctx)
        {
            if (ctx.Pot <= 0)
            {
                return false;
            }

            return ctx.Spr < 2.5f || ctx.Pot >= Math.Max(ctx.EffectiveStack * 0.8f, ctx.BigBlind * 8);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
