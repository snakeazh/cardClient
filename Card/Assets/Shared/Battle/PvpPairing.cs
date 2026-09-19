using System;
using System.Collections.Generic;

#nullable enable

namespace CardShare.Battle
{
    public readonly struct PvpPairSlot
    {
        public PvpPairSlot(int leftSeat, int? rightSeat)
        {
            LeftSeat = leftSeat;
            RightSeat = rightSeat;
        }

        public int LeftSeat { get; }

        public int? RightSeat { get; }

        public bool IsMonster => RightSeat == null;
    }

    public static class PvpPairing
    {
        public static readonly (int A, int B)[][] Rotation =
        {
            new[] { (0, 1), (2, 3) },
            new[] { (0, 2), (1, 3) },
            new[] { (0, 3), (1, 2) }
        };

        public static IReadOnlyList<PvpPairSlot> ForMonster(IReadOnlyList<int> aliveSeats)
        {
            var list = new PvpPairSlot[aliveSeats.Count];
            for (var i = 0; i < aliveSeats.Count; i++)
            {
                list[i] = new PvpPairSlot(aliveSeats[i], null);
            }

            return list;
        }

        public static IReadOnlyList<PvpPairSlot> ForPvp(IReadOnlyList<int> aliveSeats, int cycleIndex)
        {
            if (aliveSeats.Count <= 1)
            {
                return Array.Empty<PvpPairSlot>();
            }

            if (aliveSeats.Count == 2)
            {
                return new[] { new PvpPairSlot(aliveSeats[0], aliveSeats[1]) };
            }

            var rotation = Rotation[Math.Abs(cycleIndex) % Rotation.Length];
            var alive = new HashSet<int>(aliveSeats);
            var slots = new List<PvpPairSlot>(4);
            var used = new HashSet<int>();
            foreach (var pair in rotation)
            {
                var leftAlive = alive.Contains(pair.A);
                var rightAlive = alive.Contains(pair.B);
                if (leftAlive && rightAlive)
                {
                    slots.Add(new PvpPairSlot(pair.A, pair.B));
                    used.Add(pair.A);
                    used.Add(pair.B);
                }
                else if (leftAlive)
                {
                    slots.Add(new PvpPairSlot(pair.A, null));
                    used.Add(pair.A);
                }
                else if (rightAlive)
                {
                    slots.Add(new PvpPairSlot(pair.B, null));
                    used.Add(pair.B);
                }
            }

            for (var i = 0; i < aliveSeats.Count; i++)
            {
                var seat = aliveSeats[i];
                if (!used.Contains(seat))
                {
                    slots.Add(new PvpPairSlot(seat, null));
                }
            }

            return slots;
        }
    }
}
