using CardShare.Domain.Ads;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CardShare.Infrastructure.Ads;

/// <summary>
/// 广告观看按场景计数：Redis hash adwatch:daily:{yyyyMMdd}（UTC+8），field=场景名，90 天过期。
/// 查询：redis-cli HGETALL adwatch:daily:20261021
/// </summary>
public sealed class RedisAdWatchStats : IAdWatchStats
{
    private const string KeyPrefix = "adwatch:daily:";
    private static readonly TimeSpan KeyTtl = TimeSpan.FromDays(90);
    private static readonly TimeSpan CnOffset = TimeSpan.FromHours(8);

    private readonly IDatabase _db;
    private readonly ILogger<RedisAdWatchStats> _logger;

    public RedisAdWatchStats(IConnectionMultiplexer redis, ILogger<RedisAdWatchStats> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public void RecordWatch(string scene)
    {
        _ = RecordSafelyAsync(scene);
    }

    private async Task RecordSafelyAsync(string scene)
    {
        try
        {
            var key = KeyPrefix + DateTimeOffset.UtcNow.ToOffset(CnOffset).ToString("yyyyMMdd");
            await _db.HashIncrementAsync(key, scene);
            await _db.KeyExpireAsync(key, KeyTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ad watch stats record failed ({Scene})", scene);
        }
    }
}
