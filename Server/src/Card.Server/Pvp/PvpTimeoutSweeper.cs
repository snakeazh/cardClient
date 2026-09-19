using CardShare.Battle;

namespace CardShare.Server.Pvp;

/// <summary>后台定时扫描：选牌超时的座位自动锁定结算，并向房间广播最新快照。</summary>
public sealed class PvpTimeoutSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly PvpMatchHost _matches;
    private readonly PvpConnectionHub _hub;

    public PvpTimeoutSweeper(PvpMatchHost matches, PvpConnectionHub hub)
    {
        _matches = matches;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            List<PvpMatch> changed;
            try
            {
                changed = _matches.SweepTimeouts();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var match in changed)
            {
                await PvpWebSocketHost.BroadcastMatchAsync(_hub, match, 0, stoppingToken);
            }
        }
    }
}
