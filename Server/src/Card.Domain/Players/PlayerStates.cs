namespace CardShare.Domain.Players;

public sealed class EnergyState
{
    public int Current { get; set; }

    public DateTimeOffset LastResetAt { get; set; }

    public int AdRefillCount { get; set; }
}

public sealed class AdShopState
{
    public int StaminaCount { get; set; }

    public int GoldCount { get; set; }
}

public sealed class LevelProgressState
{
    public List<DifficultyProgressEntry> DifficultyProgress { get; set; } = new List<DifficultyProgressEntry>();

    public int LastHeroId { get; set; }

    public int LastLevelId { get; set; }

    public int LastDifficulty { get; set; }

    public List<int> UnlockedHeroIds { get; set; } = new List<int>();

    public int GetHighestCleared(int difficulty)
    {
        foreach (var entry in DifficultyProgress)
        {
            if (entry.Difficulty == difficulty)
            {
                return entry.HighestClearedLevel;
            }
        }

        return 0;
    }

    public void SetHighestCleared(int difficulty, int level)
    {
        foreach (var entry in DifficultyProgress)
        {
            if (entry.Difficulty == difficulty)
            {
                entry.HighestClearedLevel = level;
                return;
            }
        }

        DifficultyProgress.Add(new DifficultyProgressEntry
        {
            Difficulty = difficulty,
            HighestClearedLevel = level
        });
    }

    public bool TryUnlockHero(int heroId)
    {
        if (heroId <= 0 || UnlockedHeroIds.Contains(heroId))
        {
            return false;
        }

        UnlockedHeroIds.Add(heroId);
        return true;
    }
}

public sealed class DifficultyProgressEntry
{
    public int Difficulty { get; set; }

    public int HighestClearedLevel { get; set; }
}

public sealed class TalentState
{
    public List<TalentSaveEntry> Entries { get; set; } = new List<TalentSaveEntry>();

    public int DrawCount { get; set; }

    public TalentSaveEntry GetOrAdd(int talentId)
    {
        foreach (var entry in Entries)
        {
            if (entry.TalentId == talentId)
            {
                return entry;
            }
        }

        var created = new TalentSaveEntry { TalentId = talentId, Count = 0 };
        Entries.Add(created);
        return created;
    }

    public int GetCount(int talentId)
    {
        foreach (var entry in Entries)
        {
            if (entry.TalentId == talentId)
            {
                return entry.Count;
            }
        }

        return 0;
    }
}

public sealed class TalentSaveEntry
{
    public int TalentId { get; set; }

    public int Count { get; set; }
}

public sealed class UnlockState
{
    public List<UnlockProgressEntry> Progress { get; set; } = new List<UnlockProgressEntry>();

    public int GetAmount(int conditionId)
    {
        foreach (var entry in Progress)
        {
            if (entry.ConditionId == conditionId)
            {
                return entry.Amount;
            }
        }

        return 0;
    }

    public void SetAmount(int conditionId, int amount)
    {
        foreach (var entry in Progress)
        {
            if (entry.ConditionId == conditionId)
            {
                entry.Amount = amount;
                return;
            }
        }

        Progress.Add(new UnlockProgressEntry { ConditionId = conditionId, Amount = amount });
    }
}

public sealed class UnlockProgressEntry
{
    public int ConditionId { get; set; }

    public int Amount { get; set; }
}

public sealed class BagState
{
    public List<BagEntryState> Entries { get; set; } = new List<BagEntryState>();

    public int GetCount(int itemId)
    {
        foreach (var entry in Entries)
        {
            if (entry.ItemId == itemId)
            {
                return entry.Count;
            }
        }

        return 0;
    }

    public void Add(int itemId, int amount)
    {
        foreach (var entry in Entries)
        {
            if (entry.ItemId == itemId)
            {
                entry.Count += amount;
                return;
            }
        }

        Entries.Add(new BagEntryState { ItemId = itemId, Count = amount });
    }

    public bool TryRemove(int itemId, int amount)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].ItemId != itemId)
            {
                continue;
            }

            if (Entries[i].Count < amount)
            {
                return false;
            }

            Entries[i].Count -= amount;
            if (Entries[i].Count <= 0)
            {
                Entries.RemoveAt(i);
            }

            return true;
        }

        return false;
    }
}

public sealed class BagEntryState
{
    public int ItemId { get; set; }

    public int Count { get; set; }
}
