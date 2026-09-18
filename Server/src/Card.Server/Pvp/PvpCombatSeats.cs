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
        for (var i = 0; i < room.Players.Count; i++)
        {
            var pub = room.Players[i];
            if (pub.IsBot)
            {
                seats[i] = CombatBonuses.BuildSeat(
                    i,
                    pub.UserId,
                    pub.NickName,
                    0,
                    Array.Empty<CombatTalentCount>(),
                    tables);
                seats[i].IsHuman = false;
                continue;
            }

            PlayerProfile? profile = null;
            if (Guid.TryParse(pub.UserId, out var userId))
            {
                profile = await players.GetAsync(userId, cancellationToken);
            }

            var heroId = 0;
            var relicIds = Array.Empty<int>();
            if (profile != null)
            {
                heroId = profile.Level.LastHeroId;
                relicIds = profile.ShopRelicIds(config);
            }

            seats[i] = CombatBonuses.BuildSeat(
                i,
                pub.UserId,
                pub.NickName,
                heroId,
                MapTalents(profile),
                tables,
                relicIds);
        }

        return seats;
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
