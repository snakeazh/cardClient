using System.Collections.Concurrent;
using CardShare.Domain;
using CardShare.Domain.Players;
using CardShare.Domain.Pve;

namespace CardShare.Infrastructure.Memory;

public sealed class MemoryPlayerRepository : IPlayerRepository
{
    private readonly ConcurrentDictionary<Guid, string> _store = new ConcurrentDictionary<Guid, string>();

    public Task<PlayerProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(userId, out var json))
        {
            return Task.FromResult<PlayerProfile?>(null);
        }

        return Task.FromResult(ProfileJson.Deserialize(json));
    }

    public Task SaveAsync(PlayerProfile profile, CancellationToken cancellationToken)
    {
        _store[profile.UserId] = ProfileJson.Serialize(profile);
        return Task.CompletedTask;
    }
}

public sealed class MemoryAuthBindingRepository : IAuthBindingRepository
{
    private readonly ConcurrentDictionary<string, Guid> _store = new ConcurrentDictionary<string, Guid>(StringComparer.Ordinal);

    public Task<Guid?> FindUserIdAsync(string provider, string openId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_store.TryGetValue(Key(provider, openId), out var id) ? id : (Guid?)null);
    }

    public Task BindAsync(string provider, string openId, Guid userId, CancellationToken cancellationToken)
    {
        _store[Key(provider, openId)] = userId;
        return Task.CompletedTask;
    }

    private static string Key(string provider, string openId) => provider + ":" + openId;
}

public sealed class MemoryPveRunRepository : IPveRunRepository
{
    private readonly ConcurrentDictionary<Guid, PveRun> _store = new ConcurrentDictionary<Guid, PveRun>();

    public Task AddAsync(PveRun run, CancellationToken cancellationToken)
    {
        _store[run.RunId] = Clone(run);
        return Task.CompletedTask;
    }

    public Task<PveRun?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_store.TryGetValue(runId, out var run) ? Clone(run) : null);
    }

    public Task<PveRun?> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        PveRun? latest = null;
        foreach (var run in _store.Values)
        {
            if (run.UserId != userId || run.Status != PveRunStatus.Active)
            {
                continue;
            }

            if (latest == null || run.StartedAt > latest.StartedAt)
            {
                latest = run;
            }
        }

        return Task.FromResult(latest == null ? null : Clone(latest));
    }

    public Task SaveAsync(PveRun run, CancellationToken cancellationToken)
    {
        _store[run.RunId] = Clone(run);
        return Task.CompletedTask;
    }

    private static PveRun Clone(PveRun run)
    {
        return new PveRun
        {
            RunId = run.RunId,
            UserId = run.UserId,
            LevelId = run.LevelId,
            HeroId = run.HeroId,
            ShopRelicIds = run.ShopRelicIds.ToArray(),
            Gold = run.Gold,
            RelicIds = (run.RelicIds ?? new List<int>()).ToList(),
            ShopOfferIds = (run.ShopOfferIds ?? new List<int>()).ToList(),
            ShopRefreshCount = run.ShopRefreshCount,
            FreeShopRefreshLeft = run.FreeShopRefreshLeft,
            Status = run.Status,
            StartedAt = run.StartedAt,
            SettledAt = run.SettledAt,
            SettleFingerprint = run.SettleFingerprint,
            GoldGranted = run.GoldGranted,
            ScoreTotal = run.ScoreTotal,
            CurrentLevelId = run.CurrentLevelId,
            HighestClearedLevelId = run.HighestClearedLevelId,
            ScoredLevelId = run.ScoredLevelId
        };
    }
}

internal static class ProfileJson
{
    public static string Serialize(PlayerProfile profile)
        => System.Text.Json.JsonSerializer.Serialize(profile);

    public static PlayerProfile? Deserialize(string json)
        => System.Text.Json.JsonSerializer.Deserialize<PlayerProfile>(json);
}
