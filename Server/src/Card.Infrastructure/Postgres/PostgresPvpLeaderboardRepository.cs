using CardShare.Contracts;
using CardShare.Domain.Pvp;
using Microsoft.EntityFrameworkCore;

namespace CardShare.Infrastructure.Postgres;

public sealed class PostgresPvpLeaderboardRepository : IPvpLeaderboardRepository
{
    private const int BoardSize = 100;
    private const string ScoreSql = """(("ProfileJson" ->> 'PvpScore')::integer)""";

    public const string EnsureIndexSql = $"""
        CREATE INDEX IF NOT EXISTS "IX_players_pvp_score_user" ON players ({ScoreSql} DESC, "UserId")
        WHERE {ScoreSql} > 0;
        """;

    private readonly CardDbContext _db;

    public PostgresPvpLeaderboardRepository(CardDbContext db) => _db = db;

    public async Task<PvpLeaderboardResponse?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        // 单条语句保证前 100 名和本人名次来自同一个快照。旧档缺积分字段时按 0 分展示。
        var rows = await _db.Database.SqlQueryRaw<LeaderboardRow>($$"""
            WITH top_players AS (
                SELECT "UserId", "ProfileJson", {{ScoreSql}} AS score
                FROM players
                WHERE {{ScoreSql}} > 0
                ORDER BY {{ScoreSql}} DESC, "UserId"
                LIMIT {{BoardSize}}
            ), selected_players AS (
                SELECT "ProfileJson", score,
                       row_number() OVER (ORDER BY score DESC, "UserId") AS place, false AS is_self
                FROM top_players
                UNION ALL
                SELECT me."ProfileJson", GREATEST(me.score, 0),
                       CASE WHEN me.score > 0 THEN 1 + (
                           SELECT count(*) FROM players
                           WHERE {{ScoreSql}} > 0 AND {{ScoreSql}} > me.score
                       ) + (
                           SELECT count(*) FROM players
                           WHERE {{ScoreSql}} > 0 AND {{ScoreSql}} = me.score AND "UserId" < me."UserId"
                       ) END, true
                FROM (
                    SELECT "UserId", "ProfileJson", {{ScoreSql}} AS score
                    FROM players WHERE "UserId" = {0}
                ) AS me
            )
            SELECT place AS "Place", score AS "Score", is_self AS "IsSelf",
                   COALESCE("ProfileJson" ->> 'NickName', '') AS "NickName",
                   COALESCE("ProfileJson" ->> 'AvatarUrl', '') AS "AvatarUrl"
            FROM selected_players
            ORDER BY is_self, place
            """, userId).ToListAsync(cancellationToken);

        var self = rows.LastOrDefault();
        if (self == null || !self.IsSelf)
        {
            return null;
        }

        return new PvpLeaderboardResponse
        {
            Entries = rows.Take(rows.Count - 1).Select(ToEntry).ToArray(),
            Self = ToEntry(self)
        };
    }

    private static PvpLeaderboardEntry ToEntry(LeaderboardRow row) => new()
    {
        Place = row.Place,
        Score = row.Score,
        NickName = row.NickName,
        AvatarUrl = row.AvatarUrl
    };

    private sealed class LeaderboardRow
    {
        public long? Place { get; set; }
        public int Score { get; set; }
        public bool IsSelf { get; set; }
        public string NickName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
    }
}
