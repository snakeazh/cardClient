using System.Text.Json;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;
using StackExchange.Redis;

namespace CardShare.Infrastructure.Redis;

public sealed class RedisPlayerLock : IPlayerLock
{
    private readonly IDatabase _db;

    public RedisPlayerLock(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task<IAsyncDisposable> AcquireAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = "lock:player:" + userId.ToString("N");
        var token = Guid.NewGuid().ToString("N");
        for (var i = 0; i < 50; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _db.StringSetAsync(key, token, TimeSpan.FromSeconds(15), When.NotExists))
            {
                return new RedisReleaser(_db, key, token);
            }

            await Task.Delay(40, cancellationToken);
        }

        throw new DomainException(ErrorCodes.Conflict, "Player is busy.");
    }

    private sealed class RedisReleaser : IAsyncDisposable
    {
        private static readonly LuaScript Release = LuaScript.Prepare(
            "if redis.call('get', @key) == @token then return redis.call('del', @key) else return 0 end");

        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private int _disposed;

        public RedisReleaser(IDatabase db, string key, string token)
        {
            _db = db;
            _key = key;
            _token = token;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await _db.ScriptEvaluateAsync(Release, new { key = (RedisKey)_key, token = _token });
            }
        }
    }
}

/// <summary>队列存 Redis Sorted Set（member = userId("N", 32字符) + 条目 JSON，score = 入队毫秒），三个操作各一条 Lua 脚本原子执行。</summary>
public sealed class RedisPvpMatchmaker : IPvpMatchmaker
{
    private const string QueueKey = "pvp:queue";

    private const int UserIdPrefixLength = 32;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly LuaScript EnqueueScript = LuaScript.Prepare(
        "local all = redis.call('ZRANGE', @key, 0, -1) " +
        "for i, m in ipairs(all) do " +
        "  if string.sub(m, 1, @prefixLen) == @uid then redis.call('ZREM', @key, m) end " +
        "end " +
        "redis.call('ZADD', @key, tonumber(@score), @member) " +
        "if redis.call('ZCARD', @key) < tonumber(@roomSize) then " +
        "  return {0, redis.call('ZRANGE', @key, 0, -1)} " +
        "end " +
        "local seated = redis.call('ZRANGE', @key, 0, tonumber(@roomSize) - 1) " +
        "for i, m in ipairs(seated) do redis.call('ZREM', @key, m) end " +
        "return {1, seated}");

    private static readonly LuaScript CancelScript = LuaScript.Prepare(
        "local all = redis.call('ZRANGE', @key, 0, -1) " +
        "local removed = false " +
        "for i, m in ipairs(all) do " +
        "  if string.sub(m, 1, @prefixLen) == @uid then redis.call('ZREM', @key, m) removed = true end " +
        "end " +
        "if not removed then return {0, {}} end " +
        "return {1, redis.call('ZRANGE', @key, 0, -1)}");

    private static readonly LuaScript SweepScript = LuaScript.Prepare(
        "local stale = redis.call('ZRANGEBYSCORE', @key, '-inf', @maxScore) " +
        "for i, m in ipairs(stale) do redis.call('ZREM', @key, m) end " +
        "return {stale, redis.call('ZRANGE', @key, 0, -1)}");

    private sealed class QueueEntry
    {
        public PlayerPublic Player { get; set; } = new PlayerPublic();

        public long EnqueuedUtcMs { get; set; }
    }

    private readonly IDatabase _db;
    private readonly Random _random = new Random();

    public RedisPvpMatchmaker(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public MatchEvent Enqueue(PlayerPublic player)
    {
        if (player == null || !Guid.TryParse(player.UserId, out var userId))
        {
            throw DomainException.Invalid("Invalid player.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var member = Member(userId, new QueueEntry { Player = Clone(player), EnqueuedUtcMs = now });
        var result = Evaluate(EnqueueScript, new
        {
            key = (RedisKey)QueueKey,
            prefixLen = UserIdPrefixLength,
            uid = userId.ToString("N"),
            score = now,
            member,
            roomSize = PvpRules.RoomSize
        });
        var parts = (RedisResult[])result!;
        var opened = (int)parts[0] == 1;
        var players = ParseMembers((RedisResult[])parts[1]!);
        if (!opened)
        {
            return new MatchEvent
            {
                RoomOpened = false,
                Players = players,
                Recipients = Ids(players),
                Joiner = userId
            };
        }

        return new MatchEvent
        {
            RoomOpened = true,
            Room = new PvpRoom
            {
                RoomId = Guid.NewGuid(),
                Seed = _random.Next(),
                Players = players
            },
            Players = players,
            Recipients = Ids(players)
        };
    }

    public MatchEvent? Cancel(Guid userId)
    {
        var result = Evaluate(CancelScript, new
        {
            key = (RedisKey)QueueKey,
            prefixLen = UserIdPrefixLength,
            uid = userId.ToString("N")
        });
        var parts = (RedisResult[])result!;
        if ((int)parts[0] != 1)
        {
            return null;
        }

        var players = ParseMembers((RedisResult[])parts[1]!);
        return new MatchEvent
        {
            RoomOpened = false,
            Players = players,
            Recipients = Ids(players)
        };
    }

    public void Leave(Guid userId) => Cancel(userId);

    public IReadOnlyList<Guid> SweepExpired(long nowUtcMs, long timeoutMs, out IReadOnlyList<PlayerPublic> remaining)
    {
        var maxScore = timeoutMs > 0 ? nowUtcMs - timeoutMs : long.MinValue;
        var result = Evaluate(SweepScript, new
        {
            key = (RedisKey)QueueKey,
            maxScore
        });
        var parts = (RedisResult[])result!;
        var stale = (RedisResult[])parts[0]!;
        var expired = new List<Guid>(stale.Length);
        foreach (var member in stale)
        {
            if (Guid.TryParse(Prefix((string)member!), out var id))
            {
                expired.Add(id);
            }
        }

        remaining = ParseMembers((RedisResult[])parts[1]!);
        return expired;
    }

    private RedisResult Evaluate(LuaScript script, object parameters)
        => _db.ScriptEvaluate(script, parameters);

    private static string Member(Guid userId, QueueEntry entry)
        => userId.ToString("N") + JsonSerializer.Serialize(entry, Json);

    private static string Prefix(string member)
        => member.Length >= UserIdPrefixLength ? member.Substring(0, UserIdPrefixLength) : member;

    private static PlayerPublic[] ParseMembers(RedisResult[] members)
    {
        var players = new List<PlayerPublic>(members.Length);
        foreach (var member in members)
        {
            var raw = (string?)member;
            if (string.IsNullOrEmpty(raw) || raw.Length <= UserIdPrefixLength)
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<QueueEntry>(raw.Substring(UserIdPrefixLength), Json);
            if (entry?.Player != null && Guid.TryParse(entry.Player.UserId, out _))
            {
                players.Add(Clone(entry.Player));
            }
        }

        return players.ToArray();
    }

    private static Guid[] Ids(IReadOnlyList<PlayerPublic> players)
    {
        return players
            .Select(p => Guid.TryParse(p.UserId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();
    }

    private static PlayerPublic Clone(PlayerPublic player)
    {
        return new PlayerPublic
        {
            UserId = player.UserId,
            NickName = player.NickName,
            AvatarUrl = player.AvatarUrl,
            IsBot = player.IsBot,
            BotConfigId = player.BotConfigId
        };
    }
}
