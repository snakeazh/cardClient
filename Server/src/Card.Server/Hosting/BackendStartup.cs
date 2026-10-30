using System.Diagnostics;
using System.Text.RegularExpressions;
using CardShare.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CardShare.Server.Hosting;

public sealed class BackendStatus
{
    public string Persistence { get; init; } = "Postgres";

    public bool? Postgres { get; init; }

    public bool? Redis { get; init; }

    public bool Ok => (Postgres ?? true) && (Redis ?? true);
}

public static class BackendHealth
{
    public static async Task<BackendStatus> ProbeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<CardDbContext>();
        var postgres = await db.Database.CanConnectAsync(cancellationToken);

        var redis = false;
        var mux = sp.GetService<IConnectionMultiplexer>();
        if (mux is { IsConnected: true })
        {
            try
            {
                await mux.GetDatabase().PingAsync();
                redis = true;
            }
            catch
            {
                redis = false;
            }
        }

        return new BackendStatus
        {
            Persistence = "Postgres",
            Postgres = postgres,
            Redis = redis
        };
    }
}

public static class BackendStartup
{
    public static async Task EnsureAsync(WebApplication app)
    {
        var config = app.Configuration;
        var log = app.Logger;
        var postgresCs = config.GetConnectionString("Postgres");
        var redisCs = config.GetConnectionString("Redis");
        var guest = config.GetValue("GuestAuth:Enabled", false);
        var fillBots = config.GetValue("Pvp:FillWithBots", false);
        log.LogInformation(
            "Server starting. postgres={Postgres} redis={Redis} guest={Guest} fillBots={FillBots}",
            Redact(postgresCs),
            Redact(redisCs),
            guest,
            fillBots);

        await EnsurePostgresAsync(app, postgresCs);
        await EnsureRedisAsync(app, redisCs);
    }

    private static async Task EnsurePostgresAsync(WebApplication app, string? connectionString)
    {
        var log = app.Logger;
        var target = Redact(connectionString);
        log.LogInformation("Connecting Postgres {Target} ...", target);
        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CardDbContext>();
            var ok = await db.Database.CanConnectAsync();
            sw.Stop();
            if (!ok)
            {
                log.LogError(
                    "Postgres connect failed after {ElapsedMs}ms. target={Target}",
                    sw.ElapsedMilliseconds,
                    target);
                throw new InvalidOperationException(
                    "Postgres is configured but not reachable. Check ConnectionStrings:Postgres and that PostgreSQL is running.");
            }

            log.LogInformation("Postgres connected in {ElapsedMs}ms. target={Target}", sw.ElapsedMilliseconds, target);
            await db.Database.EnsureCreatedAsync();
            // SaveVersion 已废弃（实体不再映射）：老库的 players 列保留，补默认值兼容不带该列的 INSERT。
            // 新库 EnsureCreated 建的 players 没有该列，存在时才补默认值。
            await db.Database.ExecuteSqlRawAsync(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name = 'players' AND column_name = 'SaveVersion') THEN
                        ALTER TABLE players ALTER COLUMN "SaveVersion" SET DEFAULT 1;
                    END IF;
                END $$;
                """);
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "ScoreTotal" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "CurrentLevelId" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "HighestClearedLevelId" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "ScoredLevelId" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "BoughtRelicSlots" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS auth_tokens;""");
            log.LogInformation("Postgres ready, schema ensured.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Postgres connect error after {ElapsedMs}ms. target={Target}", sw.ElapsedMilliseconds, target);
            throw new InvalidOperationException(
                "Postgres is configured but not reachable. Check ConnectionStrings:Postgres and that PostgreSQL is running.",
                ex);
        }
    }

    private static async Task EnsureRedisAsync(WebApplication app, string? connectionString)
    {
        var log = app.Logger;
        var target = Redact(connectionString);
        log.LogInformation("Connecting Redis {Target} ...", target);
        var sw = Stopwatch.StartNew();
        try
        {
            var mux = app.Services.GetRequiredService<IConnectionMultiplexer>();
            var ping = await mux.GetDatabase().PingAsync();
            sw.Stop();
            if (!mux.IsConnected)
            {
                log.LogError(
                    "Redis connect failed after {ElapsedMs}ms. target={Target}",
                    sw.ElapsedMilliseconds,
                    target);
                throw new InvalidOperationException(
                    "Redis is configured but not reachable. Check ConnectionStrings:Redis and that Redis is running.");
            }

            log.LogInformation(
                "Redis connected in {ElapsedMs}ms ping={PingMs}ms. endpoints={Endpoints}",
                sw.ElapsedMilliseconds,
                (long)ping.TotalMilliseconds,
                string.Join(",", mux.GetEndPoints().Select(e => e.ToString())));
            log.LogInformation("Redis ready, tokens/locks/pvp queue persist there.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Redis connect error after {ElapsedMs}ms. target={Target}", sw.ElapsedMilliseconds, target);
            throw new InvalidOperationException(
                "Redis is configured but not reachable. Check ConnectionStrings:Redis and that Redis is running.",
                ex);
        }
    }

    private static string Redact(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "(empty)";
        }

        return Regex.Replace(
            connectionString,
            "(Password|Pwd)=([^;]*)",
            "$1=***",
            RegexOptions.IgnoreCase);
    }
}
