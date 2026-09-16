using CardShare.Contracts;
using CardShare.Contracts.Config;
using CardShare.Domain.Config;
using CardShare.Domain.Players;

namespace CardShare.Domain.Pve;

public sealed class PveRunService : IPveRunService
{
    private readonly PlayerSession _session;
    private readonly IPveRunRepository _runs;
    private readonly IGameConfig _config;
    private readonly IClock _clock;
    private readonly Random _random = new Random();

    public PveRunService(
        PlayerSession session,
        IPveRunRepository runs,
        IGameConfig config,
        IClock clock)
    {
        _session = session;
        _runs = runs;
        _config = config;
        _clock = clock;
    }

    public async Task<PveRunResponse> GetActiveRunAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await _runs.GetActiveByUserAsync(userId, cancellationToken);
        return new PveRunResponse { Run = run == null ? null! : PveRunMapper.ToDto(run) };
    }

    public async Task<PveRunResponse> ReportProgressAsync(
        Guid userId,
        PveProgressRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.RunId))
        {
            throw DomainException.Invalid("runId is required.");
        }

        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, request.RunId, cancellationToken);
        if (run.CurrentLevelId <= 0)
        {
            run.CurrentLevelId = run.LevelId;
        }

        if (run.ScoredLevelId != run.CurrentLevelId)
        {
            run.ScoreTotal += PveProgressRules.ClampScore(request.Score);
            run.ScoredLevelId = run.CurrentLevelId;
        }

        if (request.ClearedStage)
        {
            run.HighestClearedLevelId = run.CurrentLevelId;
            var nextId = PveProgressRules.NextLevelId(_config.Tables, run.CurrentLevelId);
            if (nextId != null)
            {
                run.CurrentLevelId = nextId.Value;
            }
        }

        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
    }

    public async Task<PlayerProfileDto> ResolveRunsOnLoginAsync(
        Guid userId,
        PveSettleRequest? pending,
        CancellationToken cancellationToken)
    {
        if (pending != null && !string.IsNullOrEmpty(pending.RunId))
        {
            try
            {
                var settled = await SettlePveAsync(userId, pending, cancellationToken);
                return await ForfeitActiveRunAsync(userId, cancellationToken, settled.Profile);
            }
            catch (DomainException ex) when (
                ex.Code == ErrorCodes.RunNotFound ||
                ex.Code == ErrorCodes.RunAlreadySettled ||
                ex.Code == ErrorCodes.Conflict ||
                ex.Code == ErrorCodes.InvalidRequest)
            {
            }
        }

        return await ForfeitActiveRunAsync(userId, cancellationToken);
    }

    public async Task<PlayerProfileDto> ForfeitActiveRunAsync(
        Guid userId,
        CancellationToken cancellationToken,
        PlayerProfileDto? alreadyLoaded = null)
    {
        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await _runs.GetActiveByUserAsync(userId, cancellationToken);
        var profile = await _session.LoadAsync(userId, cancellationToken);
        if (run == null)
        {
            await _session.SaveAsync(profile, cancellationToken);
            return alreadyLoaded ?? ProfileMapper.ToDto(profile);
        }

        var request = new PveSettleRequest
        {
            RunId = run.RunId.ToString("N"),
            Cleared = false,
            Forfeit = true
        };
        return (await SettleUnlockedAsync(run, request, profile, cancellationToken)).Profile;
    }

    public async Task<PveStartResponse> StartPveAsync(Guid userId, PveStartRequest request, CancellationToken cancellationToken)
    {
        if (request == null || request.LevelId <= 0)
        {
            throw DomainException.Invalid("levelId is required.");
        }

        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var active = await _runs.GetActiveByUserAsync(userId, cancellationToken);
        if (active != null)
        {
            throw new DomainException(ErrorCodes.ActiveRunExists, "An active run already exists.");
        }

        var profile = await _session.LoadAsync(userId, cancellationToken);
        if (!_config.Tables.TryGetLevel(request.LevelId, out var level))
        {
            throw DomainException.Invalid($"Unknown level {request.LevelId}.");
        }

        var heroId = request.HeroId > 0 ? request.HeroId : _config.Balance.DefaultHeroId;
        if (!profile.IsLevelUnlocked(request.LevelId, _config))
        {
            throw new DomainException(ErrorCodes.LevelLocked, "Level is locked.");
        }

        if (!profile.IsHeroUnlocked(heroId, _config))
        {
            throw new DomainException(ErrorCodes.HeroLocked, "Hero is locked.");
        }

        profile.SpendEnergyForRun(_config);
        profile.Level.LastHeroId = heroId;
        profile.Level.LastLevelId = request.LevelId;
        profile.Level.LastDifficulty = level.Difficulty;

        var run = new PveRun
        {
            RunId = Guid.NewGuid(),
            UserId = userId,
            LevelId = request.LevelId,
            CurrentLevelId = request.LevelId,
            HeroId = heroId,
            ShopRelicIds = profile.ShopRelicIds(_config),
            Gold = _config.Balance.PlayerInitialGoldNum,
            RelicIds = new List<int>(),
            ShopOfferIds = new List<int>(),
            Status = PveRunStatus.Active,
            StartedAt = _clock.UtcNow
        };
        lock (_random)
        {
            PveShopRules.EnsureOffers(run, _config.Tables, _config.Balance, _random);
        }

        await _runs.AddAsync(run, cancellationToken);
        await _session.SaveAsync(profile, cancellationToken);

        return new PveStartResponse
        {
            RunId = run.RunId.ToString("N"),
            LevelId = run.LevelId,
            HeroId = run.HeroId,
            ShopRelicIds = run.ShopRelicIds,
            Run = PveRunMapper.ToDto(run),
            Profile = ProfileMapper.ToDto(profile)
        };
    }

    public async Task<PveSettleResponse> SettlePveAsync(Guid userId, PveSettleRequest request, CancellationToken cancellationToken)
    {
        if (request == null || !Guid.TryParse(request.RunId, out var runId))
        {
            throw DomainException.Invalid("runId is required.");
        }

        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await _runs.GetAsync(runId, cancellationToken);
        if (run == null || run.UserId != userId)
        {
            throw new DomainException(ErrorCodes.RunNotFound, "Run not found.");
        }

        var fingerprint = SettleFingerprint(run, request);
        if (run.Status == PveRunStatus.Settled)
        {
            if (!string.Equals(run.SettleFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new DomainException(ErrorCodes.Conflict, "Run already settled with different payload.");
            }

            var existing = await _session.LoadAsync(userId, cancellationToken);
            return new PveSettleResponse
            {
                AlreadySettled = true,
                GoldGranted = run.GoldGranted,
                Profile = ProfileMapper.ToDto(existing)
            };
        }

        var profile = await _session.LoadAsync(userId, cancellationToken);
        return await SettleUnlockedAsync(run, request, profile, cancellationToken);
    }

    public async Task<PveRunResponse> BuyShopRelicAsync(Guid userId, string runIdText, int relicId, CancellationToken cancellationToken)
    {
        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, runIdText, cancellationToken);
        if (!run.ShopOfferIds.Contains(relicId))
        {
            throw DomainException.Invalid("Relic is not on the shelf.");
        }

        if (run.RelicIds.Contains(relicId))
        {
            throw DomainException.Invalid("Already owned.");
        }

        if (!_config.Tables.TryGetRelic(relicId, out var relic))
        {
            throw DomainException.Invalid($"Unknown relic {relicId}.");
        }

        if (run.RelicIds.Count >= _config.Balance.DefaultRelicNumMax)
        {
            throw DomainException.Invalid("Relic bag is full.");
        }

        var price = PveShopRules.BuyPrice(relic);
        if (run.Gold < price)
        {
            throw new DomainException(ErrorCodes.InsufficientGold, "Not enough run gold.");
        }

        run.Gold -= price;
        run.RelicIds.Add(relicId);
        run.ShopOfferIds.Remove(relicId);
        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
    }

    public async Task<PveRunResponse> SellShopRelicAsync(Guid userId, string runIdText, int relicId, CancellationToken cancellationToken)
    {
        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, runIdText, cancellationToken);
        if (!run.RelicIds.Remove(relicId))
        {
            throw DomainException.Invalid("Relic not owned.");
        }

        if (!_config.Tables.TryGetRelic(relicId, out var relic))
        {
            throw DomainException.Invalid($"Unknown relic {relicId}.");
        }

        run.Gold += PveShopRules.SellPrice(relic);
        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
    }

    public async Task<PveRunResponse> EnterShopAsync(Guid userId, string runIdText, int freeShopRefreshLeft, CancellationToken cancellationToken)
    {
        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, runIdText, cancellationToken);
        run.ShopRefreshCount = 0;
        run.FreeShopRefreshLeft = Math.Max(0, freeShopRefreshLeft);
        lock (_random)
        {
            PveShopRules.RerollOffers(run, _config.Tables, _config.Balance, _random);
        }

        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
    }

    public async Task<PveRunResponse> RefreshShopAsync(Guid userId, string runIdText, CancellationToken cancellationToken)
    {
        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, runIdText, cancellationToken);
        if (run.FreeShopRefreshLeft > 0)
        {
            run.FreeShopRefreshLeft--;
        }
        else
        {
            var cost = PveShopRules.RefreshCost(run, _config.Balance);
            if (run.Gold < cost)
            {
                throw new DomainException(ErrorCodes.InsufficientGold, "Not enough run gold.");
            }

            run.Gold -= cost;
            run.ShopRefreshCount++;
        }

        lock (_random)
        {
            PveShopRules.RerollOffers(run, _config.Tables, _config.Balance, _random);
        }

        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
    }

    public async Task<PveRunResponse> GrantRunGoldAsync(Guid userId, string runIdText, int amount, CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            throw DomainException.Invalid("amount must be positive.");
        }

        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, runIdText, cancellationToken);
        run.Gold += amount;
        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
    }

    public async Task<PveRunResponse> SpendRunGoldAsync(Guid userId, string runIdText, int amount, CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            throw DomainException.Invalid("amount must be positive.");
        }

        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, runIdText, cancellationToken);
        if (run.Gold < amount)
        {
            throw new DomainException(ErrorCodes.InsufficientGold, "Not enough run gold.");
        }

        run.Gold -= amount;
        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
    }

    private async Task<PveSettleResponse> SettleUnlockedAsync(
        PveRun run,
        PveSettleRequest request,
        PlayerProfile profile,
        CancellationToken cancellationToken)
    {
        if (run.CurrentLevelId <= 0)
        {
            run.CurrentLevelId = run.LevelId;
        }

        if (request.Cleared && run.HighestClearedLevelId <= 0)
        {
            throw DomainException.Invalid("No stage cleared.");
        }

        LevelConfig? clearedLevel = null;
        if (run.HighestClearedLevelId > 0)
        {
            if (!_config.Tables.TryGetLevel(run.HighestClearedLevelId, out clearedLevel))
            {
                throw DomainException.Invalid($"Unknown level {run.HighestClearedLevelId}.");
            }

            profile.MarkCleared(clearedLevel, _config);
        }

        var stats = new PveSettleStats
        {
            NumberOfCoinsOwned = run.Gold,
            RefreshStore = run.ShopRefreshCount,
            AccumulateGold = Math.Max(0, run.Gold)
        };
        if (request.Cleared && clearedLevel != null)
        {
            stats.ClearDifficulty = clearedLevel.Difficulty;
        }

        profile.ApplyUnlockStats(stats, _config);
        if (request.Cleared && clearedLevel != null)
        {
            UnlockRules.Apply(
                profile.Unlock,
                new PveSettleStats { ClearDifficulty = clearedLevel.Difficulty },
                _config.Tables.UnlockConditions);
        }

        var clearGold = request.Cleared && clearedLevel != null ? clearedLevel.GetGold : 0;
        var scoreGold = request.Forfeit ? 0 : run.ScoreTotal;
        var gold = profile.GrantSettleGold(scoreGold, _config, clearGold);

        run.Status = PveRunStatus.Settled;
        run.SettledAt = _clock.UtcNow;
        run.SettleFingerprint = SettleFingerprint(run, request);
        run.GoldGranted = gold;
        await _runs.SaveAsync(run, cancellationToken);
        await _session.SaveAsync(profile, cancellationToken);

        return new PveSettleResponse
        {
            AlreadySettled = false,
            GoldGranted = gold,
            Profile = ProfileMapper.ToDto(profile)
        };
    }

    private async Task<PveRun> RequireActiveRun(Guid userId, string runIdText, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(runIdText, out var runId))
        {
            throw DomainException.Invalid("runId is required.");
        }

        var run = await _runs.GetAsync(runId, cancellationToken);
        if (run == null || run.UserId != userId)
        {
            throw new DomainException(ErrorCodes.RunNotFound, "Run not found.");
        }

        if (run.Status != PveRunStatus.Active)
        {
            throw DomainException.Invalid("Run already settled.");
        }

        run.RelicIds ??= new List<int>();
        run.ShopOfferIds ??= new List<int>();
        return run;
    }

    private static string SettleFingerprint(PveRun run, PveSettleRequest request)
    {
        return string.Join("|",
            request.Cleared ? "1" : "0",
            request.Forfeit ? "1" : "0",
            run.ScoreTotal,
            run.HighestClearedLevelId,
            run.CurrentLevelId);
    }
}
