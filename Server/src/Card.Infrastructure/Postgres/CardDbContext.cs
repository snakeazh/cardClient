using System.Text.Json;
using CardShare.Domain;
using CardShare.Domain.Admin;
using CardShare.Domain.Players;
using CardShare.Domain.Pve;
using Microsoft.EntityFrameworkCore;

namespace CardShare.Infrastructure.Postgres;

public sealed class AdminLogRow
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string Command { get; set; } = string.Empty;

    public Guid TargetUserId { get; set; }

    public string Detail { get; set; } = string.Empty;

    public string RemoteIp { get; set; } = string.Empty;

    public bool Succeeded { get; set; }
}

public sealed class PlayerRow
{
    public Guid UserId { get; set; }

    // players 表的历史列 SaveVersion 已废弃保留（并发由 IPlayerLock 保证，见 PlayerProfile 注释）；
    // 老库列仍在，由 BackendStartup 的 ALTER ... SET DEFAULT 兼容不再带该列的 INSERT。

    public string ProfileJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuthBindingRow
{
    public string Provider { get; set; } = string.Empty;

    public string OpenId { get; set; } = string.Empty;

    public Guid UserId { get; set; }
}

public sealed class PveRunRow
{
    public Guid RunId { get; set; }

    public Guid UserId { get; set; }

    public int LevelId { get; set; }

    public int HeroId { get; set; }

    public string ShopRelicIdsJson { get; set; } = "[]";

    public int Gold { get; set; }

    public string RelicIdsJson { get; set; } = "[]";

    public string ShopOfferIdsJson { get; set; } = "[]";

    public int ShopRefreshCount { get; set; }

    public int FreeShopRefreshLeft { get; set; }

    public int BoughtRelicSlots { get; set; }

    public int Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    public string? SettleFingerprint { get; set; }

    public int GoldGranted { get; set; }

    public int ScoreTotal { get; set; }

    public int CurrentLevelId { get; set; }

    public int HighestClearedLevelId { get; set; }

    public int ScoredLevelId { get; set; }
}

public sealed class CardDbContext : DbContext
{
    public CardDbContext(DbContextOptions<CardDbContext> options)
        : base(options)
    {
    }

    public DbSet<PlayerRow> Players => Set<PlayerRow>();

    public DbSet<AuthBindingRow> AuthBindings => Set<AuthBindingRow>();

    public DbSet<PveRunRow> PveRuns => Set<PveRunRow>();

    public DbSet<AdminLogRow> AdminLogs => Set<AdminLogRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerRow>(e =>
        {
            e.ToTable("players");
            e.HasKey(x => x.UserId);
            e.Property(x => x.ProfileJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AuthBindingRow>(e =>
        {
            e.ToTable("auth_bindings");
            e.HasKey(x => new { x.Provider, x.OpenId });
        });

        modelBuilder.Entity<PveRunRow>(e =>
        {
            e.ToTable("pve_runs");
            e.HasKey(x => x.RunId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.Status });
        });

        modelBuilder.Entity<AdminLogRow>(e =>
        {
            e.ToTable("admin_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.HasIndex(x => x.CreatedAtUtc);
        });
    }
}

public sealed class PostgresPlayerRepository : IPlayerRepository
{
    private readonly CardDbContext _db;

    public PostgresPlayerRepository(CardDbContext db) => _db = db;

    public async Task<PlayerProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await _db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return row == null ? null : JsonSerializer.Deserialize<PlayerProfile>(row.ProfileJson);
    }

    public async Task SaveAsync(PlayerProfile profile, CancellationToken cancellationToken)
    {
        var row = await _db.Players.FirstOrDefaultAsync(p => p.UserId == profile.UserId, cancellationToken);
        var json = JsonSerializer.Serialize(profile);
        if (row == null)
        {
            _db.Players.Add(new PlayerRow
            {
                UserId = profile.UserId,
                ProfileJson = json,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            row.ProfileJson = json;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerSummary>> SearchAsync(string keyword, int take, CancellationToken cancellationToken)
    {
        take = take <= 0 ? 20 : Math.Min(take, 100);
        List<PlayerRow> rows;
        var kw = (keyword ?? string.Empty).Trim();
        if (kw.Length == 0)
        {
            rows = await _db.Players.AsNoTracking()
                .OrderByDescending(p => p.UpdatedAt)
                .Take(take)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var like = "%" + kw + "%";
            rows = await _db.Players.FromSqlInterpolated($"""
                SELECT "UserId", "ProfileJson", "UpdatedAt" FROM players
                WHERE ("ProfileJson" ->> 'NickName') ILIKE {like}
                   OR "UserId"::text ILIKE {like}
                ORDER BY "UpdatedAt" DESC
                LIMIT {take}
                """).AsNoTracking().ToListAsync(cancellationToken);
        }

        var result = new List<PlayerSummary>(rows.Count);
        foreach (var row in rows)
        {
            var profile = JsonSerializer.Deserialize<PlayerProfile>(row.ProfileJson);
            result.Add(new PlayerSummary
            {
                UserId = row.UserId,
                NickName = profile?.NickName ?? string.Empty,
                Gold = profile?.Gold ?? 0,
                Energy = profile?.Energy.Current ?? 0,
                PvpScore = profile?.PvpScore ?? 0,
                UpdatedAt = row.UpdatedAt
            });
        }

        return result;
    }
}

public sealed class PostgresAdminLogRepository : IAdminLogRepository
{
    private readonly CardDbContext _db;

    public PostgresAdminLogRepository(CardDbContext db) => _db = db;

    public async Task AddAsync(AdminLogEntry entry, CancellationToken cancellationToken)
    {
        var row = new AdminLogRow
        {
            CreatedAtUtc = entry.CreatedAtUtc,
            Command = entry.Command,
            TargetUserId = entry.TargetUserId,
            Detail = entry.Detail,
            RemoteIp = entry.RemoteIp,
            Succeeded = entry.Succeeded
        };
        _db.AdminLogs.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        entry.Id = row.Id;
    }

    public async Task<IReadOnlyList<AdminLogEntry>> ListAsync(int take, CancellationToken cancellationToken)
    {
        take = take <= 0 ? 50 : Math.Min(take, 200);
        var rows = await _db.AdminLogs.AsNoTracking()
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new AdminLogEntry
        {
            Id = row.Id,
            CreatedAtUtc = row.CreatedAtUtc,
            Command = row.Command,
            TargetUserId = row.TargetUserId,
            Detail = row.Detail,
            RemoteIp = row.RemoteIp,
            Succeeded = row.Succeeded
        }).ToList();
    }
}

public sealed class PostgresAuthBindingRepository : IAuthBindingRepository
{
    private readonly CardDbContext _db;

    public PostgresAuthBindingRepository(CardDbContext db) => _db = db;

    public async Task<Guid?> FindUserIdAsync(string provider, string openId, CancellationToken cancellationToken)
    {
        var row = await _db.AuthBindings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Provider == provider && b.OpenId == openId, cancellationToken);
        if (row == null)
        {
            return null;
        }

        return row.UserId;
    }

    public async Task BindAsync(string provider, string openId, Guid userId, CancellationToken cancellationToken)
    {
        var exists = await _db.AuthBindings.AnyAsync(b => b.Provider == provider && b.OpenId == openId, cancellationToken);
        if (!exists)
        {
            _db.AuthBindings.Add(new AuthBindingRow { Provider = provider, OpenId = openId, UserId = userId });
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class PostgresPveRunRepository : IPveRunRepository
{
    private readonly CardDbContext _db;

    public PostgresPveRunRepository(CardDbContext db) => _db = db;

    public async Task AddAsync(PveRun run, CancellationToken cancellationToken)
    {
        _db.PveRuns.Add(ToRow(run));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PveRun?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        var row = await _db.PveRuns.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken);
        return row == null ? null : FromRow(row);
    }

    public async Task<PveRun?> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await _db.PveRuns.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == (int)PveRunStatus.Active)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row == null ? null : FromRow(row);
    }

    public async Task SaveAsync(PveRun run, CancellationToken cancellationToken)
    {
        var row = await _db.PveRuns.FirstOrDefaultAsync(r => r.RunId == run.RunId, cancellationToken);
        if (row == null)
        {
            _db.PveRuns.Add(ToRow(run));
        }
        else
        {
            row.Status = (int)run.Status;
            row.SettledAt = run.SettledAt;
            row.SettleFingerprint = run.SettleFingerprint;
            row.GoldGranted = run.GoldGranted;
            row.Gold = run.Gold;
            row.RelicIdsJson = JsonSerializer.Serialize(run.RelicIds);
            row.ShopOfferIdsJson = JsonSerializer.Serialize(run.ShopOfferIds);
            row.ShopRefreshCount = run.ShopRefreshCount;
            row.FreeShopRefreshLeft = run.FreeShopRefreshLeft;
            row.BoughtRelicSlots = run.BoughtRelicSlots;
            row.ScoreTotal = run.ScoreTotal;
            row.CurrentLevelId = run.CurrentLevelId;
            row.HighestClearedLevelId = run.HighestClearedLevelId;
            row.ScoredLevelId = run.ScoredLevelId;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static PveRunRow ToRow(PveRun run)
    {
        return new PveRunRow
        {
            RunId = run.RunId,
            UserId = run.UserId,
            LevelId = run.LevelId,
            HeroId = run.HeroId,
            ShopRelicIdsJson = JsonSerializer.Serialize(run.ShopRelicIds),
            Gold = run.Gold,
            RelicIdsJson = JsonSerializer.Serialize(run.RelicIds),
            ShopOfferIdsJson = JsonSerializer.Serialize(run.ShopOfferIds),
            ShopRefreshCount = run.ShopRefreshCount,
            FreeShopRefreshLeft = run.FreeShopRefreshLeft,
            BoughtRelicSlots = run.BoughtRelicSlots,
            Status = (int)run.Status,
            StartedAt = run.StartedAt,
            SettledAt = run.SettledAt,
            SettleFingerprint = run.SettleFingerprint,
            GoldGranted = run.GoldGranted,
            ScoreTotal = run.ScoreTotal,
            CurrentLevelId = run.CurrentLevelId,
            HighestClearedLevelId = run.HighestClearedLevelId,
            ScoredLevelId = run.ScoredLevelId
        };
    }

    private static PveRun FromRow(PveRunRow row)
    {
        return new PveRun
        {
            RunId = row.RunId,
            UserId = row.UserId,
            LevelId = row.LevelId,
            HeroId = row.HeroId,
            ShopRelicIds = JsonSerializer.Deserialize<int[]>(row.ShopRelicIdsJson) ?? Array.Empty<int>(),
            Gold = row.Gold,
            RelicIds = (JsonSerializer.Deserialize<List<int>>(row.RelicIdsJson) ?? new List<int>()),
            ShopOfferIds = JsonSerializer.Deserialize<List<int>>(row.ShopOfferIdsJson) ?? new List<int>(),
            ShopRefreshCount = row.ShopRefreshCount,
            FreeShopRefreshLeft = row.FreeShopRefreshLeft,
            BoughtRelicSlots = row.BoughtRelicSlots,
            Status = (PveRunStatus)row.Status,
            StartedAt = row.StartedAt,
            SettledAt = row.SettledAt,
            SettleFingerprint = row.SettleFingerprint,
            GoldGranted = row.GoldGranted,
            ScoreTotal = row.ScoreTotal,
            CurrentLevelId = row.CurrentLevelId > 0 ? row.CurrentLevelId : row.LevelId,
            HighestClearedLevelId = row.HighestClearedLevelId,
            ScoredLevelId = row.ScoredLevelId
        };
    }
}
