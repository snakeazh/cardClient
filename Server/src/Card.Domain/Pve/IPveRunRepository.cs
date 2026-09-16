using CardShare.Domain.Pve;

namespace CardShare.Domain;

public interface IPveRunRepository
{
    Task AddAsync(PveRun run, CancellationToken cancellationToken);

    Task<PveRun?> GetAsync(Guid runId, CancellationToken cancellationToken);

    Task<PveRun?> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveAsync(PveRun run, CancellationToken cancellationToken);
}
