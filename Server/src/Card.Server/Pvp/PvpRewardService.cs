using CardShare.Battle;
using CardShare.Domain.Players;

namespace CardShare.Server.Pvp;

/// <summary>PvP 名次奖励落库：对局 finished 后按 fighter.RewardGold 给真人玩家发钱包金币，整局只发一次。</summary>
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
            if (fighter.IsBot || fighter.RewardGold <= 0 || !Guid.TryParse(fighter.UserId, out var userId))
            {
                continue;
            }

            try
            {
                await session.MutateAsync(userId, profile =>
                {
                    profile.GrantWalletGold(fighter.RewardGold);
                    return 0;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                // 单个玩家发奖失败不阻塞广播与其余玩家；对局级一次性标志保证不重发——必须留日志，否则奖励静默丢失。
                _logger.LogError(
                    ex,
                    "PvP reward grant failed. room={RoomId} user={UserId} gold={Gold}",
                    match.RoomId,
                    fighter.UserId,
                    fighter.RewardGold);
            }
        }
    }
}
