using CardShare.Domain.Config;

namespace CardShare.Domain.Players;

public sealed class PlayerSession
{
    private readonly IPlayerRepository _players;
    private readonly IGameConfig _config;
    private readonly IClock _clock;
    private readonly IPlayerLock _locks;

    public PlayerSession(
        IPlayerRepository players,
        IGameConfig config,
        IClock clock,
        IPlayerLock locks)
    {
        _players = players;
        _config = config;
        _clock = clock;
        _locks = locks;
    }

    public Task<IAsyncDisposable> AcquireAsync(Guid userId, CancellationToken cancellationToken)
        => _locks.AcquireAsync(userId, cancellationToken);

    public async Task<PlayerProfile> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await _players.GetAsync(userId, cancellationToken);
        if (profile == null)
        {
            throw DomainException.Unauthorized("Player not found.");
        }

        profile.ApplyDailyReset(_config, _clock.UtcNow);
        return profile;
    }

    public Task SaveAsync(PlayerProfile profile, CancellationToken cancellationToken)
        => _players.SaveAsync(profile, cancellationToken);

    /// <summary>读档。仅当跨日重置真正改档时写回，其它 GET 不落库。</summary>
    public async Task<T> ReadAsync<T>(Guid userId, Func<PlayerProfile, T> project, CancellationToken cancellationToken)
    {
        await using var gate = await AcquireAsync(userId, cancellationToken);
        var profile = await _players.GetAsync(userId, cancellationToken);
        if (profile == null)
        {
            throw DomainException.Unauthorized("Player not found.");
        }

        var dirty = profile.ApplyDailyReset(_config, _clock.UtcNow);
        var result = project(profile);
        if (dirty)
        {
            await SaveAsync(profile, cancellationToken);
        }

        return result;
    }

    public async Task<T> MutateAsync<T>(Guid userId, Func<PlayerProfile, T> mutate, CancellationToken cancellationToken)
    {
        await using var gate = await AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        var result = mutate(profile);
        await SaveAsync(profile, cancellationToken);
        return result;
    }
}
