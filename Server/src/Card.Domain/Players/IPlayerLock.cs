namespace CardShare.Domain;

public interface IPlayerLock
{
    Task<IAsyncDisposable> AcquireAsync(Guid userId, CancellationToken cancellationToken);
}
