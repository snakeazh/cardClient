using System.Text.Json;
using CardShare.Contracts;
using CardShare.Domain.Pvp;
using StackExchange.Redis;

namespace CardShare.Infrastructure.Redis;

public sealed class RedisPvpBus : IPvpBus
{
    public const string MessageChannel = "pvp:msg";
    public const string CommandChannel = "pvp:cmd";

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISubscriber _subscriber;

    public RedisPvpBus(IConnectionMultiplexer redis)
    {
        _subscriber = redis.GetSubscriber();
    }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public Task PublishToUserAsync(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
    {
        return Publish(MessageChannel, new PvpBusMessage
        {
            OriginInstanceId = InstanceId,
            UserId = userId,
            Envelope = envelope
        });
    }

    public async Task<bool> PublishCommandAsync(Guid roomId, Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
    {
        await Publish(CommandChannel, new PvpBusMessage
        {
            OriginInstanceId = InstanceId,
            UserId = userId,
            RoomId = roomId,
            Envelope = envelope
        });
        return true;
    }

    public static string Serialize(PvpBusMessage message) => JsonSerializer.Serialize(message, Json);

    public static PvpBusMessage? Deserialize(string payload)
        => JsonSerializer.Deserialize<PvpBusMessage>(payload, Json);

    private Task Publish(string channel, PvpBusMessage message)
        => _subscriber.PublishAsync(RedisChannel.Literal(channel), Serialize(message));
}
