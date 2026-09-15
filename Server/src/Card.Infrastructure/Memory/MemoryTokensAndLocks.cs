using System.Collections.Concurrent;
using CardShare.Domain;

namespace CardShare.Infrastructure.Memory;

public sealed class MemoryTokenService : ITokenService
{
    private readonly ConcurrentDictionary<string, TokenRecord> _access = new ConcurrentDictionary<string, TokenRecord>();
    private readonly ConcurrentDictionary<string, TokenRecord> _refresh = new ConcurrentDictionary<string, TokenRecord>();
    private readonly IClock _clock;
    private readonly TimeSpan _accessTtl;
    private readonly TimeSpan _refreshTtl;

    public MemoryTokenService(IClock clock, TimeSpan accessTtl, TimeSpan refreshTtl)
    {
        _clock = clock;
        _accessTtl = accessTtl;
        _refreshTtl = refreshTtl;
    }

    public Task<AuthTicket> IssueAsync(Guid userId, CancellationToken cancellationToken)
        => Task.FromResult(Issue(userId));

    public Task<Guid?> ResolveAccessTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || !_access.TryGetValue(accessToken, out var record))
        {
            return Task.FromResult<Guid?>(null);
        }

        if (record.ExpiresAt <= _clock.UtcNow)
        {
            _access.TryRemove(accessToken, out _);
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(record.UserId);
    }

    public Task<AuthTicket?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (!_refresh.TryGetValue(refreshToken, out var record) || record.ExpiresAt <= _clock.UtcNow)
        {
            return Task.FromResult<AuthTicket?>(null);
        }

        _refresh.TryRemove(refreshToken, out _);
        return Task.FromResult<AuthTicket?>(Issue(record.UserId));
    }

    private AuthTicket Issue(Guid userId)
    {
        var access = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var refresh = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var now = _clock.UtcNow;
        _access[access] = new TokenRecord(userId, now.Add(_accessTtl));
        _refresh[refresh] = new TokenRecord(userId, now.Add(_refreshTtl));
        return new AuthTicket
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresInSeconds = (int)_accessTtl.TotalSeconds,
            UserId = userId
        };
    }

    private readonly record struct TokenRecord(Guid UserId, DateTimeOffset ExpiresAt);
}

public sealed class MemoryPlayerLock : IPlayerLock
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new ConcurrentDictionary<Guid, SemaphoreSlim>();

    public async Task<IAsyncDisposable> AcquireAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sem = _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        return new Releaser(sem);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sem;
        private int _disposed;

        public Releaser(SemaphoreSlim sem) => _sem = sem;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _sem.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
