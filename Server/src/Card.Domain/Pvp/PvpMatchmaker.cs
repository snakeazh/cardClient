using CardShare.Contracts;

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
