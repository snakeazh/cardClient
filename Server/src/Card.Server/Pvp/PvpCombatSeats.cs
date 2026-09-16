using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain;
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
        var seats = new SeatSetup[room.Players.Count];
        for (var i = 0; i < room.Players.Count; i++)
        {
            var pub = room.Players[i];
            PlayerProfile? profile = null;
            if (pub != null && Guid.TryParse(pub.UserId, out var userId))
            {
                profile = await players.GetAsync(userId, cancellationToken);
            }

            seats[i] = CombatBonuses.BuildSeat(
                i,
                pub?.UserId ?? string.Empty,
                pub?.NickName ?? string.Empty,
                profile?.Level.LastHeroId ?? 0,
                MapTalents(profile),
                tables);
        }

        return seats;
    }

    private static CombatTalentCount[] MapTalents(PlayerProfile? profile)
    {
        var entries = profile?.Talent?.Entries;
        if (entries == null || entries.Count == 0)
        {
            return Array.Empty<CombatTalentCount>();
        }

        var list = new List<CombatTalentCount>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry == null || entry.TalentId <= 0 || entry.Count <= 0)
            {
                continue;
            }

            list.Add(new CombatTalentCount { TalentId = entry.TalentId, Count = entry.Count });
        }

        return list.ToArray();
    }
}
