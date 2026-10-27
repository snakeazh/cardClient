using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Config;
using CardShare.Domain.Players;
using CardShare.Domain.Pvp;

namespace CardShare.Server.Pvp;

internal static class PvpCombatSeats
{
    public static async Task<SeatSetup[]> LoadAsync(
        IServiceScopeFactory scopes,
        PvpRoom room,
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var tables = scope.ServiceProvider.GetRequiredService<IGameTables>();
        var config = scope.ServiceProvider.GetRequiredService<IGameConfig>();
        var seats = new SeatSetup[room.Players.Count];
        var heroIds = HeroIds(tables);
        var rng = new Random(room.Seed);
        for (var i = 0; i < room.Players.Count; i++)
        {
            var pub = room.Players[i];
            var heroId = heroIds.Count > 0
                ? heroIds[rng.Next(heroIds.Count)]
                : tables.GameConst.DefaultHeroId;
            if (pub.IsBot)
            {
                seats[i] = CombatBonuses.BuildSeat(
                    i,
                    pub.UserId,
                    pub.NickName,
                    heroId,
                    Array.Empty<CombatTalentCount>(),
                    tables);
                seats[i].IsHuman = false;
                seats[i].ShopPoolIds = DefaultRelicIds(tables);
                continue;
            }

            PlayerProfile? profile = null;
            if (Guid.TryParse(pub.UserId, out var userId))
            {
                profile = await players.GetAsync(userId, cancellationToken);
            }

            var shopPoolIds = profile != null ? profile.ShopRelicIds(config) : Array.Empty<int>();
            seats[i] = CombatBonuses.BuildSeat(
                i,
                pub.UserId,
                pub.NickName,
                heroId,
                MapTalents(profile),
                tables);
            seats[i].ShopPoolIds = shopPoolIds;
        }

        foreach (var seat in seats)
        {
            CombatBonuses.ApplyPvpHpBoost(seat, tables);
        }

        return seats;
    }

    /// <summary>可随机到的英雄。同一局里允许重复抽到同一个。</summary>
    private static List<int> HeroIds(IGameTables tables)
    {
        var ids = new List<int>();
        foreach (var hero in tables.Heroes)
        {
            if (hero != null && hero.Id > 0)
            {
                ids.Add(hero.Id);
            }
        }

        return ids;
    }

    private static int[] DefaultRelicIds(IGameTables tables)
    {
        var ids = new List<int>();
        foreach (var relic in tables.PvpRelics)
        {
            if (relic.Id > 0)
            {
                ids.Add(relic.Id);
            }
        }

        ids.Sort();
        return ids.ToArray();
    }

    private static CombatTalentCount[] MapTalents(PlayerProfile? profile)
    {
        if (profile == null)
        {
            return Array.Empty<CombatTalentCount>();
        }

        var entries = profile.Talent.Entries;
        var list = new List<CombatTalentCount>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.TalentId <= 0 || entry.Count <= 0)
            {
                continue;
            }

            list.Add(new CombatTalentCount { TalentId = entry.TalentId, Count = entry.Count });
        }

        return list.ToArray();
    }
}
