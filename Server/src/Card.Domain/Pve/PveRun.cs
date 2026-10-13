namespace CardShare.Domain.Pve;

public enum PveRunStatus
{
    Active = 0,
    Settled = 1
}

public sealed class PveRun
{
    public Guid RunId { get; set; }

    public Guid UserId { get; set; }

    public int LevelId { get; set; }

    public int CurrentLevelId { get; set; }

    public int HighestClearedLevelId { get; set; }

    public int ScoreTotal { get; set; }

    public int ScoredLevelId { get; set; }

    public int HeroId { get; set; }

    public int[] ShopRelicIds { get; set; } = Array.Empty<int>();

    public int Gold { get; set; }

    public List<int> RelicIds { get; set; } = new List<int>();

    public List<int> ShopOfferIds { get; set; } = new List<int>();

    public int ShopRefreshCount { get; set; }

    public int FreeShopRefreshLeft { get; set; }

    public PveRunStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    public string? SettleFingerprint { get; set; }

    public int GoldGranted { get; set; }
}
