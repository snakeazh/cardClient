using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;

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

    public async Task SendAsync(Guid userId, WsEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_sockets.TryGetValue(userId, out var socket) && socket.State == WebSocketState.Open)
        {
            await PvpWebSocketHost.SendAsync(socket, envelope, cancellationToken);
        }
    }
}

public static class PvpWebSocketHost
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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
            var battles = context.RequestServices.GetRequiredService<PvpBattleHost>();
            var scopes = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
            await HandleAsync(socket, tokens, matchmaker, hub, battles, scopes, context.RequestAborted);
        });
    }

    internal static Task SendAsync(WebSocket socket, WsEnvelope envelope, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, Json));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task HandleAsync(
        WebSocket socket,
        ITokenService tokens,
        IPvpMatchmaker matchmaker,
        PvpConnectionHub hub,
        PvpBattleHost battles,
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
                    incoming = JsonSerializer.Deserialize<WsEnvelope>(json, Json);
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
                    await BroadcastAsync(hub, battles, evt, userId.Value, incoming.Seq, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Battle)
                {
                    await HandleBattleAsync(socket, hub, battles, userId.Value, incoming, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Cancel || t == WsMessageTypes.Leave)
                {
                    battles.Leave(userId.Value);
                    var evt = matchmaker.Cancel(userId.Value);
                    await SendAsync(socket, new WsEnvelope { T = t, Seq = incoming.Seq }, cancellationToken);
                    if (evt != null)
                    {
                        await BroadcastRosterAsync(
                            hub,
                            evt.Recipients,
                            WsMessageTypes.QueueUpdate,
                            evt.Players,
                            0,
                            cancellationToken);
                    }
                }
            }
        }
        finally
        {
            if (userId != null)
            {
                battles.Leave(userId.Value);
                var evt = matchmaker.Cancel(userId.Value);
                hub.Remove(userId.Value);
                if (evt != null)
                {
                    await BroadcastRosterAsync(
                        hub,
                        evt.Recipients,
                        WsMessageTypes.QueueUpdate,
                        evt.Players,
                        0,
                        cancellationToken);
                }
            }
        }
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

    private static async Task HandleBattleAsync(
        WebSocket socket,
        PvpConnectionHub hub,
        PvpBattleHost battles,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (!battles.TryGet(userId, out var table))
        {
            await SendError(socket, incoming.Seq, ErrorCodes.InvalidRequest, "Not in a battle.", cancellationToken);
            return;
        }

        var action = ReadAction(incoming.Payload);
        if (action != "showdown" && action != "open")
        {
            await SendError(socket, incoming.Seq, ErrorCodes.InvalidRequest, "Unknown battle action.", cancellationToken);
            return;
        }

        table.Showdown();
        await BroadcastBattleAsync(hub, table, incoming.Seq, cancellationToken);
    }

    private static async Task BroadcastBattleAsync(
        PvpConnectionHub hub,
        CardShare.Battle.PvpBattleTable table,
        long seq,
        CancellationToken cancellationToken)
    {
        foreach (var player in table.Players)
        {
            if (!Guid.TryParse(player.UserId, out var id))
            {
                continue;
            }

            await hub.SendAsync(id, new WsEnvelope
            {
                T = WsMessageTypes.BattleUpdate,
                Seq = seq,
                Payload = table.ViewFor(player.UserId)
            }, cancellationToken);
        }
    }

    private static string ReadAction(object? payload)
    {
        if (payload is JsonElement element && element.TryGetProperty("action", out var action))
        {
            return (action.GetString() ?? string.Empty).Trim().ToLowerInvariant();
        }

        if (payload != null)
        {
            var parsed = JsonSerializer.Deserialize<WsBattleActionPayload>(payload.ToString() ?? "{}", Json);
            return (parsed?.Action ?? string.Empty).Trim().ToLowerInvariant();
        }

        return string.Empty;
    }

    private static async Task BroadcastAsync(
        PvpConnectionHub hub,
        PvpBattleHost battles,
        MatchEvent evt,
        Guid joiner,
        long seq,
        CancellationToken cancellationToken)
    {
        if (evt.RoomOpened && evt.Room != null)
        {
            var table = battles.Open(evt.Room);
            var payload = new WsRoomReadyPayload
            {
                RoomId = evt.Room.RoomId.ToString("N"),
                Seed = evt.Room.Seed,
                Players = evt.Room.Players.ToArray()
            };
            foreach (var id in evt.Recipients)
            {
                await hub.SendAsync(id, new WsEnvelope { T = WsMessageTypes.RoomReady, Payload = payload }, cancellationToken);
            }

            await BroadcastBattleAsync(hub, table, 0, cancellationToken);
            return;
        }

        await hub.SendAsync(joiner, Roster(WsMessageTypes.Queued, evt.Players, seq), cancellationToken);
        foreach (var id in evt.Recipients)
        {
            if (id == joiner)
            {
                continue;
            }

            await hub.SendAsync(id, Roster(WsMessageTypes.QueueUpdate, evt.Players, 0), cancellationToken);
        }
    }

    private static async Task BroadcastRosterAsync(
        PvpConnectionHub hub,
        IReadOnlyList<Guid> recipients,
        string type,
        IReadOnlyList<PlayerPublic> players,
        long seq,
        CancellationToken cancellationToken)
    {
        var envelope = Roster(type, players, seq);
        foreach (var id in recipients)
        {
            await hub.SendAsync(id, envelope, cancellationToken);
        }
    }

    private static WsEnvelope Roster(string type, IReadOnlyList<PlayerPublic> players, long seq)
    {
        return new WsEnvelope
        {
            T = type,
            Seq = seq,
            Payload = new WsQueueRosterPayload { Players = players.ToArray() }
        };
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
            var payload = JsonSerializer.Deserialize<WsAuthPayload>(incoming.Payload.ToString() ?? "{}", Json);
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
