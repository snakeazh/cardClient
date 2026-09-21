using CardShare.Domain.Players;

namespace CardShare.Domain;

public interface IPlayerRepository
{
    Task<PlayerProfile?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveAsync(PlayerProfile profile, CancellationToken cancellationToken);
}
