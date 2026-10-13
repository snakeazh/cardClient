using CardShare.Contracts;
using CardShare.Contracts.Config;
using CardShare.Domain.Config;

namespace CardShare.Domain.Pve;

public static class PveRunMapper
{
    public static PveRunDto ToDto(PveRun run)
    {
        return new PveRunDto
        {
            RunId = run.RunId.ToString("N"),
            LevelId = run.LevelId,
            HeroId = run.HeroId,
            Gold = run.Gold,
            RelicIds = run.RelicIds.ToArray(),
            ShopOfferIds = run.ShopOfferIds.ToArray(),
            ShopRelicIds = run.ShopRelicIds,
            ShopRefreshCount = run.ShopRefreshCount,
            FreeShopRefreshLeft = run.FreeShopRefreshLeft,
            Status = run.Status == PveRunStatus.Settled ? "settled" : "active"
        };
    }
}

public static class PveShopRules
{
    public static int RefreshCost(PveRun run, GameBalance balance)
    {
        var first = Math.Max(0, balance.ShopRefreshFirst);
        var after = Math.Max(0, balance.ShopRefreshAfter);
        var maxUp = balance.ShopRefreshGoldUpNumMax > 0 ? balance.ShopRefreshGoldUpNumMax : int.MaxValue;
        var ups = Math.Min(Math.Max(0, run.ShopRefreshCount), maxUp);
        return first + ups * after;
    }

    public static void FillOffers(PveRun run, IGameTables tables, GameBalance balance, Random rng)
    {
        var target = Math.Max(1, balance.ShopOfferCount);
        while (run.ShopOfferIds.Count > target)
        {
            run.ShopOfferIds.RemoveAt(run.ShopOfferIds.Count - 1);
        }

        var pool = BuildPool(run, tables);
        while (run.ShopOfferIds.Count < target && pool.Count > 0)
        {
            var pick = PickWeighted(pool, rng);
            if (pick == null)
            {
                break;
            }

            run.ShopOfferIds.Add(pick.Id);
            pool.Remove(pick);
        }
    }

    public static void RerollOffers(PveRun run, IGameTables tables, GameBalance balance, Random rng)
    {
        run.ShopOfferIds = new List<int>();
        FillOffers(run, tables, balance, rng);
    }

    public static int BuyPrice(RelicConfig relic) => Math.Max(0, relic.Price);

    public static int SellPrice(RelicConfig relic) => Math.Max(0, relic.SellingPrice);

    private static List<RelicConfig> BuildPool(PveRun run, IGameTables tables)
    {
        var unlocked = new HashSet<int>(run.ShopRelicIds);
        var owned = new HashSet<int>(run.RelicIds);
        var onShelf = new HashSet<int>(run.ShopOfferIds);
        var pool = new List<RelicConfig>();
        foreach (var relic in tables.Relics)
        {
            if (relic.Id <= 0 || relic.RefreshProbability <= 0f)
            {
                continue;
            }

            if (!unlocked.Contains(relic.Id) || owned.Contains(relic.Id) || onShelf.Contains(relic.Id))
            {
                continue;
            }

            pool.Add(relic);
        }

        return pool;
    }

    private static RelicConfig? PickWeighted(List<RelicConfig> pool, Random rng)
    {
        if (pool.Count == 0)
        {
            return null;
        }

        var total = 0f;
        for (var i = 0; i < pool.Count; i++)
        {
            total += Math.Max(0f, pool[i].RefreshProbability);
        }

        if (total <= 0f)
        {
            return pool[rng.Next(pool.Count)];
        }

        var roll = (float)rng.NextDouble() * total;
        var acc = 0f;
        for (var i = 0; i < pool.Count; i++)
        {
            acc += Math.Max(0f, pool[i].RefreshProbability);
            if (roll <= acc)
            {
                return pool[i];
            }
        }

        return pool[pool.Count - 1];
    }
}
