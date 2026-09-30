using System.Text.Json;
using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;
using CardShare.Infrastructure;

namespace CardShare.Server.Pvp;

/// <summary>PvP 业务处理与广播：battle/sync 的房主侧处理、对局快照+增量事件广播、开房/队列 roster 广播。所有出站经 PvpMessageRouter 收口。</summary>
public sealed class PvpCommandDispatcher
{
    private readonly PvpMatchHost _matches;
    private readonly PvpMessageRouter _router;
    private readonly PvpRewardService _rewards;
    private readonly IServiceScopeFactory _scopes;

    public PvpCommandDispatcher(
        PvpMatchHost matches,
        PvpMessageRouter router,
        PvpRewardService rewards,
        IServiceScopeFactory scopes)
    {
        _matches = matches;
        _router = router;
        _rewards = rewards;
        _scopes = scopes;
    }

    /// <summary>房主侧 battle 处理：Act、发奖、广播。房主未命中（房间已回收）静默丢，客户端靠 sync 超时判死。</summary>
    public async Task ProcessBattleAsync(Guid userId, WsEnvelope incoming, CancellationToken cancellationToken)
    {
        if (!_matches.TryGet(userId, out var match))
        {
            return;
        }

        var cmd = ReadBattle(incoming.Payload);
        if (string.IsNullOrEmpty(cmd.Action))
        {
            await SendErrorTo(userId, incoming.Seq, ErrorCodes.InvalidRequest, "Unknown battle action.", cancellationToken);
            return;
        }

        try
        {
            _matches.Act(userId, cmd.Action, cmd.Index, cmd.Indexes);
        }
        catch (DomainException ex)
        {
            await SendErrorTo(userId, incoming.Seq, ex.Code, ex.Message, cancellationToken);
            return;
        }

        await _rewards.GrantIfFinishedAsync(match, cancellationToken);
        await BroadcastMatchAsync(match, incoming.Seq, cancellationToken);
        if (_matches.AdvanceIfReady(userId))
        {
            await _rewards.GrantIfFinishedAsync(match, cancellationToken);
            await BroadcastMatchAsync(match, incoming.Seq, cancellationToken);
        }
    }

    /// <summary>房主侧 sync 处理：标记上线、回全量快照（Seq 回显）、有变化广播 player_online。</summary>
    public async Task ProcessSyncAsync(Guid userId, WsEnvelope incoming, CancellationToken cancellationToken)
    {
        if (!_matches.TryGet(userId, out var match))
        {
            return;
        }

        var changed = _matches.SetConnected(userId, true, out _);
        await _router.SendToUser(userId, new WsEnvelope
        {
            T = WsMessageTypes.MatchUpdate,
            Seq = incoming.Seq,
            Payload = match.ViewFor(userId.ToString("N"))
        }, cancellationToken);
        if (changed)
        {
            await BroadcastMatchAsync(match, 0, cancellationToken);
        }
    }

    /// <summary>房主侧 leave 处理：判负淘汰、发奖（放弃可能触发终局）、广播。本地无对局则静默丢。</summary>
    public async Task ProcessLeaveAsync(Guid userId, WsEnvelope incoming, CancellationToken cancellationToken)
    {
        if (_matches.Leave(userId, out var match) && match != null)
        {
            await _rewards.GrantIfFinishedAsync(match, cancellationToken);
            await BroadcastMatchAsync(match, incoming.Seq, cancellationToken);
        }
    }

    /// <summary>逐人发 match_update（先 DrainEvents 拿增量），再把事件逐条包 match_event 发给真人。</summary>
    public async Task BroadcastMatchAsync(PvpMatch match, long seq, CancellationToken cancellationToken)
    {
        var events = match.DrainEvents();
        foreach (var player in match.Players)
        {
            if (player.IsBot || !Guid.TryParse(player.UserId, out var id))
            {
                continue;
            }

            var view = match.ViewFor(player.UserId);
            await _router.SendToUser(id, new WsEnvelope
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
                await _router.SendToUser(id, new WsEnvelope
                {
                    T = WsMessageTypes.MatchEvent,
                    Seq = seq,
                    Payload = evt
                }, cancellationToken);
            }
        }
    }

    /// <summary>开房：装配座位、建房、发 room_ready + 首轮快照；未开房：回 joiner queued、其余 queue_update。</summary>
    public async Task BroadcastRoomEventAsync(
        MatchEvent evt,
        Guid joiner,
        long seq,
        CancellationToken cancellationToken)
    {
        if (evt.RoomOpened && evt.Room != null)
        {
            var combatSeats = await PvpCombatSeats.LoadAsync(_scopes, evt.Room, cancellationToken);
            var match = _matches.Open(evt.Room, combatSeats);
            var payload = new WsRoomReadyPayload
            {
                RoomId = evt.Room.RoomId.ToString("N"),
                Seed = evt.Room.Seed,
                ModeId = match.ModeId,
                Players = evt.Room.Players.ToArray()
            };
            foreach (var id in evt.Recipients)
            {
                await _router.SendToUser(id, new WsEnvelope { T = WsMessageTypes.RoomReady, Payload = payload }, cancellationToken);
            }

            await BroadcastMatchAsync(match, 0, cancellationToken);
            return;
        }

        await _router.SendToUser(joiner, Roster(WsMessageTypes.Queued, evt.Players, seq), cancellationToken);
        foreach (var id in evt.Recipients)
        {
            if (id == joiner)
            {
                continue;
            }

            await _router.SendToUser(id, Roster(WsMessageTypes.QueueUpdate, evt.Players, 0), cancellationToken);
        }
    }

    public async Task BroadcastRosterAsync(
        IReadOnlyList<Guid> recipients,
        string type,
        IReadOnlyList<PlayerPublic> players,
        long seq,
        CancellationToken cancellationToken)
    {
        var envelope = Roster(type, players, seq);
        foreach (var id in recipients)
        {
            await _router.SendToUser(id, envelope, cancellationToken);
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

    private Task SendErrorTo(Guid userId, long seq, string code, string message, CancellationToken cancellationToken)
    {
        return _router.SendToUser(userId, new WsEnvelope
        {
            T = WsMessageTypes.Error,
            Seq = seq,
            Payload = new ApiError { Code = code, Message = message }
        }, cancellationToken);
    }

    private static WsBattleActionPayload ReadBattle(object? payload)
    {
        if (payload is JsonElement element)
        {
            var parsed = element.Deserialize<WsBattleActionPayload>(PvpJson.Options);
            return Normalize(parsed);
        }

        if (payload != null)
        {
            var parsed = JsonSerializer.Deserialize<WsBattleActionPayload>(payload.ToString() ?? "{}", PvpJson.Options);
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
}
