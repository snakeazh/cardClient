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
            var matches = context.RequestServices.GetRequiredService<PvpMatchHost>();
            var rewards = context.RequestServices.GetRequiredService<PvpRewardService>();
            var router = context.RequestServices.GetRequiredService<PvpMessageRouter>();
            var scopes = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
            await HandleAsync(socket, tokens, matchmaker, hub, matches, rewards, router, scopes, context.RequestAborted);
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
        PvpMatchHost matches,
        PvpRewardService rewards,
        PvpMessageRouter router,
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
                    await BroadcastAsync(router, matches, scopes, evt, userId.Value, incoming.Seq, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Battle)
                {
                    await HandleBattleAsync(socket, router, matches, rewards, userId.Value, incoming, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Sync)
                {
                    await HandleSyncAsync(socket, router, matches, userId.Value, incoming, cancellationToken);
                    continue;
                }

                if (t == WsMessageTypes.Cancel || t == WsMessageTypes.Leave)
                {
                    matches.Leave(userId.Value);
                    var evt = matchmaker.Cancel(userId.Value);
                    await SendAsync(socket, new WsEnvelope { T = t, Seq = incoming.Seq }, cancellationToken);
                    if (evt != null)
                    {
                        await BroadcastRosterAsync(
                            router,
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
                // WS 断开 = 代管，不退赛：置断线标志，有变化（含自动锁定结算）则广播。
                if (matches.SetConnected(userId.Value, false, out var left) && left != null)
                {
                    await BroadcastMatchAsync(router, left, 0, cancellationToken);
                }

                var evt = matchmaker.Cancel(userId.Value);
                hub.Remove(userId.Value);
                if (evt != null)
                {
                    await BroadcastRosterAsync(
                        router,
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
        PvpRewardService rewards,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (!matches.TryGet(userId, out _))
        {
            if (!await router.Bus.PublishCommandAsync(Guid.Empty, userId, incoming, cancellationToken))
            {
                await SendError(socket, incoming.Seq, ErrorCodes.InvalidRequest, "Not in a battle.", cancellationToken);
            }

            return;
        }

        await ProcessBattleCommandAsync(router, matches, rewards, userId, incoming, cancellationToken);
    }

    /// <summary>房主侧 battle 处理：Act、发奖、广播。房主未命中（房间已回收）静默丢，客户端靠 sync 超时判死。</summary>
    internal static async Task ProcessBattleCommandAsync(
        PvpMessageRouter router,
        PvpMatchHost matches,
        PvpRewardService rewards,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (!matches.TryGet(userId, out var match))
        {
            return;
        }

        var cmd = ReadBattle(incoming.Payload);
        if (string.IsNullOrEmpty(cmd.Action))
        {
            await SendErrorTo(router, userId, incoming.Seq, ErrorCodes.InvalidRequest, "Unknown battle action.", cancellationToken);
            return;
        }

        try
        {
            matches.Act(userId, cmd.Action, cmd.Index, cmd.Indexes);
        }
        catch (DomainException ex)
        {
            await SendErrorTo(router, userId, incoming.Seq, ex.Code, ex.Message, cancellationToken);
            return;
        }

        await rewards.GrantIfFinishedAsync(match, cancellationToken);
        await BroadcastMatchAsync(router, match, incoming.Seq, cancellationToken);
        if (matches.AdvanceIfReady(userId))
        {
            await rewards.GrantIfFinishedAsync(match, cancellationToken);
            await BroadcastMatchAsync(router, match, incoming.Seq, cancellationToken);
        }
    }

    /// <summary>本实例收到 sync：本地是房主则直接处理，否则经总线转发房主（无总线时回 not_in_battle）。</summary>
    private static async Task HandleSyncAsync(
        WebSocket socket,
        PvpMessageRouter router,
        PvpMatchHost matches,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (!matches.TryGet(userId, out _))
        {
            if (!await router.Bus.PublishCommandAsync(ReadSyncRoomId(incoming.Payload), userId, incoming, cancellationToken))
            {
                await SendError(socket, incoming.Seq, ErrorCodes.NotInBattle, "Not in a battle.", cancellationToken);
            }

            return;
        }

        await ProcessSyncCommandAsync(router, matches, userId, incoming, cancellationToken);
    }

    /// <summary>房主侧 sync 处理：标记上线、回全量快照（Seq 回显）、有变化广播 player_online。</summary>
    internal static async Task ProcessSyncCommandAsync(
        PvpMessageRouter router,
        PvpMatchHost matches,
        Guid userId,
        WsEnvelope incoming,
        CancellationToken cancellationToken)
    {
        if (!matches.TryGet(userId, out var match))
        {
            return;
        }

        var changed = matches.SetConnected(userId, true, out _);
        await router.SendToUser(userId, new WsEnvelope
        {
            T = WsMessageTypes.MatchUpdate,
            Seq = incoming.Seq,
            Payload = match.ViewFor(userId.ToString("N"))
        }, cancellationToken);
        if (changed)
        {
            await BroadcastMatchAsync(router, match, 0, cancellationToken);
        }
    }

    internal static async Task BroadcastMatchAsync(
        PvpMessageRouter router,
        CardShare.Battle.PvpMatch match,
        long seq,
        CancellationToken cancellationToken)
    {
        var events = match.DrainEvents();
        foreach (var player in match.Players)
        {
            if (player.IsBot || !Guid.TryParse(player.UserId, out var id))
            {
                continue;
            }

            var view = match.ViewFor(player.UserId);
            await router.SendToUser(id, new WsEnvelope
            {
                T = WsMessageTypes.MatchUpdate,
                Seq = seq,
                Payload = view
            }, cancellationToken);
        }

        if (events.Count == 0)
        {
            return;
        }

        foreach (var player in match.Players)
        {
            if (player.IsBot || !Guid.TryParse(player.UserId, out var id))
            {
                continue;
            }

            foreach (var evt in events)
            {
                await router.SendToUser(id, new WsEnvelope
                {
                    T = WsMessageTypes.MatchEvent,
                    Seq = seq,
                    Payload = evt
                }, cancellationToken);
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

    private static WsBattleActionPayload ReadBattle(object? payload)
    {
        if (payload is JsonElement element)
        {
            var parsed = element.Deserialize<WsBattleActionPayload>(Json);
            return Normalize(parsed);
        }

        if (payload != null)
        {
            var parsed = JsonSerializer.Deserialize<WsBattleActionPayload>(payload.ToString() ?? "{}", Json);
            return Normalize(parsed);
        }

        return new WsBattleActionPayload();
    }

    private static WsBattleActionPayload Normalize(WsBattleActionPayload? parsed)
    {
        var cmd = parsed ?? new WsBattleActionPayload();
        cmd.Action = (cmd.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (cmd.Indexes == null)
        {
            cmd.Indexes = Array.Empty<int>();
        }

        return cmd;
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

    private static async Task BroadcastAsync(
        PvpMessageRouter router,
        PvpMatchHost matches,
        IServiceScopeFactory scopes,
        MatchEvent evt,
        Guid joiner,
        long seq,
        CancellationToken cancellationToken)
    {
        if (evt.RoomOpened && evt.Room != null)
        {
            var combatSeats = await PvpCombatSeats.LoadAsync(scopes, evt.Room, cancellationToken);
            var match = matches.Open(evt.Room, combatSeats);
            var payload = new WsRoomReadyPayload
            {
                RoomId = evt.Room.RoomId.ToString("N"),
                Seed = evt.Room.Seed,
                ModeId = match.ModeId,
                Players = evt.Room.Players.ToArray()
            };
            foreach (var id in evt.Recipients)
            {
                await router.SendToUser(id, new WsEnvelope { T = WsMessageTypes.RoomReady, Payload = payload }, cancellationToken);
            }

            await BroadcastMatchAsync(router, match, 0, cancellationToken);
            return;
        }

        await router.SendToUser(joiner, Roster(WsMessageTypes.Queued, evt.Players, seq), cancellationToken);
        foreach (var id in evt.Recipients)
        {
            if (id == joiner)
            {
                continue;
            }

            await router.SendToUser(id, Roster(WsMessageTypes.QueueUpdate, evt.Players, 0), cancellationToken);
        }
    }

    private static async Task BroadcastRosterAsync(
        PvpMessageRouter router,
        IReadOnlyList<Guid> recipients,
        string type,
        IReadOnlyList<PlayerPublic> players,
        long seq,
        CancellationToken cancellationToken)
    {
        var envelope = Roster(type, players, seq);
        foreach (var id in recipients)
        {
            await router.SendToUser(id, envelope, cancellationToken);
        }
    }

    private static WsEnvelope Roster(string type, IReadOnlyList<PlayerPublic> players, long seq)
    {
        return new WsEnvelope
        {
            T = type,
            Seq = seq,
            Payload = new WsQueueRosterPayload
            {
                Players = players.ToArray(),
                TimeoutMs = PvpRules.QueueTimeoutMs
            }
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

    private static Task SendErrorTo(
        PvpMessageRouter router,
        Guid userId,
        long seq,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        return router.SendToUser(userId, new WsEnvelope
        {
            T = WsMessageTypes.Error,
            Seq = seq,
            Payload = new ApiError { Code = code, Message = message }
        }, cancellationToken);
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
