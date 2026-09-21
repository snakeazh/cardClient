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

    public IPvpBus Bus => _bus;

    public Task SendToUser(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
        => _hub.Contains(userId)
            ? _hub.SendAsync(userId, envelope, cancellationToken)
            : _bus.PublishToUserAsync(userId, envelope, cancellationToken);
}
