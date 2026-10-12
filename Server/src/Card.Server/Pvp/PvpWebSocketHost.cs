using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;
using CardShare.Infrastructure;

namespace CardShare.Server.Pvp;

public sealed class PvpConnectionHub
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, WebSocket> _sockets =
        new System.Collections.Concurrent.ConcurrentDictionary<Guid, WebSocket>();

    public void Add(Guid userId, WebSocket socket) => _sockets[userId] = socket;

    public void Remove(Guid userId)
    {
        _sockets.TryRemove(userId, out _);
    }

    public bool Contains(Guid userId)
        => _sockets.TryGetValue(userId, out var socket) && socket.State == WebSocketState.Open;

    public async Task SendAsync(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_sockets.TryGetValue(userId, out var socket) && socket.State == WebSocketState.Open)
        {
            await PvpWebSocketHost.SendAsync(socket, envelope, cancellationToken);
        }
    }
}

/// <summary>WS 连接层：收发字节 → JSON 反序列化 → auth → 分发。业务处理与广播在 <see cref="PvpCommandDispatcher"/>。</summary>
public static class PvpWebSocketHost
{
    public static void MapPvpWebSocket(this WebApplication app)
    {
        app.Map("/v1/pvp/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var tokens = context.RequestServices.GetRequiredService<ITokenService>();
            var matchmaker = context.RequestServices.GetRequiredService<IPvpMatchmaker>();
            var hub = context.RequestServices.GetRequiredService<PvpConnectionHub>();
            var matches = context.RequestServices.GetRequiredService<PvpMatchHost>();
            var router = context.RequestServices.GetRequiredService<PvpMessageRouter>();
            var dispatcher = context.RequestServices.GetRequiredService<PvpCommandDispatcher>();
            var scopes = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
            await HandleAsync(socket, tokens, matchmaker, hub, matches, router, dispatcher, scopes, context.RequestAborted);
        });
    }

    internal static Task SendAsync(WebSocket socket, WsEnvelope envelope, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, PvpJson.Options));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task HandleAsync(
        WebSocket socket,
        ITokenService tokens,
        IPvpMatchmaker matchmaker,
        PvpConnectionHub hub,
        PvpMatchHost matches,
        PvpMessageRouter router,
        PvpCommandDispatcher dispatcher,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        Guid? userId = null;
        await SendAsync(socket, new WsEnvelope { T = WsMessageTypes.Hello, Seq = 0 }, cancellationToken);
        var buffer = new byte[8 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                WsEnvelope? incoming;
                try
                {
                    incoming = JsonSerializer.Deserialize<WsEnvelope>(json, PvpJson.Options);
                }
                catch (JsonException)
                {
                    await SendError(socket, 0, ErrorCodes.InvalidRequest, "Invalid JSON.", cancellationToken);
                    continue;
                }

                if (incoming == null || string.IsNullOrWhiteSpace(incoming.T))
                {
                    continue;
                }

                var t = incoming.T.Trim().ToLowerInvariant();
                if (t == WsMessageTypes.Ping)
                {
                    await SendAsync(socket, new WsEnvelope { T = WsMessageTypes.Pong, Seq = incoming.Seq }, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Auth)
                {
                    userId = await AuthAsync(socket, incoming, tokens, hub, cancellationToken);
                    continue;
                }

                if (userId == null)
                {
                    await SendError(socket, incoming.Seq, ErrorCodes.Unauthorized, "Auth first.", cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Queue)
                {
                    var snapshot = await LoadPublicAsync(scopes, userId.Value, cancellationToken);
                    if (snapshot == null)
                    {
                        await SendError(socket, incoming.Seq, ErrorCodes.Unauthorized, "Player not found.", cancellationToken);
                        continue;
                    }

                    var evt = matchmaker.Enqueue(snapshot);
                    await dispatcher.BroadcastRoomEventAsync(evt, userId.Value, incoming.Seq, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Battle)
                {
                    await HandleBattleAsync(socket, router, matches, dispatcher, userId.Value, incoming, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Sync)
                {
                    await HandleSyncAsync(socket, router, matches, dispatcher, userId.Value, incoming, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Cancel)
                {
                    // 只退排队，不动对局。
                    var evt = matchmaker.Cancel(userId.Value);
                    await SendAsync(socket, new WsEnvelope { T = t, Seq = incoming.Seq }, cancellationToken);
                    if (evt != null)
                    {
                        await dispatcher.BroadcastRosterAsync(
                            evt.Recipients,
                            WsMessageTypes.QueueUpdate,
                            evt.Players,
                            0,
                            cancellationToken);
                    }

                    continue;
                }

                if (t == WsMessageTypes.Leave)
                {
                    await HandleLeaveAsync(socket, router, matches, dispatcher, matchmaker, userId.Value, incoming, cancellationToken);
                    continue;
                }
            }
        }
        finally
        {
            if (userId != null)
            {
                // WS 断开 = 代管，不退赛：置断线标志，有变化（含自动锁定结算）则广播。
                if (matches.SetConnected(userId.Value, false, out var left) && left != null)
                {
                    await dispatcher.BroadcastMatchAsync(left, 0, cancellationToken);
                }

                var evt = matchmaker.Cancel(userId.Value);
                hub.Remove(userId.Value);
                if (evt != null)
                {
                    await dispatcher.BroadcastRosterAsync(
                        evt.Recipients,
                        WsMessageTypes.QueueUpdate,
                        evt.Players,
                        0,
                        cancellationToken);
                }
            }
        }
    }

    /// <summary>本实例收到 battle：本地是对局房主则直接处理，否则经总线转发房主（无总线时按本地未命中回错）。</summary>
    private static async Task HandleBattleAsync(
        WebSocket socket,
        PvpMessageRouter router,
        PvpMatchHost matches,
        PvpCommandDispatcher dispatcher,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (!matches.TryGet(userId, out _))
        {
            if (!await router.PublishCommandAsync(Guid.Empty, userId, incoming, cancellationToken))
            {
                await SendError(socket, incoming.Seq, ErrorCodes.InvalidRequest, "Not in a battle.", cancellationToken);
            }

            return;
        }

        await dispatcher.ProcessBattleAsync(userId, incoming, cancellationToken);
    }

    /// <summary>本实例收到 leave：本地有对局则直接判负淘汰，否则经总线转发房主（无总线 = 纯排队取消）；随后 ack + 退队列。</summary>
    private static async Task HandleLeaveAsync(
        WebSocket socket,
        PvpMessageRouter router,
        PvpMatchHost matches,
        PvpCommandDispatcher dispatcher,
        IPvpMatchmaker matchmaker,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (matches.TryGet(userId, out _))
        {
            await dispatcher.ProcessLeaveAsync(userId, incoming, cancellationToken);
        }
        else
        {
            await router.PublishCommandAsync(Guid.Empty, userId, incoming, cancellationToken);
        }

        var evt = matchmaker.Cancel(userId);
        await SendAsync(socket, new WsEnvelope { T = WsMessageTypes.Leave, Seq = incoming.Seq }, cancellationToken);
        if (evt != null)
        {
            await dispatcher.BroadcastRosterAsync(
                evt.Recipients,
                WsMessageTypes.QueueUpdate,
                evt.Players,
                0,
                cancellationToken);
        }
    }

    /// <summary>本实例收到 sync：本地是房主则直接处理，否则经总线转发房主（无总线时回 not_in_battle）。</summary>
    private static async Task HandleSyncAsync(
        WebSocket socket,
        PvpMessageRouter router,
        PvpMatchHost matches,
        PvpCommandDispatcher dispatcher,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (!matches.TryGet(userId, out _))
        {
            if (!await router.PublishCommandAsync(ReadSyncRoomId(incoming.Payload), userId, incoming, cancellationToken))
            {
                await SendError(socket, incoming.Seq, ErrorCodes.NotInBattle, "Not in a battle.", cancellationToken);
            }

            return;
        }

        await dispatcher.ProcessSyncAsync(userId, incoming, cancellationToken);
    }

    private static async Task<PlayerPublic?> LoadPublicAsync(
        IServiceScopeFactory scopes,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var profile = await players.GetAsync(userId, cancellationToken);
        return profile == null ? null : ProfileMapper.ToPublic(profile);
    }

    private static Guid ReadSyncRoomId(object? payload)
    {
        if (payload is JsonElement element
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("roomId", out var roomId)
            && Guid.TryParse(roomId.GetString(), out var id))
        {
            return id;
        }

        return Guid.Empty;
    }

    private static async Task<Guid?> AuthAsync(
        WebSocket socket,
        WsEnvelope incoming,
        ITokenService tokens,
        PvpConnectionHub hub,
        CancellationToken cancellationToken)
    {
        var token = (string?)null;
        if (incoming.Payload is JsonElement element && element.TryGetProperty("accessToken", out var at))
        {
            token = at.GetString();
        }
        else if (incoming.Payload != null)
        {
            var payload = JsonSerializer.Deserialize<WsAuthPayload>(incoming.Payload.ToString() ?? "{}", PvpJson.Options);
            token = payload?.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            await SendError(socket, incoming.Seq, ErrorCodes.Unauthorized, "accessToken required.", cancellationToken);
            return null;
        }

        var userId = await tokens.ResolveAccessTokenAsync(token, cancellationToken);
        if (userId == null)
        {
            await SendError(socket, incoming.Seq, ErrorCodes.Unauthorized, "Invalid access token.", cancellationToken);
            return null;
        }

        hub.Add(userId.Value, socket);
        await SendAsync(socket, new WsEnvelope
        {
            T = WsMessageTypes.Authed,
            Seq = incoming.Seq,
            Payload = new { userId = userId.Value.ToString("N") }
        }, cancellationToken);
        return userId;
    }

    private static Task SendError(WebSocket socket, long seq, string code, string message, CancellationToken cancellationToken)
    {
        return SendAsync(socket, new WsEnvelope
        {
            T = WsMessageTypes.Error,
            Seq = seq,
            Payload = new ApiError { Code = code, Message = message }
        }, cancellationToken);
    }
}
