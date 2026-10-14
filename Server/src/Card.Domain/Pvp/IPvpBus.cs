using CardShare.Contracts;

namespace CardShare.Domain.Pvp;

/// <summary>多实例 PvP 消息总线：pvp:msg 投递用户消息，pvp:cmd 转发房主命令。运行时为 Redis 实现。</summary>
public interface IPvpBus
{
    /// <summary>本实例 Id，随消息带回显，订阅方据此跳过自己发的命令。</summary>
    Guid InstanceId { get; }

    Task PublishToUserAsync(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>转发 battle/sync 命令给房主实例。</summary>
    Task<bool> PublishCommandAsync(Guid roomId, Guid userId, WsEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class PvpBusMessage
{
    public Guid OriginInstanceId { get; set; }

    public Guid UserId { get; set; }

    public Guid RoomId { get; set; }

    public WsEnvelope? Envelope { get; set; }
}
