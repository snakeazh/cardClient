using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;

namespace CardShare.Server.Pvp;

public sealed class PvpBattleHost
{
    private readonly IGameTables _tables;
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, PvpBattleTable> _byUser = new Dictionary<Guid, PvpBattleTable>();
    private readonly Dictionary<Guid, PvpBattleTable> _byRoom = new Dictionary<Guid, PvpBattleTable>();

    public PvpBattleHost(IGameTables tables)
    {
        _tables = tables;
    }

    public PvpBattleTable Open(PvpRoom room, IReadOnlyList<SeatSetup> combatSeats)
    {
        lock (_gate)
        {
            if (_byRoom.TryGetValue(room.RoomId, out var existing))
            {
                return existing;
            }

            var table = PvpBattleTable.Open(room.RoomId, room.Seed, room.Players, _tables, combatSeats);
            _byRoom[room.RoomId] = table;
            foreach (var player in room.Players)
            {
                if (Guid.TryParse(player.UserId, out var userId))
                {
                    _byUser[userId] = table;
                }
            }

            return table;
        }
    }

    public bool TryGet(Guid userId, out PvpBattleTable table)
    {
        lock (_gate)
        {
            return _byUser.TryGetValue(userId, out table!);
        }
    }

    public BattleSnapshot Showdown(Guid userId)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var table))
            {
                throw DomainException.Invalid("Not in a battle.");
            }

            return table.Showdown();
        }
    }

    public void Leave(Guid userId)
    {
        lock (_gate)
        {
            if (!_byUser.Remove(userId, out var table))
            {
                return;
            }

            var stillSeated = false;
            foreach (var pair in _byUser)
            {
                if (ReferenceEquals(pair.Value, table))
                {
                    stillSeated = true;
                    break;
                }
            }

            if (!stillSeated)
            {
                _byRoom.Remove(table.RoomId);
            }
        }
    }
}
