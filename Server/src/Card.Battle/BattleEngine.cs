using System;
using System.Collections.Generic;
using System.Linq;
using CardShare.Contracts;

namespace CardShare.Battle;

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

    public BattleEngine(BattleMode mode, int seed, IReadOnlyList<SeatSetup> seats, IGameTables tables)
    {
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _seed = seed;
        _seats = PadSeats(seats, mode.Kind);
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _snapshot = EmptySnapshot();
    }

    public BattleSnapshot Snapshot => _snapshot;

    public BattleSnapshot Apply(BattleCommand cmd)
    {
        if (cmd == null)
        {
            return _snapshot;
        }

        var prev = HandEvaluator.Tables;
        HandEvaluator.Tables = _tables;
        try
        {
            if (cmd.Type == BattleCommandType.Deal)
            {
                Deal();
            }
            else if (cmd.Type == BattleCommandType.Showdown || cmd.Type == BattleCommandType.Open)
            {
                if (_snapshot.Phase == BattlePhase.Idle)
                {
                    Deal();
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
            Deal();
        }

        return _deck != null ? _deck.Draw() : default;
    }

    private void Deal()
    {
        _deck = new Deck(_seed);
        var hands = new Card[BattleLimits.RoomSeats][];
        for (var i = 0; i < BattleLimits.RoomSeats; i++)
        {
            var active = IsSeatActive(i);
            var hand = new Card[BattleLimits.OpenHandSize];
            if (active)
            {
                for (var c = 0; c < hand.Length; c++)
                {
                    hand[c] = _deck.Draw();
                }
            }

            hands[i] = hand;
        }

        _snapshot = new BattleSnapshot
        {
            Seed = _seed,
            Mode = _mode.Kind,
            Phase = BattlePhase.Dealt,
            Seats = _seats,
            Hands = hands,
            Scores = new HandScore[BattleLimits.RoomSeats],
            Damages = new int[BattleLimits.RoomSeats],
            Events = new[] { new BattleEvent { Type = BattleEventType.Dealt, Message = "deal" } }
        };
    }

    private void ScoreHands()
    {
        var hands = _snapshot.Hands;
        var scores = new HandScore[hands.Length];
        HandScore? best = null;
        var winners = new List<int>();
        for (var i = 0; i < hands.Length; i++)
        {
            if (!IsSeatActive(i) || !HasOpenHand(hands[i]))
            {
                continue;
            }

            scores[i] = HandEvaluator.Evaluate(hands[i]);
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
            if (!IsSeatActive(i) || !HasOpenHand(hands[i]))
            {
                continue;
            }

            var seat = i < _seats.Count ? _seats[i] : null;
            CombatDamageInput input;
            if (seat != null && seat.IsHuman)
            {
                input = CombatBonuses.BuildPlayerInput(
                    _tables,
                    seat,
                    scores[i],
                    new CombatSituation
                    {
                        FirstShow = firstShow,
                        AliveOpponents = Math.Max(0, alive - 1),
                        AttackerHp = seat.Hp,
                        AttackerMaxHp = seat.MaxHp,
                        DefenderIsPlayer = isPvp,
                        DefenderIsBoss = false,
                        DefenderHp = 0
                    });
            }
            else
            {
                input = new CombatDamageInput
                {
                    IsPlayer = false,
                    Attack = seat != null && seat.Attack > 0 ? seat.Attack : 1,
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
            Scores = scores,
            Winners = winners,
            Damages = damages,
            Events = new[] { new BattleEvent { Type = BattleEventType.Compared, Message = "showdown" } }
        };
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
            Scores = new HandScore[BattleLimits.RoomSeats],
            Damages = new int[BattleLimits.RoomSeats]
        };
    }

    private static bool HasOpenHand(IReadOnlyList<Card> hand)
    {
        if (hand == null || hand.Count < BattleLimits.OpenHandSize)
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

    private static IReadOnlyList<SeatSetup> PadSeats(IReadOnlyList<SeatSetup>? seats, BattleModeKind kind)
    {
        var padded = new SeatSetup[BattleLimits.RoomSeats];
        var source = seats ?? Array.Empty<SeatSetup>();
        var fillAll = source.Count == 0;
        for (var i = 0; i < padded.Length; i++)
        {
            if (i < source.Count && source[i] != null)
            {
                padded[i] = CloneSeat(source[i], i);
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
            UserId = seat.UserId ?? string.Empty,
            NickName = seat.NickName ?? string.Empty,
            IsHuman = seat.IsHuman,
            Alive = seat.Alive,
            Attack = seat.Attack,
            Hp = seat.Hp,
            MaxHp = seat.MaxHp,
            HeroId = seat.HeroId,
            Talents = CombatBonuses.CloneTalents(seat.Talents)
        };
    }
}
