using CardShare.Contracts;

namespace CardShare.Domain.Pvp;

public interface IPvpLeaderboardRepository
{
    Task<PvpLeaderboardResponse?> GetAsync(Guid userId, CancellationToken cancellationToken);
}
