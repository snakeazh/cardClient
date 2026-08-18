using System.Collections.Generic;

namespace App.Score
{
    /// <summary>
    /// In-memory per-seat HP. Not persisted. Betting does not change HP.
    /// </summary>
    public sealed class HpService : IHpService
    {
        private sealed class SeatHp
        {
            public int Hp;
            public int MaxHp;
        }

        private readonly Dictionary<int, SeatHp> _seats = new Dictionary<int, SeatHp>();

        public int GetHp(int seatId)
        {
            return TryGet(seatId, out var seat) ? seat.Hp : 0;
        }

        public int GetMaxHp(int seatId)
        {
            return TryGet(seatId, out var seat) ? seat.MaxHp : 0;
        }

        public bool IsDead(int seatId)
        {
            return GetHp(seatId) <= 0;
        }

        public void BeginStage(int seatId, int maxHp, int hp = -1)
        {
            maxHp = maxHp < 0 ? 0 : maxHp;
            if (hp < 0)
            {
                hp = maxHp;
            }

            _seats[seatId] = new SeatHp
            {
                MaxHp = maxHp,
                Hp = hp < 0 ? 0 : hp
            };
        }

        public void Heal(int seatId, int amount)
        {
            if (amount <= 0 || !TryGet(seatId, out var seat))
            {
                return;
            }

            seat.Hp += amount;
        }

        public void Damage(int seatId, int amount)
        {
            if (amount <= 0 || !TryGet(seatId, out var seat))
            {
                return;
            }

            seat.Hp -= amount;
            if (seat.Hp < 0)
            {
                seat.Hp = 0;
            }
        }

        public void Revive(int seatId)
        {
            if (!TryGet(seatId, out var seat))
            {
                return;
            }

            seat.Hp = seat.MaxHp;
        }

        private bool TryGet(int seatId, out SeatHp seat)
        {
            return _seats.TryGetValue(seatId, out seat);
        }
    }
}
