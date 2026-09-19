using System;
using System.Collections.Generic;
using CardShare.Contracts;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>PVP 权威桌：开房后立刻 Deal。客户端只收 DTO，不收内部 Card。</summary>
    public sealed class PvpBattleTable
    {
        private readonly object _gate = new object();

        private PvpBattleTable(Guid roomId, IReadOnlyList<PlayerPublic> players, BattleEngine engine)
        {
            RoomId = roomId;
            Players = players;
            Engine = engine;
        }

        public Guid RoomId { get; }

        public IReadOnlyList<PlayerPublic> Players { get; }

        public BattleEngine Engine { get; }

        public static PvpBattleTable Open(
            Guid roomId,
            int seed,
            IReadOnlyList<PlayerPublic> players,
            IGameTables tables)
        {
            return Open(roomId, seed, players, tables, Array.Empty<SeatSetup>());
        }

        public static PvpBattleTable Open(
            Guid roomId,
            int seed,
            IReadOnlyList<PlayerPublic> players,
            IGameTables tables,
            IReadOnlyList<SeatSetup> combatSeats)
        {
            if (players.Count != BattleLimits.RoomSeats)
            {
                throw new ArgumentException("PVP needs exactly 4 players.", nameof(players));
            }

            var seats = new SeatSetup[BattleLimits.RoomSeats];
            for (var i = 0; i < seats.Length; i++)
            {
                var player = players[i];
                if (i < combatSeats.Count)
                {
                    var combat = combatSeats[i];
                    seats[i] = new SeatSetup
                    {
                        SeatId = i,
                        UserId = string.IsNullOrEmpty(combat.UserId) ? player.UserId : combat.UserId,
                        NickName = string.IsNullOrEmpty(combat.NickName) ? player.NickName : combat.NickName,
                        IsHuman = true,
                        Alive = combat.Alive,
                        Attack = combat.Attack,
                        Hp = combat.Hp,
                        MaxHp = combat.MaxHp,
                        HeroId = combat.HeroId,
                        Talents = CombatBonuses.CloneTalents(combat.Talents),
                        RelicIds = CombatBonuses.CloneRelicIds(combat.RelicIds)
                    };
                    continue;
                }

                seats[i] = new SeatSetup
                {
                    SeatId = i,
                    UserId = player.UserId,
                    NickName = player.NickName,
                    IsHuman = true,
                    Alive = true
                };
            }

            var engine = new BattleEngine(new PvpMode(), seed, seats, tables);
            engine.Apply(new BattleCommand { Type = BattleCommandType.Deal });
            return new PvpBattleTable(roomId, players, engine);
        }

        public int SeatOf(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return -1;
            }

            for (var i = 0; i < Players.Count; i++)
            {
                if (SameUser(Players[i].UserId, userId))
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool SameUser(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Guid.TryParse(a, out var left) && Guid.TryParse(b, out var right) && left == right;
        }

        public BattleSnapshot Showdown()
        {
            lock (_gate)
            {
                return Engine.Apply(new BattleCommand { Type = BattleCommandType.Showdown });
            }
        }

        public BattleStateDto ViewFor(string userId)
        {
            lock (_gate)
            {
                return BattleWire.ForViewer(Engine.Snapshot, SeatOf(userId), RoomId.ToString("N"), Players);
            }
        }
    }
}
