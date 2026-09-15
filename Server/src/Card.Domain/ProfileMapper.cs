using CardShare.Contracts;
using CardShare.Domain.Players;

namespace CardShare.Domain;

public static class ProfileMapper
{
    public static PlayerProfileDto ToDto(PlayerProfile profile)
    {
        return new PlayerProfileDto
        {
            UserId = profile.UserId.ToString("N"),
            SaveVersion = profile.SaveVersion,
            NickName = profile.NickName ?? string.Empty,
            AvatarUrl = profile.AvatarUrl ?? string.Empty,
            Gold = profile.Gold,
            Energy = new EnergyDto
            {
                Current = profile.Energy.Current,
                LastResetAt = profile.Energy.LastResetAt.ToUniversalTime().ToString("O"),
                AdRefillCount = profile.Energy.AdRefillCount
            },
            AdShop = new AdShopDto
            {
                StaminaCount = profile.AdShop.StaminaCount,
                GoldCount = profile.AdShop.GoldCount
            },
            Level = new LevelProgressDto
            {
                DifficultyProgress = profile.Level.DifficultyProgress
                    .Select(e => new DifficultyProgressDto
                    {
                        Difficulty = e.Difficulty,
                        HighestClearedLevel = e.HighestClearedLevel
                    })
                    .ToArray(),
                LastHeroId = profile.Level.LastHeroId,
                LastLevelId = profile.Level.LastLevelId,
                LastDifficulty = profile.Level.LastDifficulty,
                UnlockedHeroIds = profile.Level.UnlockedHeroIds.ToArray()
            },
            Talent = new TalentDto
            {
                Entries = profile.Talent.Entries
                    .Select(e => new TalentEntryDto { TalentId = e.TalentId, Count = e.Count })
                    .ToArray(),
                DrawCount = profile.Talent.DrawCount
            },
            Unlock = new UnlockDto
            {
                Progress = profile.Unlock.Progress
                    .Select(e => new UnlockProgressDto { ConditionId = e.ConditionId, Amount = e.Amount })
                    .ToArray()
            },
            GuideCompletedGroupIds = profile.GuideCompletedGroupIds ?? Array.Empty<int>(),
            Bag = (profile.Bag?.Entries ?? new List<CardShare.Domain.Players.BagEntryState>())
                .Where(e => e != null && e.Count > 0)
                .Select(e => new BagEntryDto { ItemId = e.ItemId, Count = e.Count })
                .ToArray()
        };
    }

    public static PlayerPublic ToPublic(PlayerProfile profile)
    {
        return new PlayerPublic
        {
            UserId = profile.UserId.ToString("N"),
            NickName = profile.NickName ?? string.Empty,
            AvatarUrl = profile.AvatarUrl ?? string.Empty
        };
    }
}
