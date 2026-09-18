using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;

namespace CardShare.Server.Pvp;

public sealed class PvpMatchHost
{
    public const int DefaultModeId = PvpSchedule.DefaultModeId;

    private readonly IGameTables _tables;
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, PvpMatch> _byUser = new Dictionary<Guid, PvpMatch>();
    private readonly Dictionary<Guid, PvpMatch> _byRoom = new Dictionary<Guid, PvpMatch>();

    public PvpMatchHost(IGameTables tables)
    {
        _tables = tables;
    }

    public PvpMatch Open(PvpRoom room, IReadOnlyList<SeatSetup> combatSeats, int modeId = DefaultModeId)
    {
        lock (_gate)
        {
            if (_byRoom.TryGetValue(room.RoomId, out var existing))
            {
                return existing;
            }

            var match = PvpMatch.Open(room.RoomId, room.Seed, room.Players, _tables, combatSeats, modeId);
            _byRoom[room.RoomId] = match;
            foreach (var player in room.Players)
            {
                if (player.IsBot || !Guid.TryParse(player.UserId, out var userId))
                {
                    continue;
                }

                _byUser[userId] = match;
            }

            return match;
        }
    }

    public bool TryGet(Guid userId, out PvpMatch match)
    {
        lock (_gate)
        {
            return _byUser.TryGetValue(userId, out match!);
        }
    }

    public void Showdown(Guid userId)
    {
        Act(userId, "showdown", 0, Array.Empty<int>());
    }

    public void Act(Guid userId, string action, int index, int[] indexes)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var match))
            {
                throw DomainException.Invalid("Not in a battle.");
            }

            try
            {
                match.Act(userId.ToString("N"), action, index, indexes);
            }
            catch (InvalidOperationException ex)
            {
                throw DomainException.Invalid(ex.Message);
            }
        }
    }

    public bool AdvanceIfReady(Guid userId)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var match))
            {
                return false;
            }

            var round = match.Round;
            var phase = match.Phase;
            match.AdvanceIfReady();
            return match.Round != round || match.Phase != phase;
        }
    }

    public void Leave(Guid userId)
    {
        lock (_gate)
        {
            if (!_byUser.Remove(userId, out var match))
            {
                return;
            }

            var stillSeated = false;
            foreach (var pair in _byUser)
            {
                if (ReferenceEquals(pair.Value, match))
                {
                    stillSeated = true;
                    break;
                }
            }

            if (!stillSeated)
            {
                _byRoom.Remove(match.RoomId);
            }
        }
    }
}
