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

public sealed class RedisPvpMatchmaker : IPvpMatchmaker
{
    private const string QueueKey = "pvp:queue";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IDatabase _db;
    private readonly object _gate = new object();
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

        lock (_gate)
        {
            RemoveLocked(userId);
            var waiting = LoadLocked();
            waiting.Add(Clone(player));
            if (waiting.Count < PvpRules.RoomSize)
            {
                SaveLocked(waiting);
                return new MatchEvent
                {
                    RoomOpened = false,
                    Players = waiting,
                    Recipients = Ids(waiting),
                    Joiner = userId
                };
            }

            var seated = waiting.Take(PvpRules.RoomSize).Select(Clone).ToArray();
            var remain = waiting.Skip(PvpRules.RoomSize).ToList();
            SaveLocked(remain);
            return new MatchEvent
            {
                RoomOpened = true,
                Room = new PvpRoom
                {
                    RoomId = Guid.NewGuid(),
                    Seed = _random.Next(),
                    Players = seated
                },
                Players = seated,
                Recipients = Ids(seated)
            };
        }
    }

    public MatchEvent? Cancel(Guid userId)
    {
        lock (_gate)
        {
            var waiting = LoadLocked();
            var removed = waiting.RemoveAll(p => SameUser(p, userId));
            if (removed <= 0)
            {
                return null;
            }

            SaveLocked(waiting);
            return new MatchEvent
            {
                RoomOpened = false,
                Players = waiting,
                Recipients = Ids(waiting)
            };
        }
    }

    public void Leave(Guid userId) => Cancel(userId);

    private void RemoveLocked(Guid userId)
    {
        var waiting = LoadLocked();
        if (waiting.RemoveAll(p => SameUser(p, userId)) > 0)
        {
            SaveLocked(waiting);
        }
    }

    private List<PlayerPublic> LoadLocked()
    {
        var values = _db.ListRange(QueueKey);
        var list = new List<PlayerPublic>();
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            var parsed = JsonSerializer.Deserialize<PlayerPublic>((string)value!, Json);
            if (parsed != null)
            {
                list.Add(parsed);
            }
        }

        return list;
    }

    private void SaveLocked(IReadOnlyList<PlayerPublic> waiting)
    {
        _db.KeyDelete(QueueKey);
        if (waiting.Count == 0)
        {
            return;
        }

        var payload = waiting.Select(p => (RedisValue)JsonSerializer.Serialize(Clone(p), Json)).ToArray();
        _db.ListRightPush(QueueKey, payload);
    }

    private static bool SameUser(PlayerPublic player, Guid userId)
    {
        return Guid.TryParse(player.UserId, out var id) && id == userId;
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
