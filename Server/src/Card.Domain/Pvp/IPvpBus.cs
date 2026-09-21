using CardShare.Contracts;

namespace CardShare.Domain.Pvp;

/// <summary>多实例 PvP 消息总线：pvp:msg 投递用户消息，pvp:cmd 转发房主命令。单实例（NullPvpBus）全部为 no-op。</summary>
public interface IPvpBus
{
    /// <summary>本实例 Id，随消息带回显，订阅方据此跳过自己发的命令。</summary>
    Guid InstanceId { get; }

    Task PublishToUserAsync(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>转发 battle/sync 命令给房主实例。返回 false = 无总线（单实例），调用方按本地未命中处理。</summary>
    Task<bool> PublishCommandAsync(Guid roomId, Guid userId, WsEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class PvpBusMessage
{
    public Guid OriginInstanceId { get; set; }

    public Guid UserId { get; set; }

    public Guid RoomId { get; set; }

    public WsEnvelope? Envelope { get; set; }
}

public sealed class NullPvpBus : IPvpBus
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    public Task PublishToUserAsync(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<bool> PublishCommandAsync(Guid roomId, Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
