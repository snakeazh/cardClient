using CardShare.Domain;
using StackExchange.Redis;

namespace CardShare.Infrastructure.Redis;

public sealed class RedisTokenService : ITokenService
{
    private const string AccessPrefix = "auth:access:";
    private const string RefreshPrefix = "auth:refresh:";

    private readonly IDatabase _db;
    private readonly TimeSpan _accessTtl;
    private readonly TimeSpan _refreshTtl;

    public RedisTokenService(IConnectionMultiplexer redis, TimeSpan accessTtl, TimeSpan refreshTtl)
    {
        _db = redis.GetDatabase();
        _accessTtl = accessTtl;
        _refreshTtl = refreshTtl;
    }

    public async Task<AuthTicket> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await IssueAsync(userId);
    }

    public async Task<Guid?> ResolveAccessTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var value = await _db.StringGetAsync(AccessPrefix + accessToken);
        return ParseUserId(value);
    }

    public async Task<AuthTicket?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var key = RefreshPrefix + refreshToken;
        var value = await _db.StringGetAsync(key);
        var userId = ParseUserId(value);
        if (userId == null)
        {
            return null;
        }

        await _db.KeyDeleteAsync(key);
        return await IssueAsync(userId.Value);
    }

    private async Task<AuthTicket> IssueAsync(Guid userId)
    {
        var access = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var refresh = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var user = userId.ToString("N");
        await _db.StringSetAsync(AccessPrefix + access, user, _accessTtl);
        await _db.StringSetAsync(RefreshPrefix + refresh, user, _refreshTtl);
        return new AuthTicket
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresInSeconds = (int)_accessTtl.TotalSeconds,
            UserId = userId
        };
    }

    private static Guid? ParseUserId(RedisValue value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Guid.TryParse((string)value!, out var userId) ? userId : null;
    }
}
