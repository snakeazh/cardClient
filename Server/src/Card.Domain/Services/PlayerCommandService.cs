using CardShare.Contracts;
using CardShare.Domain.Config;
using CardShare.Domain.Players;
using CardShare.Domain.Pve;

namespace CardShare.Domain.Services;

public sealed class PlayerCommandService
{
    private readonly IPlayerRepository _players;
    private readonly IPveRunRepository _runs;
    private readonly IGameConfig _config;
    private readonly IClock _clock;
    private readonly IPlayerLock _locks;
    private readonly Random _random = new Random();

    public PlayerCommandService(
        IPlayerRepository players,
        IPveRunRepository runs,
        IGameConfig config,
        IClock clock,
        IPlayerLock locks)
    {
        _players = players;
        _runs = runs;
        _config = config;
        _clock = clock;
        _locks = locks;
    }

    public async Task<PlayerProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        await _players.SaveAsync(profile, cancellationToken);
        return ProfileMapper.ToDto(profile);
    }

    public async Task<TalentDrawResponse> DrawTalentAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        var talentId = PickTalent(profile);
        var result = profile.DrawTalent(talentId, _config);
        await _players.SaveAsync(profile, cancellationToken);
        return new TalentDrawResponse
        {
            TalentId = result.TalentId,
            Count = result.Count,
            DrawCount = result.DrawCount,
            GoldSpent = result.GoldSpent,
            Profile = ProfileMapper.ToDto(profile)
        };
    }

    public async Task<EnergyRefillResponse> RefillEnergyByAdAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        profile.RefillEnergyByAd(_config);
        await _players.SaveAsync(profile, cancellationToken);
        return new EnergyRefillResponse { Profile = ProfileMapper.ToDto(profile) };
    }

    public async Task<AdShopClaimResponse> ClaimAdShopAsync(Guid userId, string kind, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        profile.ClaimAdShop(kind, _config);
        await _players.SaveAsync(profile, cancellationToken);
        return new AdShopClaimResponse { Profile = ProfileMapper.ToDto(profile) };
    }

    public async Task<PlayerProfileDto> GrantBagAsync(Guid userId, int itemId, int amount, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        profile.GrantBagItem(itemId, amount, _config);
        await _players.SaveAsync(profile, cancellationToken);
        return ProfileMapper.ToDto(profile);
    }

    public async Task<PlayerProfileDto> ConsumeBagAsync(Guid userId, int itemId, int amount, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        profile.ConsumeBagItem(itemId, amount, _config);
        await _players.SaveAsync(profile, cancellationToken);
        return ProfileMapper.ToDto(profile);
    }

    public async Task<PlayerProfileDto> CompleteGuideAsync(Guid userId, int groupId, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        profile.CompleteGuideGroup(groupId);
        await _players.SaveAsync(profile, cancellationToken);
        return ProfileMapper.ToDto(profile);
    }

    public async Task<PlayerProfileDto> DebugGrantGoldAsync(Guid userId, int amount, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
        profile.GrantWalletGold(amount);
        await _players.SaveAsync(profile, cancellationToken);
        return ProfileMapper.ToDto(profile);
    }

    public async Task<PveStartResponse> StartPveAsync(Guid userId, PveStartRequest request, CancellationToken cancellationToken)
    {
        if (request == null || request.LevelId <= 0)
        {
            throw DomainException.Invalid("levelId is required.");
        }

        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var profile = await LoadAsync(userId, cancellationToken);
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
        await _players.SaveAsync(profile, cancellationToken);

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

        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var run = await _runs.GetAsync(runId, cancellationToken);
        if (run == null || run.UserId != userId)
        {
            throw new DomainException(ErrorCodes.RunNotFound, "Run not found.");
        }

        var fingerprint = SettleFingerprint(request);
        if (run.Status == PveRunStatus.Settled)
        {
            if (!string.Equals(run.SettleFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new DomainException(ErrorCodes.Conflict, "Run already settled with different payload.");
            }

            var existing = await LoadAsync(userId, cancellationToken);
            return new PveSettleResponse
            {
                AlreadySettled = true,
                GoldGranted = run.GoldGranted,
                Profile = ProfileMapper.ToDto(existing)
            };
        }

        if (request.LevelId > 0 && request.LevelId != run.LevelId)
        {
            if (!_config.Tables.TryGetLevel(run.LevelId, out var startLevel) ||
                !_config.Tables.TryGetLevel(request.LevelId, out var reportedLevel) ||
                startLevel.Difficulty != reportedLevel.Difficulty)
            {
                throw new DomainException(ErrorCodes.RunMismatch, "Settle level does not match the run.");
            }
        }

        var settleLevelId = request.LevelId > 0 ? request.LevelId : run.LevelId;
        if (!_config.Tables.TryGetLevel(settleLevelId, out var level))
        {
            throw DomainException.Invalid($"Unknown level {settleLevelId}.");
        }

        var profile = await LoadAsync(userId, cancellationToken);
        if (request.Cleared)
        {
            profile.MarkCleared(level, _config);
        }

        profile.ApplyUnlockStats(request.Stats, _config);
        if (request.Cleared)
        {
            UnlockRules.Apply(
                profile.Unlock,
                new PveSettleStats { ClearDifficulty = level.Difficulty },
                _config.Tables.UnlockConditions);
        }
        var gold = profile.GrantSettleGold(request.TotalScore, _config, request.Cleared ? level.GetGold : 0);

        run.Status = PveRunStatus.Settled;
        run.SettledAt = _clock.UtcNow;
        run.SettleFingerprint = fingerprint;
        run.GoldGranted = gold;
        await _runs.SaveAsync(run, cancellationToken);
        await _players.SaveAsync(profile, cancellationToken);

        return new PveSettleResponse
        {
            AlreadySettled = false,
            GoldGranted = gold,
            Profile = ProfileMapper.ToDto(profile)
        };
    }

    public async Task<PveRunResponse> BuyShopRelicAsync(Guid userId, string runIdText, int relicId, CancellationToken cancellationToken)
    {
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
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
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
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
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
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
        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
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

        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
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

        await using var gate = await _locks.AcquireAsync(userId, cancellationToken);
        var run = await RequireActiveRun(userId, runIdText, cancellationToken);
        if (run.Gold < amount)
        {
            throw new DomainException(ErrorCodes.InsufficientGold, "Not enough run gold.");
        }

        run.Gold -= amount;
        await _runs.SaveAsync(run, cancellationToken);
        return new PveRunResponse { Run = PveRunMapper.ToDto(run) };
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

    private async Task<PlayerProfile> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await _players.GetAsync(userId, cancellationToken);
        if (profile == null)
        {
            throw DomainException.Unauthorized("Player not found.");
        }

        profile.EnsureDailyReset(_config, _clock.UtcNow);
        return profile;
    }

    private int PickTalent(PlayerProfile profile)
    {
        var pool = new List<int>();
        foreach (var talentId in _config.Tables.TalentRows.Select(t => t.TalentId).Distinct())
        {
            var count = profile.Talent.GetCount(talentId);
            var copies = _config.Balance.CopiesPerLevel <= 0 ? 1 : _config.Balance.CopiesPerLevel;
            var maxLevel = _config.Tables.GetTalentMaxLevel(talentId);
            var level = maxLevel <= 0 ? 0 : Math.Min(maxLevel, count / copies);
            if (maxLevel <= 0 || level < maxLevel)
            {
                pool.Add(talentId);
            }
        }

        if (pool.Count == 0)
        {
            throw new DomainException(ErrorCodes.TalentPoolEmpty, "All talents are maxed.");
        }

        lock (_random)
        {
            return pool[_random.Next(pool.Count)];
        }
    }

    private static string SettleFingerprint(PveSettleRequest request)
    {
        var s = request.Stats;
        return string.Join("|",
            request.Cleared ? "1" : "0",
            request.LevelId,
            request.TotalScore,
            s.KillMonster, s.ShuffleCard, s.RefreshStore, s.Straight, s.TwoThreeFive,
            s.ShuffleCardAndVictory, s.Seven, s.Flush, s.ClearDifficulty, s.AccumulateGold,
            s.SingleDamage, s.Couplet, s.Failure, s.Defeat, s.LuxuryGoods, s.Angel,
            s.DeathNum, s.Perspective, s.OneDamage, s.NumberOfCoinsOwned, s.CriticalNum,
            s.ThreeCardAttack);
    }
}
