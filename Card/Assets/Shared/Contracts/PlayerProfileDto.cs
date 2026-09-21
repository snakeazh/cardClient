#nullable enable

namespace CardShare.Contracts
{
    public sealed class PlayerProfileDto
    {
        public string UserId { get; set; } = string.Empty;

        public int SaveVersion { get; set; } = 1;

        public string NickName { get; set; } = string.Empty;

        public string AvatarUrl { get; set; } = string.Empty;

        public int Gold { get; set; }

        public EnergyDto Energy { get; set; } = new EnergyDto();

        public AdShopDto AdShop { get; set; } = new AdShopDto();

        public LevelProgressDto Level { get; set; } = new LevelProgressDto();

        public TalentDto Talent { get; set; } = new TalentDto();

        public UnlockDto Unlock { get; set; } = new UnlockDto();

        public int[] GuideCompletedGroupIds { get; set; } = System.Array.Empty<int>();

        public BagEntryDto[] Bag { get; set; } = System.Array.Empty<BagEntryDto>();
    }

    public sealed class BagEntryDto
    {
        public int ItemId { get; set; }

        public int Count { get; set; }
    }

    public sealed class EnergyDto
    {
        public int Current { get; set; }

        public string LastResetAt { get; set; } = string.Empty;

        public int AdRefillCount { get; set; }
    }

    public sealed class AdShopDto
    {
        public int StaminaCount { get; set; }

        public int GoldCount { get; set; }
    }

    public sealed class LevelProgressDto
    {
        public DifficultyProgressDto[] DifficultyProgress { get; set; } = System.Array.Empty<DifficultyProgressDto>();

        public int LastHeroId { get; set; }

        public int LastLevelId { get; set; }

        public int LastDifficulty { get; set; }

        public int[] UnlockedHeroIds { get; set; } = System.Array.Empty<int>();
    }

    public sealed class DifficultyProgressDto
    {
        public int Difficulty { get; set; }

        public int HighestClearedLevel { get; set; }
    }

    public sealed class TalentDto
    {
        public TalentEntryDto[] Entries { get; set; } = System.Array.Empty<TalentEntryDto>();

        public int DrawCount { get; set; }
    }

    public sealed class TalentEntryDto
    {
        public int TalentId { get; set; }

        public int Count { get; set; }
    }

    public sealed class UnlockDto
    {
        public UnlockProgressDto[] Progress { get; set; } = System.Array.Empty<UnlockProgressDto>();
    }

    public sealed class UnlockProgressDto
    {
        public int ConditionId { get; set; }

        public int Amount { get; set; }
    }
}
