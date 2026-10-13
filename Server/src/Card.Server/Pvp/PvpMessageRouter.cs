using CardShare.Contracts;
using CardShare.Domain.Pvp;

namespace CardShare.Server.Pvp;

/// <summary>PvP 发送收口：本地有 socket 直发，否则经 IPvpBus 投递到目标所在实例。单实例（NullPvpBus）下与直连等价。</summary>
public sealed class PvpMessageRouter
{
    private readonly PvpConnectionHub _hub;
    private readonly IPvpBus _bus;

    public PvpMessageRouter(PvpConnectionHub hub, IPvpBus bus)
    {
        _hub = hub;
        _bus = bus;
    }

    public Task SendToUser(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
        => _hub.Contains(userId)
            ? _hub.SendAsync(userId, envelope, cancellationToken)
            : _bus.PublishToUserAsync(userId, envelope, cancellationToken);

    /// <summary>转发 battle/sync 命令给房主实例。返回 false = 无总线（单实例），调用方按本地未命中处理。</summary>
    public Task<bool> PublishCommandAsync(Guid roomId, Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
        => _bus.PublishCommandAsync(roomId, userId, envelope, cancellationToken);
}
