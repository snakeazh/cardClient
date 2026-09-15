namespace CardShare.Contracts;

public sealed class PveStartRequest
{
    public int LevelId { get; set; }

    public int HeroId { get; set; }
}

public sealed class PveStartResponse
{
    public string RunId { get; set; } = string.Empty;

    public int LevelId { get; set; }

    public int HeroId { get; set; }

    public int[] ShopRelicIds { get; set; } = System.Array.Empty<int>();

    public PveRunDto Run { get; set; } = new PveRunDto();

    public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
}

public sealed class PveRunDto
{
    public string RunId { get; set; } = string.Empty;

    public int LevelId { get; set; }

    public int HeroId { get; set; }

    public int Gold { get; set; }

    public int[] RelicIds { get; set; } = System.Array.Empty<int>();

    public int[] ShopOfferIds { get; set; } = System.Array.Empty<int>();

    public int[] ShopRelicIds { get; set; } = System.Array.Empty<int>();

    public int ShopRefreshCount { get; set; }

    public int FreeShopRefreshLeft { get; set; }

    public string Status { get; set; } = "active";
}

public sealed class PveShopActionRequest
{
    public string RunId { get; set; } = string.Empty;

    public int RelicId { get; set; }
}

public sealed class PveShopEnterRequest
{
    public string RunId { get; set; } = string.Empty;

    public int FreeShopRefreshLeft { get; set; }
}

public sealed class PveRunGoldRequest
{
    public string RunId { get; set; } = string.Empty;

    public int Amount { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class PveRunSpendRequest
{
    public string RunId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public int Amount { get; set; }
}

public sealed class PveRunResponse
{
    public PveRunDto Run { get; set; } = new PveRunDto();
}

public sealed class PveSettleRequest
{
    public string RunId { get; set; } = string.Empty;

    public bool Cleared { get; set; }

    public int LevelId { get; set; }

    public int TotalScore { get; set; }

    public PveSettleStats Stats { get; set; } = new PveSettleStats();
}

public sealed class PveSettleStats
{
    public int KillMonster { get; set; }

    public int ShuffleCard { get; set; }

    public int RefreshStore { get; set; }

    public int Straight { get; set; }

    public int TwoThreeFive { get; set; }

    public int ShuffleCardAndVictory { get; set; }

    public int Seven { get; set; }

    public int Flush { get; set; }

    public int ClearDifficulty { get; set; }

    public int AccumulateGold { get; set; }

    public int SingleDamage { get; set; }

    public int Couplet { get; set; }

    public int Failure { get; set; }

    public int Defeat { get; set; }

    public int LuxuryGoods { get; set; }

    public int Angel { get; set; }

    public int DeathNum { get; set; }

    public int Perspective { get; set; }

    public int OneDamage { get; set; }

    public int NumberOfCoinsOwned { get; set; }

    public int CriticalNum { get; set; }

    public int ThreeCardAttack { get; set; }
}

public sealed class PveSettleResponse
{
    public bool AlreadySettled { get; set; }

    public int GoldGranted { get; set; }

    public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
}
