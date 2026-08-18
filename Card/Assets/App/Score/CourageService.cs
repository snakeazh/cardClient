using System.Collections.Generic;

namespace App.Score
{
    /// <summary>
    /// In-memory per-seat courage (chips). Recalculated from that seat's HP at stage start.
    /// </summary>
    public sealed class CourageService : ICourageService
    {
        private sealed class SeatCourage
        {
            public int Amount;
            public int Stake;
        }

        private readonly Dictionary<int, SeatCourage> _seats = new Dictionary<int, SeatCourage>();

        public int GetAmount(int seatId)
        {
            return TryGet(seatId, out var seat) ? seat.Amount : 0;
        }

        public int GetStake(int seatId)
        {
            return TryGet(seatId, out var seat) ? seat.Stake : 0;
        }

        public void BeginStage(int seatId, int hp)
        {
            _seats[seatId] = new SeatCourage
            {
                Amount = ScoreBalance.HpToCourage(hp),
                Stake = 0
            };
        }

        public void BeginRound(int seatId)
        {
            if (TryGet(seatId, out var seat))
            {
                seat.Stake = 0;
            }
        }

        public bool TryBet(int seatId, int amount)
        {
            if (amount <= 0 || !TryGet(seatId, out var seat) || seat.Amount < amount)
            {
                return false;
            }

            seat.Amount -= amount;
            seat.Stake += amount;
            return true;
        }

        public void Add(int seatId, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Ensure(seatId).Amount += amount;
        }

        public int Win(int seatId, int chipsWon)
        {
            var seat = Ensure(seatId);
            seat.Stake = 0;
            if (chipsWon <= 0)
            {
                return 0;
            }

            seat.Amount += chipsWon;
            return chipsWon;
        }

        public int Lose(int seatId)
        {
            if (!TryGet(seatId, out var seat))
            {
                return 0;
            }

            var lost = seat.Stake;
            seat.Stake = 0;
            return lost;
        }

        private SeatCourage Ensure(int seatId)
        {
            if (_seats.TryGetValue(seatId, out var seat))
            {
                return seat;
            }

            seat = new SeatCourage();
            _seats[seatId] = seat;
            return seat;
        }

        private bool TryGet(int seatId, out SeatCourage seat)
        {
            return _seats.TryGetValue(seatId, out seat);
        }
    }
}
