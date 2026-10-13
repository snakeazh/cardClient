using CardShare.Domain;
using StackExchange.Redis;

namespace CardShare.Infrastructure.Redis;

public sealed class RedisTokenService : ITokenService
{
    private const string AccessPrefix = "auth:access:";

    private readonly IDatabase _db;
    private readonly TimeSpan _accessTtl;

    public RedisTokenService(IConnectionMultiplexer redis, TimeSpan accessTtl)
    {
        _db = redis.GetDatabase();
        _accessTtl = accessTtl;
    }

    public async Task<AuthTicket> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var access = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        await _db.StringSetAsync(AccessPrefix + access, userId.ToString("N"), _accessTtl);
        return new AuthTicket
        {
            AccessToken = access,
            ExpiresInSeconds = (int)_accessTtl.TotalSeconds,
            UserId = userId
        };
    }

    public async Task<Guid?> ResolveAccessTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var value = await _db.StringGetAsync(AccessPrefix + accessToken.Trim());
        return ParseUserId(value);
    }

    private static Guid? ParseUserId(RedisValue value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var text = value.ToString();
        return Guid.TryParse(text, out var userId) ? userId : null;
    }
}
