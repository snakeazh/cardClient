using CardShare.Battle;
using CardShare.Domain.Players;

namespace CardShare.Server.Pvp;

/// <summary>PvP 终局落库：名次金币进钱包、名次 PVP 分数加减（下限 0）。整局只发一次。</summary>
public sealed class PvpRewardService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PvpRewardService> _logger;

    public PvpRewardService(IServiceScopeFactory scopes, ILogger<PvpRewardService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task GrantIfFinishedAsync(PvpMatch match, CancellationToken cancellationToken)
    {
        if (!match.TryMarkRewardsGranted())
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<PlayerSession>();
        foreach (var fighter in match.Fighters)
        {
            if (fighter.IsBot || !Guid.TryParse(fighter.UserId, out var userId))
            {
                continue;
            }

            var gold = fighter.RewardGold;
            var scoreDelta = fighter.RewardScore;
            if (gold <= 0 && scoreDelta == 0)
            {
                continue;
            }

            try
            {
                await session.MutateAsync(userId, profile =>
                {
                    if (gold > 0)
                    {
                        profile.GrantWalletGold(gold);
                    }

                    if (scoreDelta != 0)
                    {
                        profile.ApplyPvpScoreDelta(scoreDelta);
                    }

                    return 0;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                // 单个玩家发奖失败不阻塞广播与其余玩家；对局级一次性标志保证不重发——必须留日志，否则奖励静默丢失。
                _logger.LogError(
                    ex,
                    "PvP reward grant failed. room={RoomId} user={UserId} gold={Gold} scoreDelta={ScoreDelta}",
                    match.RoomId,
                    fighter.UserId,
                    gold,
                    scoreDelta);
            }
        }
    }
}
