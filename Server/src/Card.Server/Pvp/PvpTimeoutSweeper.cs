using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain.Pvp;

namespace CardShare.Server.Pvp;

/// <summary>后台定时扫描：选牌超时的座位自动锁定结算并向房间广播；真人排队到期补机器人开房；排队超时者踢出队列并通知。</summary>
public sealed class PvpTimeoutSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly PvpMatchHost _matches;
    private readonly PvpMessageRouter _router;
    private readonly PvpRewardService _rewards;
    private readonly IPvpMatchmaker _matchmaker;
    private readonly IServiceScopeFactory _scopes;

    public PvpTimeoutSweeper(
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            List<PvpMatch> changed;
            IReadOnlyList<Guid> expired;
            IReadOnlyList<PlayerPublic> remaining;
            MatchEvent? fillEvt = null;
            try
            {
                _matches.PurgeFinishedRooms(DateTimeOffset.UtcNow);
                changed = _matches.SweepTimeouts();
                // 真人排队超过 GameConst.PvpBotFillDelaySeconds 秒没满 4 人：补机器人开房
                if (_matchmaker is PvpBotFillMatchmaker filler)
                {
                    fillEvt = filler.FillDueBots();
                }

                expired = _matchmaker.SweepExpired(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    PvpRules.QueueTimeoutMs,
                    out remaining);
            }
            catch (Exception)
            {
                continue;
            }

            if (fillEvt is { RoomOpened: true, Room: not null })
            {
                await PvpWebSocketHost.BroadcastAsync(_router, _matches, _scopes, fillEvt, Guid.Empty, 0, stoppingToken);
            }

            foreach (var match in changed)
            {
                await _rewards.GrantIfFinishedAsync(match, stoppingToken);
                await PvpWebSocketHost.BroadcastMatchAsync(_router, match, 0, stoppingToken);
            }

            if (expired.Count == 0)
            {
                continue;
            }

            foreach (var userId in expired)
            {
                await _router.SendToUser(userId, new WsEnvelope { T = WsMessageTypes.QueueTimeout, Seq = 0 }, stoppingToken);
            }

            var update = new WsEnvelope
            {
                T = WsMessageTypes.QueueUpdate,
                Seq = 0,
                Payload = new WsQueueRosterPayload
                {
                    Players = remaining.ToArray(),
                    TimeoutMs = PvpRules.QueueTimeoutMs
                }
            };
            foreach (var player in remaining)
            {
                if (Guid.TryParse(player.UserId, out var id))
                {
                    await _router.SendToUser(id, update, stoppingToken);
                }
            }
        }
    }
}
