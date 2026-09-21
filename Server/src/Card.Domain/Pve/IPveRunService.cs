using CardShare.Contracts;

namespace CardShare.Domain.Pve;

public interface IPveRunService
{
    Task<PlayerProfileDto> ResolveRunsOnLoginAsync(
        Guid userId,
        PveSettleRequest? pending,
        CancellationToken cancellationToken);
}
