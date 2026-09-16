using System;
using System.Collections.Generic;
using App.Config;
using CardShare.Battle;
using CardShare.Contracts;
using SharedCard = CardShare.Battle.Card;

namespace App.Game
{
    /// <summary>主线发牌走共享 <see cref="PveLocalSession"/>，再补满 4～5 张。引导发牌不走这里。</summary>
    public static class SharedBattleBridge
    {
        public static PveLocalSession TryStart(
            IGameTables tables,
            int seed,
            SeatState player,
            SeatState[] enemies,
            bool skipForGuide)
        {
            if (tables == null || skipForGuide || player == null || enemies == null)
            {
                return null;
            }

            var seats = new SeatSetup[1 + enemies.Length];
            seats[0] = ToSetup(0, PveLocalSession.PlayerUserId, player, true);
            for (var i = 0; i < enemies.Length; i++)
            {
                seats[i + 1] = ToSetup(i + 1, "pve-ai-" + (i + 1), enemies[i], false);
            }

            return PveLocalSession.Create(tables, seed, seats);
        }

        public static bool TryDeal(
            PveLocalSession session,
            IEnumerable<SeatState> seats,
            Func<SeatState, int> cardsDealt)
        {
            if (session == null || seats == null || cardsDealt == null)
            {
                return false;
            }

            var snap = session.Deal();
            var index = 0;
            foreach (var seat in seats)
            {
                if (seat == null)
                {
                    index++;
                    continue;
                }

                if (!seat.IsPlayer && !seat.Alive)
                {
                    index++;
                    continue;
                }

                var count = cardsDealt(seat);
                var shared = index < snap.Hands.Length ? snap.Hands[index] : Array.Empty<SharedCard>();
                for (var i = 0; i < seat.Hand.Length; i++)
                {
                    if (i >= count)
                    {
                        seat.Hand[i] = default;
                        continue;
                    }

                    if (i < shared.Count && shared[i].IsValid)
                    {
                        seat.Hand[i] = ToUnity(shared[i]);
                        continue;
                    }

                    seat.Hand[i] = ToUnity(session.DrawExtra());
                }

                index++;
            }

            return true;
        }

        public static Card ToUnity(SharedCard card)
        {
            if (!card.IsValid)
            {
                return default;
            }

            return new Card((Suit)(int)card.Suit, (Rank)(int)card.Rank);
        }

        private static SeatSetup ToSetup(int seatId, string userId, SeatState seat, bool human)
        {
            return new SeatSetup
            {
                SeatId = seatId,
                UserId = userId,
                NickName = seat != null ? seat.Name : string.Empty,
                IsHuman = human,
                Alive = seat != null && (human || seat.Alive),
                Attack = seat != null ? seat.Attack : 0,
                Hp = seat != null ? seat.Hp : 0,
                MaxHp = seat != null ? seat.MaxHp : 0
            };
        }
    }
}
