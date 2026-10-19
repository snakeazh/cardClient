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
    private readonly IPvpMatchmaker _matchmaker;
    private readonly IServiceScopeFactory _scopes;

    public PvpCommandDispatcher(
        PvpMatchHost matches,
        PvpMessageRouter router,
        PvpRewardService rewards,
        IPvpMatchmaker matchmaker,
        IServiceScopeFactory scopes)
    {
        _matches = matches;
        _router = router;
        _rewards = rewards;
        _matchmaker = matchmaker;
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

    /// <summary>房主侧 sync 处理：标记上线、回全量快照（Seq 回显）、有变化广播 player_online。已淘汰则解绑并回 not_in_battle。</summary>
    public async Task ProcessSyncAsync(Guid userId, WsEnvelope incoming, CancellationToken cancellationToken)
    {
        if (!_matches.TryGet(userId, out var match))
        {
            return;
        }

        var self = FindFighter(match, userId);
        if (self == null || !self.Alive)
        {
            _matches.Unbind(userId);
            await SendErrorTo(userId, incoming.Seq, ErrorCodes.NotInBattle, "Not in a battle.", cancellationToken);
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

    /// <summary>
    /// 逐人发 match_update（先 DrainEvents 拿增量），再把事件逐条包 match_event 发给真人。
    /// 只发给当前仍映射到这一局的真人。本批刚淘汰、尚未解绑的会收到最后一次快照，推完后 UnbindEliminated。
    /// 已去开下一把的人映射在新局上，旧局不再推送，也不拆掉新局映射。
    /// </summary>
    public async Task BroadcastMatchAsync(PvpMatch match, long seq, CancellationToken cancellationToken)
    {
        var events = match.DrainEvents();
        foreach (var fighter in match.Fighters)
        {
            if (fighter.IsBot || !Guid.TryParse(fighter.UserId, out var id))
            {
                continue;
            }

            // 只推仍挂在这一局上的人。淘汰解绑后若已去开下一把，IsBound 会变成 true，但不能把旧局结算推过去。
            if (!_matches.IsBoundTo(id, match))
            {
                continue;
            }

            var view = match.ViewFor(fighter.UserId);
            await _router.SendToUser(id, new WsEnvelope
            {
                T = WsMessageTypes.MatchUpdate,
                Seq = seq,
                Payload = view
            }, cancellationToken);
        }

        if (events.Count > 0)
        {
            foreach (var fighter in match.Fighters)
            {
                if (fighter.IsBot || !Guid.TryParse(fighter.UserId, out var id))
                {
                    continue;
                }

                if (!_matches.IsBoundTo(id, match))
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

        _matches.UnbindEliminated(match);
    }

    /// <summary>接受没凑齐：通知房间里的真人，再把要留下的人插回队列前面。</summary>
    public async Task<bool> FailAcceptAsync(Guid userId, bool requeueUser, CancellationToken cancellationToken)
    {
        if (!_matches.TryFailAccept(userId, requeueUser, out var group))
        {
            return false;
        }

        await BroadcastAcceptFailAsync(group, cancellationToken);
        return true;
    }

    public async Task SweepAcceptFailuresAsync(CancellationToken cancellationToken)
    {
        var groups = _matches.SweepAcceptTimeouts(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        for (var i = 0; i < groups.Count; i++)
        {
            await BroadcastAcceptFailAsync(groups[i], cancellationToken);
        }
    }

    public async Task SweepBotAcceptsAsync(CancellationToken cancellationToken)
    {
        var ticks = _matches.SweepBotAccepts(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        for (var i = 0; i < ticks.Count; i++)
        {
            await BroadcastAcceptProgressAsync(ticks[i], cancellationToken);
        }
    }

    public Task BroadcastAcceptProgressAsync(PvpMatchHost.AcceptProgress progress, CancellationToken cancellationToken)
    {
        return BroadcastAcceptProgressAsync(
            progress.Humans,
            progress.AcceptedIds,
            progress.AllAccepted,
            progress.Match,
            cancellationToken);
    }

    private async Task BroadcastAcceptProgressAsync(
        Guid[] humans,
        string[] acceptedIds,
        bool allAccepted,
        PvpMatch? match,
        CancellationToken cancellationToken)
    {
        var update = new WsEnvelope
        {
            T = WsMessageTypes.AcceptUpdate,
            Seq = 0,
            Payload = new WsAcceptUpdatePayload { UserIds = acceptedIds ?? Array.Empty<string>() }
        };
        for (var i = 0; i < humans.Length; i++)
        {
            await _router.SendToUser(humans[i], update, cancellationToken);
        }

        if (!allAccepted)
        {
            return;
        }

        var ready = new WsEnvelope { T = WsMessageTypes.AllAccepted, Seq = 0 };
        for (var i = 0; i < humans.Length; i++)
        {
            await _router.SendToUser(humans[i], ready, cancellationToken);
        }

        if (match != null)
        {
            await BroadcastMatchAsync(match, 0, cancellationToken);
        }
    }

    private async Task BroadcastAcceptFailAsync(PvpMatchHost.AcceptFailGroup group, CancellationToken cancellationToken)
    {
        var failed = new WsEnvelope { T = WsMessageTypes.AcceptFailed, Seq = 0 };
        for (var i = 0; i < group.Humans.Length; i++)
        {
            await _router.SendToUser(group.Humans[i], failed, cancellationToken);
        }

        for (var i = 0; i < group.Requeue.Length; i++)
        {
            var player = group.Requeue[i];
            if (player == null || !Guid.TryParse(player.UserId, out var id))
            {
                continue;
            }

            var evt = _matchmaker.EnqueuePriority(player);
            await BroadcastRoomEventAsync(evt, id, 0, cancellationToken);
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

    private static PvpFighter? FindFighter(PvpMatch match, Guid userId)
    {
        var key = userId.ToString("N");
        foreach (var fighter in match.Fighters)
        {
            if (string.Equals(fighter.UserId, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fighter.UserId, userId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return fighter;
            }
        }

        return null;
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
