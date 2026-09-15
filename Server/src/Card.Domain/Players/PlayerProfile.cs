using CardShare.Contracts;
using CardShare.Contracts.Config;
using CardShare.Domain.Config;

namespace CardShare.Domain.Players;

public sealed class PlayerProfile
{
    public Guid UserId { get; set; }

    public int SaveVersion { get; set; } = 1;

    public string NickName { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;

    public int Gold { get; set; }

    public EnergyState Energy { get; set; } = new EnergyState();

    public AdShopState AdShop { get; set; } = new AdShopState();

    public LevelProgressState Level { get; set; } = new LevelProgressState();

    public TalentState Talent { get; set; } = new TalentState();

    public UnlockState Unlock { get; set; } = new UnlockState();

    public BagState Bag { get; set; } = new BagState();

    public int[] GuideCompletedGroupIds { get; set; } = Array.Empty<int>();

    public static PlayerProfile CreateNew(Guid userId, IGameConfig config, DateTimeOffset utcNow)
    {
        var profile = new PlayerProfile
        {
            UserId = userId,
            Energy =
            {
                Current = config.Balance.EnergyMax,
                LastResetAt = utcNow
            }
        };
        profile.UnlockDefaultHeroes(config);
        profile.Level.LastHeroId = config.Balance.DefaultHeroId;
        profile.Level.LastDifficulty = config.Tables.Difficulties.Count > 0 ? config.Tables.Difficulties[0] : 1;
        return profile;
    }

    public void ApplyUserInfo(LoginUserInfo? userInfo)
    {
        if (userInfo == null)
        {
            return;
        }

        var nick = AvatarUrlPolicy.NormalizeNickName(userInfo.NickName);
        if (!string.IsNullOrEmpty(nick))
        {
            NickName = nick;
        }

        if (AvatarUrlPolicy.TryNormalize(userInfo.AvatarUrl, out var avatar))
        {
            AvatarUrl = avatar;
        }
    }

    public void EnsureDailyReset(IGameConfig config, DateTimeOffset utcNow)
    {
        var today = GameDay.Of(utcNow, config.TimeZone, config.Balance.EnergyDailyResetHour);
        var last = GameDay.Of(Energy.LastResetAt, config.TimeZone, config.Balance.EnergyDailyResetHour);
        if (today == last)
        {
            return;
        }

        Energy.Current = config.Balance.EnergyMax;
        Energy.AdRefillCount = 0;
        Energy.LastResetAt = utcNow;
        AdShop.StaminaCount = 0;
        AdShop.GoldCount = 0;
    }

    public void SpendEnergyForRun(IGameConfig config)
    {
        var cost = config.Balance.EnergyCostPerRun;
        if (Energy.Current < cost)
        {
            throw new DomainException(ErrorCodes.InsufficientEnergy, "Not enough energy.");
        }

        Energy.Current -= cost;
    }

    public void RefillEnergyByAd(IGameConfig config)
    {
        if (config.Balance.EnergyAdRefillDailyLimit <= 0
            || Energy.AdRefillCount >= config.Balance.EnergyAdRefillDailyLimit)
        {
            throw new DomainException(ErrorCodes.AdLimitReached, "Daily energy ad refill limit reached.");
        }

        Energy.Current = config.Balance.EnergyMax;
        Energy.AdRefillCount++;
    }

    public void ClaimAdShop(string kind, IGameConfig config)
    {
        if (string.Equals(kind, "stamina", StringComparison.OrdinalIgnoreCase))
        {
            if (config.Balance.AdShopStaminaDailyLimit <= 0
                || AdShop.StaminaCount >= config.Balance.AdShopStaminaDailyLimit)
            {
                throw new DomainException(ErrorCodes.AdLimitReached, "Daily ad-shop stamina limit reached.");
            }

            Energy.Current += config.Balance.AdShopStaminaPerBuy;
            AdShop.StaminaCount++;
            return;
        }

        if (string.Equals(kind, "gold", StringComparison.OrdinalIgnoreCase))
        {
            if (config.Balance.AdShopGoldDailyLimit <= 0
                || AdShop.GoldCount >= config.Balance.AdShopGoldDailyLimit)
            {
                throw new DomainException(ErrorCodes.AdLimitReached, "Daily ad-shop gold limit reached.");
            }

            Gold += config.Balance.AdShopGoldPerBuy;
            AdShop.GoldCount++;
            return;
        }

        throw DomainException.Invalid("Ad-shop kind must be stamina or gold.");
    }

    public void GrantBagItem(int itemId, int amount, IGameConfig config)
    {
        if (amount <= 0)
        {
            throw DomainException.Invalid("amount must be positive.");
        }

        if (config == null || !config.Tables.TryGetItem(itemId, out _))
        {
            throw DomainException.Invalid($"Unknown item {itemId}.");
        }

        Bag.Add(itemId, amount);
    }

    public void ConsumeBagItem(int itemId, int amount, IGameConfig config)
    {
        if (amount <= 0)
        {
            throw DomainException.Invalid("amount must be positive.");
        }

        if (config == null || !config.Tables.TryGetItem(itemId, out _))
        {
            throw DomainException.Invalid($"Unknown item {itemId}.");
        }

        if (!Bag.TryRemove(itemId, amount))
        {
            throw DomainException.Invalid("Not enough items.");
        }
    }

    public void CompleteGuideGroup(int groupId)
    {
        if (groupId <= 0)
        {
            throw DomainException.Invalid("groupId is required.");
        }

        if (GuideCompletedGroupIds != null)
        {
            for (var i = 0; i < GuideCompletedGroupIds.Length; i++)
            {
                if (GuideCompletedGroupIds[i] == groupId)
                {
                    return;
                }
            }
        }

        var current = GuideCompletedGroupIds ?? Array.Empty<int>();
        var next = new int[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = groupId;
        GuideCompletedGroupIds = next;
    }

    public void GrantWalletGold(int amount)
    {
        if (amount <= 0)
        {
            throw DomainException.Invalid("amount must be positive.");
        }

        Gold += amount;
    }

    public int GetDrawCost(IGameConfig config)
    {
        return config.Balance.TalentChestNeedGold
               + config.Balance.TalentNeedChestGold * Talent.DrawCount;
    }

    public TalentDrawResult DrawTalent(int talentId, IGameConfig config)
    {
        var cost = GetDrawCost(config);
        if (Gold < cost)
        {
            throw new DomainException(ErrorCodes.InsufficientGold, "Not enough gold.");
        }

        if (config.Tables.GetTalentMaxLevel(talentId) <= 0 && !config.Tables.TalentRows.Any(t => t.TalentId == talentId))
        {
            throw DomainException.Invalid($"Unknown talent {talentId}.");
        }

        var entry = Talent.GetOrAdd(talentId);
        var maxLevel = config.Tables.GetTalentMaxLevel(talentId);
        var level = TalentLevel(entry.Count, maxLevel, config.Balance.CopiesPerLevel);
        if (maxLevel > 0 && level >= maxLevel)
        {
            throw new DomainException(ErrorCodes.TalentPoolEmpty, "Talent is already maxed.");
        }

        Gold -= cost;
        entry.Count++;
        Talent.DrawCount++;
        return new TalentDrawResult(talentId, entry.Count, Talent.DrawCount, cost);
    }

    public bool IsDifficultyUnlocked(int difficulty, IGameConfig config)
    {
        var diffs = config.Tables.Difficulties;
        if (diffs.Count == 0)
        {
            return false;
        }

        if (difficulty == diffs[0])
        {
            return true;
        }

        for (var i = 1; i < diffs.Count; i++)
        {
            if (diffs[i] == difficulty)
            {
                return IsDifficultyCleared(diffs[i - 1], config);
            }
        }

        return false;
    }

    public bool IsDifficultyCleared(int difficulty, IGameConfig config)
    {
        var max = config.Tables.GetMaxLevel(difficulty);
        return max > 0 && Level.GetHighestCleared(difficulty) >= max;
    }

    public bool IsLevelUnlocked(int levelId, IGameConfig config)
    {
        if (!config.Tables.TryGetLevel(levelId, out var snapshot))
        {
            return false;
        }

        if (!IsDifficultyUnlocked(snapshot.Difficulty, config))
        {
            return false;
        }

        if (snapshot.Level <= 1)
        {
            return true;
        }

        return Level.GetHighestCleared(snapshot.Difficulty) >= snapshot.Level - 1;
    }

    public bool IsHeroUnlocked(int heroId, IGameConfig config)
    {
        if (Level.UnlockedHeroIds.Contains(heroId))
        {
            return true;
        }

        var hero = config.Tables.Heroes.FirstOrDefault(h => h.Id == heroId);
        if (hero == null)
        {
            return false;
        }

        return hero.UnlockCondition <= 0 || IsDifficultyCleared(hero.UnlockCondition, config);
    }

    public void MarkCleared(LevelConfig snapshot, IGameConfig config)
    {
        var prev = Level.GetHighestCleared(snapshot.Difficulty);
        if (snapshot.Level > prev)
        {
            Level.SetHighestCleared(snapshot.Difficulty, snapshot.Level);
        }

        if (IsDifficultyCleared(snapshot.Difficulty, config))
        {
            UnlockHeroesForDifficulty(snapshot.Difficulty, config);
        }
    }

    public void ApplyUnlockStats(PveSettleStats stats, IGameConfig config)
    {
        UnlockRules.Apply(Unlock, stats, config.Tables.UnlockConditions);
    }

    public int GrantSettleGold(int totalScore, IGameConfig config, int levelClearGold = 0)
    {
        var gold = PointsToGold(totalScore, config) + Math.Max(0, levelClearGold);
        Gold += gold;
        return gold;
    }

    public static int PointsToGold(int totalScore, IGameConfig config)
    {
        if (totalScore <= 0)
        {
            return 0;
        }

        var rate = config.Balance.ExchangePointsForGoldCoins;
        if (rate <= 0)
        {
            rate = 10;
        }

        return totalScore / rate;
    }

    public int[] ShopRelicIds(IGameConfig config)
    {
        var ids = new List<int>();
        foreach (var relic in config.Tables.Relics)
        {
            if (IsRelicUnlocked(relic, config))
            {
                ids.Add(relic.Id);
            }
        }

        ids.Sort();
        return ids.ToArray();
    }

    public bool IsRelicUnlocked(RelicConfig relic, IGameConfig config)
    {
        if (relic.UnlockConditionId <= 0)
        {
            return true;
        }

        var condition = config.Tables.UnlockConditions.FirstOrDefault(c => c.Id == relic.UnlockConditionId);
        if (condition == null)
        {
            return false;
        }

        return Unlock.GetAmount(condition.Id) >= condition.Value;
    }

    private void UnlockDefaultHeroes(IGameConfig config)
    {
        foreach (var hero in config.Tables.Heroes)
        {
            if (hero.UnlockCondition <= 0)
            {
                Level.TryUnlockHero(hero.Id);
            }
        }

        if (config.Balance.DefaultHeroId > 0)
        {
            Level.TryUnlockHero(config.Balance.DefaultHeroId);
        }
    }

    private void UnlockHeroesForDifficulty(int difficulty, IGameConfig config)
    {
        foreach (var hero in config.Tables.Heroes)
        {
            if (hero.UnlockCondition == difficulty)
            {
                Level.TryUnlockHero(hero.Id);
            }
        }
    }

    private static int TalentLevel(int count, int maxLevel, int copiesPerLevel)
    {
        if (maxLevel <= 0 || copiesPerLevel <= 0)
        {
            return 0;
        }

        var raw = count / copiesPerLevel;
        return raw > maxLevel ? maxLevel : raw;
    }
}

public readonly struct TalentDrawResult
{
    public TalentDrawResult(int talentId, int count, int drawCount, int goldSpent)
    {
        TalentId = talentId;
        Count = count;
        DrawCount = drawCount;
        GoldSpent = goldSpent;
    }

    public int TalentId { get; }

    public int Count { get; }

    public int DrawCount { get; }

    public int GoldSpent { get; }
}
