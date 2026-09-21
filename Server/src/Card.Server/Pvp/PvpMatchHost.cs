using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;

namespace CardShare.Server.Pvp;

public sealed class PvpMatchHost
{
    public const int DefaultModeId = PvpSchedule.DefaultModeId;

    private const long FinishedRoomTtlMs = 5 * 60 * 1000;

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

    /// <summary>标记玩家断线/重连。match 为 null = 无对局；返回 true = 有状态变化需广播。</summary>
    public bool SetConnected(Guid userId, bool connected, out PvpMatch? match)
    {
        lock (_gate)
        {
            match = null;
            if (!_byUser.TryGetValue(userId, out var found))
            {
                return false;
            }

            var version = found.StateVersion;
            found.SetDisconnected(userId.ToString("N"), !connected);
            match = found;
            return found.StateVersion != version;
        }
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

    /// <summary>扫描所有房间，结算选牌超时的座位。返回有状态变化、需要广播的比赛。</summary>
    public List<PvpMatch> SweepTimeouts()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            var changed = new List<PvpMatch>();
            foreach (var pair in _byRoom)
            {
                if (pair.Value.ApplyTimeouts(now))
                {
                    changed.Add(pair.Value);
                }
            }

            return changed;
        }
    }

    /// <summary>回收结束超过 5 分钟的房间。</summary>
    public void PurgeFinishedRooms(DateTimeOffset now)
    {
        var nowMs = now.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            var stale = new List<PvpMatch>();
            foreach (var pair in _byRoom)
            {
                var match = pair.Value;
                if (match.Phase == PvpMatch.PhaseFinished
                    && match.FinishedUtcMs > 0
                    && nowMs - match.FinishedUtcMs > FinishedRoomTtlMs)
                {
                    stale.Add(match);
                }
            }

            foreach (var match in stale)
            {
                _byRoom.Remove(match.RoomId);
                var users = new List<Guid>();
                foreach (var pair in _byUser)
                {
                    if (ReferenceEquals(pair.Value, match))
                    {
                        users.Add(pair.Key);
                    }
                }

                foreach (var userId in users)
                {
                    _byUser.Remove(userId);
                }
            }
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
