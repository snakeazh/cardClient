using CardShare.Contracts;
using CardShare.Domain;

namespace CardShare.Domain.Pvp;

public static class PvpRules
{
    public const int RoomSize = 4;
}

public sealed class PvpRoom
{
    public Guid RoomId { get; init; }

    public int Seed { get; init; }

    public IReadOnlyList<PlayerPublic> Players { get; init; } = Array.Empty<PlayerPublic>();
}

public sealed class MatchEvent
{
    public bool RoomOpened { get; init; }

    public PvpRoom? Room { get; init; }

    public IReadOnlyList<PlayerPublic> Players { get; init; } = Array.Empty<PlayerPublic>();

    public IReadOnlyList<Guid> Recipients { get; init; } = Array.Empty<Guid>();

    public Guid? Joiner { get; init; }
}

public interface IPvpMatchmaker
{
    MatchEvent Enqueue(PlayerPublic player);

    MatchEvent? Cancel(Guid userId);

    void Leave(Guid userId);
}

public sealed class InMemoryPvpMatchmaker : IPvpMatchmaker
{
    private readonly object _gate = new object();
    private readonly List<PlayerPublic> _waiting = new List<PlayerPublic>();
    private readonly Random _random = new Random();

    public MatchEvent Enqueue(PlayerPublic player)
    {
        if (player == null || !Guid.TryParse(player.UserId, out var userId))
        {
            throw DomainException.Invalid("Invalid player.");
        }

        lock (_gate)
        {
            RemoveLocked(userId);
            _waiting.Add(Clone(player));
            if (_waiting.Count < PvpRules.RoomSize)
            {
                var snapshot = SnapshotLocked();
                return new MatchEvent
                {
                    RoomOpened = false,
                    Players = snapshot,
                    Recipients = Ids(snapshot),
                    Joiner = userId
                };
            }

            var seated = _waiting.Take(PvpRules.RoomSize).Select(Clone).ToArray();
            _waiting.RemoveRange(0, PvpRules.RoomSize);
            var room = new PvpRoom
            {
                RoomId = Guid.NewGuid(),
                Seed = _random.Next(),
                Players = seated
            };
            return new MatchEvent
            {
                RoomOpened = true,
                Room = room,
                Players = seated,
                Recipients = Ids(seated)
            };
        }
    }

    public MatchEvent? Cancel(Guid userId)
    {
        lock (_gate)
        {
            if (!RemoveLocked(userId))
            {
                return null;
            }

            var snapshot = SnapshotLocked();
            return new MatchEvent
            {
                RoomOpened = false,
                Players = snapshot,
                Recipients = Ids(snapshot)
            };
        }
    }

    public void Leave(Guid userId) => Cancel(userId);

    private bool RemoveLocked(Guid userId)
    {
        var key = userId.ToString("N");
        var alt = userId.ToString();
        for (var i = 0; i < _waiting.Count; i++)
        {
            if (string.Equals(_waiting[i].UserId, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_waiting[i].UserId, alt, StringComparison.OrdinalIgnoreCase))
            {
                _waiting.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<PlayerPublic> SnapshotLocked()
        => _waiting.Select(Clone).ToArray();

    private static Guid[] Ids(IReadOnlyList<PlayerPublic> players)
    {
        var ids = new List<Guid>(players.Count);
        foreach (var player in players)
        {
            if (Guid.TryParse(player.UserId, out var id))
            {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    private static PlayerPublic Clone(PlayerPublic player)
    {
        return new PlayerPublic
        {
            UserId = player.UserId,
            NickName = player.NickName,
            AvatarUrl = player.AvatarUrl
        };
    }
}
