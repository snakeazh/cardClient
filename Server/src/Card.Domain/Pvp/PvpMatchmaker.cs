using CardShare.Contracts;
using CardShare.Domain;

namespace CardShare.Domain.Pvp;

public static class PvpRules
{
    public const int RoomSize = 4;

    /// <summary>排队超时（毫秒）：超过该时长未开房的玩家被踢出队列。</summary>
    public const long QueueTimeoutMs = 60_000;
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

    /// <summary>该玩家是否还在等待队列里。</summary>
    bool IsWaiting(Guid userId);

    /// <summary>踢出排队超过 timeoutMs 的玩家，返回被踢名单；remaining 为踢出后的当前队列。</summary>
    IReadOnlyList<Guid> SweepExpired(long nowUtcMs, long timeoutMs, out IReadOnlyList<PlayerPublic> remaining);
}

public sealed class InMemoryPvpMatchmaker : IPvpMatchmaker
{
    private sealed class QueueEntry
    {
        public PlayerPublic Player { get; init; } = new PlayerPublic();

        public long EnqueuedUtcMs { get; init; }
    }

    private readonly object _gate = new object();
    private readonly List<QueueEntry> _waiting = new List<QueueEntry>();
    private readonly Random _random = new Random();
    private readonly Func<long> _nowUtcMs;

    public InMemoryPvpMatchmaker(Func<long>? nowUtcMs = null)
    {
        _nowUtcMs = nowUtcMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public MatchEvent Enqueue(PlayerPublic player)
    {
        if (player == null || !Guid.TryParse(player.UserId, out var userId))
        {
            throw DomainException.Invalid("Invalid player.");
        }

        lock (_gate)
        {
            RemoveLocked(userId);
            _waiting.Add(new QueueEntry { Player = Clone(player), EnqueuedUtcMs = _nowUtcMs() });
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

            var seated = _waiting.Take(PvpRules.RoomSize).Select(e => Clone(e.Player)).ToArray();
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

    public bool IsWaiting(Guid userId)
    {
        lock (_gate)
        {
            return FindLocked(userId) >= 0;
        }
    }

    public IReadOnlyList<Guid> SweepExpired(long nowUtcMs, long timeoutMs, out IReadOnlyList<PlayerPublic> remaining)
    {
        lock (_gate)
        {
            var expired = new List<Guid>();
            if (timeoutMs > 0)
            {
                for (var i = _waiting.Count - 1; i >= 0; i--)
                {
                    if (nowUtcMs - _waiting[i].EnqueuedUtcMs < timeoutMs)
                    {
                        continue;
                    }

                    if (Guid.TryParse(_waiting[i].Player.UserId, out var id))
                    {
                        expired.Insert(0, id);
                    }

                    _waiting.RemoveAt(i);
                }
            }

            remaining = SnapshotLocked();
            return expired;
        }
    }

    private bool RemoveLocked(Guid userId)
    {
        var index = FindLocked(userId);
        if (index < 0)
        {
            return false;
        }

        _waiting.RemoveAt(index);
        return true;
    }

    private int FindLocked(Guid userId)
    {
        var key = userId.ToString("N");
        var alt = userId.ToString();
        for (var i = 0; i < _waiting.Count; i++)
        {
            if (string.Equals(_waiting[i].Player.UserId, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_waiting[i].Player.UserId, alt, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private IReadOnlyList<PlayerPublic> SnapshotLocked()
        => _waiting.Select(e => Clone(e.Player)).ToArray();

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
            AvatarUrl = player.AvatarUrl,
            IsBot = player.IsBot,
            BotConfigId = player.BotConfigId
        };
    }
}
