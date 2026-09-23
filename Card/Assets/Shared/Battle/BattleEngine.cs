using System;
using System.Collections.Generic;
using System.Linq;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    public abstract class BattleMode
    {
        public abstract BattleModeKind Kind { get; }
    }

    public sealed class PveMode : BattleMode
    {
        public override BattleModeKind Kind => BattleModeKind.Pve;
    }

    public sealed class PvpMode : BattleMode
    {
        public override BattleModeKind Kind => BattleModeKind.Pvp;
    }

    public sealed class BattleSnapshot
    {
        public int Seed { get; init; }

        public BattleModeKind Mode { get; init; }

        public BattlePhase Phase { get; init; }

        public IReadOnlyList<SeatSetup> Seats { get; init; } = Array.Empty<SeatSetup>();

        public IReadOnlyList<Card>[] Hands { get; init; } = Array.Empty<Card[]>();

        public IReadOnlyList<int>[] Picked { get; init; } = Array.Empty<int[]>();

        /// <summary>对应座位是否显式发过 pick 指令。false = 发牌/换牌时的自动兜底选牌，摊牌前不下发给客户端。</summary>
        public bool[] PickedExplicit { get; init; } = Array.Empty<bool>();

        public HandScore[] Scores { get; init; } = Array.Empty<HandScore>();

        public IReadOnlyList<int> Winners { get; init; } = Array.Empty<int>();

        public IReadOnlyList<int> Damages { get; init; } = Array.Empty<int>();

        public IReadOnlyList<BattleEvent> Events { get; init; } = Array.Empty<BattleEvent>();
    }

    public sealed class BattleEngine
    {
        private readonly BattleMode _mode;
        private readonly int _seed;
        private readonly IReadOnlyList<SeatSetup> _seats;
        private readonly IGameTables _tables;
        private Deck? _deck;
        private BattleSnapshot _snapshot;
        private int _showdownCount;
        private int[][] _picked = EmptyPicked();
        private bool[] _pickedExplicit = new bool[BattleLimits.RoomSeats];
        private bool[] _rubbed = new bool[BattleLimits.RoomSeats];
        private int[] _rubsUsed = new int[BattleLimits.RoomSeats];

        public BattleEngine(BattleMode mode, int seed, IReadOnlyList<SeatSetup> seats, IGameTables tables)
        {
            _mode = mode;
            _seed = seed;
            _seats = PadSeats(seats, mode.Kind);
            _tables = tables;
            _snapshot = EmptySnapshot();
        }

        public BattleSnapshot Snapshot => _snapshot;

        /// <summary>引擎内部座位输入（构造时克隆自入参）；结算前需要回写的字段（如剩余技能次数）通过它改才生效。</summary>
        public SeatSetup MutableSeat(int seatId) => _seats[seatId];

        public BattleSnapshot Apply(BattleCommand cmd)
        {
            var prev = HandEvaluator.Tables;
            HandEvaluator.Tables = _tables;
            try
            {
                if (cmd.Type == BattleCommandType.Deal)
                {
                    Deal(BattleLimits.OpenHandSize);
                }
                else if (cmd.Type == BattleCommandType.DealHole)
                {
                    Deal(BattleLimits.MaxCardsPerSeat);
                }
                else if (cmd.Type == BattleCommandType.Pick)
                {
                    Pick(cmd.SeatId, cmd.Indexes);
                }
                else if (cmd.Type == BattleCommandType.Rub)
                {
                    Rub(cmd.SeatId, cmd.Index);
                }
                else if (cmd.Type == BattleCommandType.Replace)
                {
                    Replace(cmd.SeatId);
                }
                else if (cmd.Type == BattleCommandType.Showdown || cmd.Type == BattleCommandType.Open)
                {
                    if (_snapshot.Phase == BattlePhase.Idle)
                    {
                        Deal(BattleLimits.OpenHandSize);
                    }

                    ScoreHands();
                }

                return _snapshot;
            }
            finally
            {
                HandEvaluator.Tables = prev;
            }
        }

        /// <summary>主线 5 张手牌：先发 3 张公开位，余下张数从同一副牌继续抽。</summary>
        public Card DrawExtra()
        {
            if (_deck == null)
            {
                Deal(BattleLimits.OpenHandSize);
            }

            return _deck!.Draw();
        }

        public IReadOnlyList<Card> OpenCards(int seat)
            => TakeOpen(_snapshot.Hands[seat], _picked[seat]);

        private void Deal(int size)
        {
            _deck = new Deck(_seed);
            _rubbed = new bool[BattleLimits.RoomSeats];
            _rubsUsed = new int[BattleLimits.RoomSeats];
            _picked = EmptyPicked();
            _pickedExplicit = new bool[BattleLimits.RoomSeats];
            var hands = new Card[BattleLimits.RoomSeats][];
            for (var i = 0; i < BattleLimits.RoomSeats; i++)
            {
                var active = IsSeatActive(i);
                var hand = new Card[size];
                if (active)
                {
                    for (var c = 0; c < hand.Length; c++)
                    {
                        hand[c] = _deck.Draw();
                    }
                }

                hands[i] = hand;
                _picked[i] = AutoPick(i, hand);
            }

            PublishDealt(hands, "deal");
        }

        private void Pick(int seatId, int[] indexes)
        {
            RequireDealt(seatId);
            var hand = _snapshot.Hands[seatId];
            if (indexes == null || indexes.Length != BattleLimits.OpenHandSize)
            {
                throw new InvalidOperationException("Pick needs 3 indexes.");
            }

            var seen = new bool[hand.Count];
            for (var n = 0; n < indexes.Length; n++)
            {
                var index = indexes[n];
                if (index < 0 || index >= hand.Count || seen[index] || !hand[index].IsValid)
                {
                    throw new InvalidOperationException("Invalid pick.");
                }

                seen[index] = true;
            }

            _picked[seatId] = (int[])indexes.Clone();
            _pickedExplicit[seatId] = true;
            PublishDealt(CloneHands(), "pick");
        }

        private void Rub(int seatId, int index)
        {
            RequireDealt(seatId);
            var hands = CloneHands();
            var hand = hands[seatId];
            if (index < 0 || index >= hand.Length || !hand[index].IsValid)
            {
                throw new InvalidOperationException("Invalid rub index.");
            }

            var next = _deck!.Draw();
            if (!next.IsValid)
            {
                throw new InvalidOperationException("Deck empty.");
            }

            hand[index] = next;
            _rubbed[seatId] = true;
            _rubsUsed[seatId]++;
            if (!_seats[seatId].IsHuman)
            {
                _picked[seatId] = PickBest(hand);
            }

            PublishDealt(hands, "rub");
        }

        private void Replace(int seatId)
        {
            RequireDealt(seatId);
            var hands = CloneHands();
            var size = Math.Max(hands[seatId].Length, BattleLimits.MaxCardsPerSeat);
            var hand = new Card[size];
            for (var c = 0; c < hand.Length; c++)
            {
                var next = _deck!.Draw();
                if (!next.IsValid)
                {
                    throw new InvalidOperationException("Deck empty.");
                }

                hand[c] = next;
            }

            hands[seatId] = hand;
            _picked[seatId] = AutoPick(seatId, hand);
            _pickedExplicit[seatId] = false;
            PublishDealt(hands, "replace");
        }

        private void ScoreHands()
        {
            var hands = _snapshot.Hands;
            var scores = new HandScore[hands.Length];
            HandScore? best = null;
            var winners = new List<int>();
            for (var i = 0; i < hands.Length; i++)
            {
                var open = OpenCards(i);
                if (!IsSeatActive(i) || !HasOpenHand(open))
                {
                    continue;
                }

                var rules = SeatHandRules(i);
                scores[i] = HandEvaluator.Evaluate(open, rules: rules);
                if (best == null || scores[i].CompareTo(best.Value) > 0)
                {
                    best = scores[i];
                    winners.Clear();
                    winners.Add(i);
                }
                else if (scores[i].CompareTo(best.Value) == 0)
                {
                    winners.Add(i);
                }
            }

            var rng = new Random(unchecked(_seed * 1103515245 + 12345));
            var damages = new int[hands.Length];
            var firstShow = _showdownCount == 0;
            _showdownCount++;
            var alive = 0;
            for (var i = 0; i < hands.Length; i++)
            {
                if (IsSeatActive(i))
                {
                    alive++;
                }
            }

            var isPvp = _mode.Kind == BattleModeKind.Pvp;
            for (var i = 0; i < scores.Length; i++)
            {
                var open = OpenCards(i);
                if (!IsSeatActive(i) || !HasOpenHand(open))
                {
                    continue;
                }

                var seat = _seats[i];
                CombatDamageInput input;
                if (seat.IsHuman)
                {
                    input = CombatBonuses.BuildPlayerInput(
                        _tables,
                        seat,
                        scores[i],
                        new CombatSituation
                        {
                            FirstShow = firstShow,
                            RubbedThisHand = _rubbed[i],
                            RubsUsedThisHand = _rubsUsed[i],
                            // 字段是历史命名：PeekLeft=搓牌剩余、XRayLeft=透视剩余（与 PvE RelicCombatContext 的 PeekGoodCharges/ChaKanGoodCharges 一致）。
                            PeekLeft = seat.RubLeft,
                            XRayLeft = seat.PeekLeft,
                            ReplaceLeft = seat.ReplaceLeft,
                            AliveOpponents = Math.Max(0, alive - 1),
                            AttackerHp = seat.Hp,
                            AttackerMaxHp = seat.MaxHp,
                            DefenderIsPlayer = isPvp,
                            DefenderIsBoss = false,
                            DefenderHp = 0,
                            Shown = open,
                            Unshown = UnshownCards(i)
                        });
                }
                else
                {
                    input = new CombatDamageInput
                    {
                        IsPlayer = false,
                        Attack = seat.Attack > 0 ? seat.Attack : 1,
                        HandTypeMag = scores[i].Multiplier,
                        FlintMultiplier = 1f
                    };
                }

                damages[i] = CombatDamage.Resolve(input, rng).Damage;
            }

            _snapshot = new BattleSnapshot
            {
                Seed = _seed,
                Mode = _mode.Kind,
                Phase = BattlePhase.Showdown,
                Seats = _seats,
                Hands = hands,
                Picked = SnapshotPicked(),
                PickedExplicit = (bool[])_pickedExplicit.Clone(),
                Scores = scores,
                Winners = winners,
                Damages = damages,
                Events = new[] { new BattleEvent { Type = BattleEventType.Compared, Message = "showdown" } }
            };
        }

        private void RequireDealt(int seatId)
        {
            if (_snapshot.Phase != BattlePhase.Dealt)
            {
                throw new InvalidOperationException("Hand is locked.");
            }

            if (!IsSeatActive(seatId))
            {
                throw new InvalidOperationException("Seat is inactive.");
            }
        }

        private int[] AutoPick(int seat, Card[] hand)
        {
            if (!_seats[seat].IsHuman && hand.Length >= BattleLimits.OpenHandSize)
            {
                return PickBest(hand);
            }

            return DefaultPick(hand.Length);
        }

        private void PublishDealt(IReadOnlyList<Card>[] hands, string message)
        {
            _snapshot = new BattleSnapshot
            {
                Seed = _seed,
                Mode = _mode.Kind,
                Phase = BattlePhase.Dealt,
                Seats = _seats,
                Hands = hands,
                Picked = SnapshotPicked(),
                PickedExplicit = (bool[])_pickedExplicit.Clone(),
                Scores = new HandScore[BattleLimits.RoomSeats],
                Damages = new int[BattleLimits.RoomSeats],
                Events = new[] { new BattleEvent { Type = BattleEventType.Dealt, Message = message } }
            };
        }

        private Card[][] CloneHands()
        {
            var src = _snapshot.Hands;
            var hands = new Card[src.Length][];
            for (var i = 0; i < src.Length; i++)
            {
                var row = src[i];
                var copy = new Card[row.Count];
                for (var c = 0; c < row.Count; c++)
                {
                    copy[c] = row[c];
                }

                hands[i] = copy;
            }

            return hands;
        }

        private IReadOnlyList<int>[] SnapshotPicked()
        {
            var arr = new int[_picked.Length][];
            for (var i = 0; i < _picked.Length; i++)
            {
                arr[i] = (int[])_picked[i].Clone();
            }

            return arr;
        }

        private IReadOnlyList<Card> UnshownCards(int seat)
        {
            var hand = _snapshot.Hands[seat];
            var pick = _picked[seat];
            if (hand.Count <= BattleLimits.OpenHandSize)
            {
                return Array.Empty<Card>();
            }

            var used = new bool[hand.Count];
            for (var n = 0; n < pick.Length; n++)
            {
                var index = pick[n];
                if (index >= 0 && index < used.Length)
                {
                    used[index] = true;
                }
            }

            var list = new List<Card>();
            for (var c = 0; c < hand.Count; c++)
            {
                if (!used[c] && hand[c].IsValid)
                {
                    list.Add(hand[c]);
                }
            }

            return list;
        }

        private static IReadOnlyList<Card> TakeOpen(IReadOnlyList<Card> hand, int[] pick)
        {
            if (hand.Count < BattleLimits.OpenHandSize)
            {
                return hand;
            }

            if (pick == null || pick.Length < BattleLimits.OpenHandSize)
            {
                if (hand.Count == BattleLimits.OpenHandSize)
                {
                    return hand;
                }

                var first = new Card[BattleLimits.OpenHandSize];
                for (var n = 0; n < first.Length; n++)
                {
                    first[n] = hand[n];
                }

                return first;
            }

            var open = new Card[BattleLimits.OpenHandSize];
            for (var n = 0; n < open.Length; n++)
            {
                var index = pick[n];
                open[n] = index >= 0 && index < hand.Count ? hand[index] : default;
            }

            return open;
        }

        private static int[] DefaultPick(int handSize)
        {
            var n = Math.Min(BattleLimits.OpenHandSize, Math.Max(0, handSize));
            var pick = new int[BattleLimits.OpenHandSize];
            for (var i = 0; i < pick.Length; i++)
            {
                pick[i] = i < n ? i : 0;
            }

            return pick;
        }

        private static int[] PickBest(Card[] hand)
        {
            var flags = new bool[hand.Length];
            HandEvaluator.SelectBestOpen(hand, flags, hand.Length);
            var pick = DefaultPick(hand.Length);
            var n = 0;
            for (var i = 0; i < flags.Length && n < pick.Length; i++)
            {
                if (flags[i])
                {
                    pick[n++] = i;
                }
            }

            return pick;
        }

        private static int[][] EmptyPicked()
        {
            var arr = new int[BattleLimits.RoomSeats][];
            for (var i = 0; i < arr.Length; i++)
            {
                arr[i] = DefaultPick(0);
            }

            return arr;
        }

        private bool IsSeatActive(int index)
        {
            if (index < 0 || index >= BattleLimits.RoomSeats)
            {
                return false;
            }

            if (index >= _seats.Count)
            {
                return _seats.Count == 0;
            }

            return _seats[index].Alive;
        }

        private BattleSnapshot EmptySnapshot()
        {
            return new BattleSnapshot
            {
                Seed = _seed,
                Mode = _mode.Kind,
                Phase = BattlePhase.Idle,
                Seats = _seats,
                Hands = Enumerable.Range(0, BattleLimits.RoomSeats).Select(_ => Array.Empty<Card>()).ToArray(),
                Picked = EmptyPicked(),
                Scores = new HandScore[BattleLimits.RoomSeats],
                Damages = new int[BattleLimits.RoomSeats]
            };
        }

        private HandEvalRules SeatHandRules(int seatId)
        {
            if (seatId < 0 || seatId >= _seats.Count)
            {
                return default;
            }

            var relics = _seats[seatId].RelicIds;
            if (relics == null || relics.Count == 0 || _tables == null)
            {
                return default;
            }

            var colorFlush = false;
            var gappedStraight = false;
            var wildAce = false;
            for (var i = 0; i < relics.Count; i++)
            {
                if (!_tables.TryGetRelic(relics[i], out var relic) || relic.MechanismId == null)
                {
                    continue;
                }

                for (var j = 0; j < relic.MechanismId.Length; j++)
                {
                    if (!_tables.TryGetRelicEntry(relic.MechanismId[j], out var entry))
                    {
                        continue;
                    }

                    if (entry.Type == MechanismType.SpecialFlush)
                    {
                        colorFlush = true;
                    }
                    else if (entry.Type == MechanismType.SpecialStraight)
                    {
                        gappedStraight = true;
                    }
                    else if (entry.Type == MechanismType.WildAce)
                    {
                        wildAce = true;
                    }
                }
            }

            return new HandEvalRules(colorFlush, gappedStraight, wildAce);
        }

        private static bool HasOpenHand(IReadOnlyList<Card> hand)
        {
            if (hand.Count < BattleLimits.OpenHandSize)
            {
                return false;
            }

            for (var i = 0; i < BattleLimits.OpenHandSize; i++)
            {
                if (!hand[i].IsValid)
                {
                    return false;
                }
            }

            return true;
        }

        private static IReadOnlyList<SeatSetup> PadSeats(IReadOnlyList<SeatSetup> seats, BattleModeKind kind)
        {
            var padded = new SeatSetup[BattleLimits.RoomSeats];
            var fillAll = seats.Count == 0;
            for (var i = 0; i < padded.Length; i++)
            {
                if (i < seats.Count)
                {
                    padded[i] = CloneSeat(seats[i], i);
                    continue;
                }

                padded[i] = new SeatSetup
                {
                    SeatId = i,
                    UserId = fillAll ? string.Empty : $"seat-{i}",
                    NickName = string.Empty,
                    IsHuman = kind == BattleModeKind.Pvp || i == 0,
                    Alive = fillAll
                };
            }

            return padded;
        }

        private static SeatSetup CloneSeat(SeatSetup seat, int index)
        {
            return new SeatSetup
            {
                SeatId = seat.SeatId >= 0 ? seat.SeatId : index,
                UserId = seat.UserId,
                NickName = seat.NickName,
                IsHuman = seat.IsHuman,
                Alive = seat.Alive,
                Attack = seat.Attack,
                Hp = seat.Hp,
                MaxHp = seat.MaxHp,
                HeroId = seat.HeroId,
                Talents = CombatBonuses.CloneTalents(seat.Talents),
                RelicIds = CombatBonuses.CloneRelicIds(seat.RelicIds),
                ShopPoolIds = seat.ShopPoolIds,
                HandTypeShowCounts = seat.HandTypeShowCounts,
                RelicSelfDecayMag = seat.RelicSelfDecayMag,
                RelicShopRefreshCounts = seat.RelicShopRefreshCounts,
                RelicWinLoseMag = seat.RelicWinLoseMag,
                ConsumableUsesThisRun = seat.ConsumableUsesThisRun,
                CopiedRelicId = seat.CopiedRelicId,
                RubLeft = seat.RubLeft,
                PeekLeft = seat.PeekLeft,
                ReplaceLeft = seat.ReplaceLeft
            };
        }
    }
}
