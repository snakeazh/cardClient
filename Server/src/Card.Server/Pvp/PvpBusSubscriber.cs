using CardShare.Contracts;
using CardShare.Domain.Pvp;
using CardShare.Infrastructure.Redis;
using StackExchange.Redis;

namespace CardShare.Server.Pvp;

/// <summary>订阅 pvp:msg / pvp:cmd：用户消息本地投递；battle/sync 命令在房主实例处理。无 Redis（单实例）时不工作。</summary>
public sealed class PvpBusSubscriber : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly PvpConnectionHub _hub;
    private readonly PvpMatchHost _matches;
    private readonly PvpRewardService _rewards;
    private readonly PvpMessageRouter _router;
    private readonly IPvpBus _bus;

    public PvpBusSubscriber(
        IServiceProvider services,
        PvpConnectionHub hub,
        PvpMatchHost matches,
        PvpRewardService rewards,
        PvpMessageRouter router,
        IPvpBus bus)
    {
        _services = services;
        _hub = hub;
        _matches = matches;
        _rewards = rewards;
        _router = router;
        _bus = bus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var redis = _services.GetService<IConnectionMultiplexer>();
        if (redis == null)
        {
            return;
        }

        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisPvpBus.MessageChannel),
            (channel, value) => { _ = OnUserMessageAsync(value); });
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisPvpBus.CommandChannel),
            (channel, value) => { _ = OnCommandAsync(value); });

        var stopped = new TaskCompletionSource();
        await using (stoppingToken.Register(() => stopped.TrySetResult()))
        {
            await stopped.Task;
        }
    }

    private async Task OnUserMessageAsync(RedisValue value)
    {
        try
        {
            var message = RedisPvpBus.Deserialize((string)value!);
            if (message?.Envelope == null)
            {
                return;
            }

            // 目标 socket 在本实例才发（hub 内部判空，不在则丢）。
            await _hub.SendAsync(message.UserId, message.Envelope, CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private async Task OnCommandAsync(RedisValue value)
    {
        try
        {
            var message = RedisPvpBus.Deserialize((string)value!);
            if (message?.Envelope == null || message.OriginInstanceId == _bus.InstanceId)
            {
                return;
            }

            var t = (message.Envelope.T ?? string.Empty).Trim().ToLowerInvariant();
            if (t == WsMessageTypes.Battle)
            {
                await PvpWebSocketHost.ProcessBattleCommandAsync(
                    _router, _matches, _rewards, message.UserId, message.Envelope, CancellationToken.None);
            }
            else if (t == WsMessageTypes.Sync)
            {
                await PvpWebSocketHost.ProcessSyncCommandAsync(
                    _router, _matches, message.UserId, message.Envelope, CancellationToken.None);
            }
        }
        catch (Exception)
        {
        }
    }
}
