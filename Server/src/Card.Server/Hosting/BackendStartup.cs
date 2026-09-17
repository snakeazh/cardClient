using System.Diagnostics;
using System.Text.RegularExpressions;
using CardShare.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CardShare.Server.Hosting;

public sealed class BackendStatus
{
    public string Persistence { get; init; } = "Memory";

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
        var persistence = configuration["Persistence:Provider"] ?? "Memory";
        bool? postgres = null;
        bool? redis = null;

        if (string.Equals(persistence, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var db = sp.GetRequiredService<CardDbContext>();
            postgres = await db.Database.CanConnectAsync(cancellationToken);
        }

        var redisCs = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            var mux = sp.GetService<IConnectionMultiplexer>();
            redis = mux is { IsConnected: true };
            if (redis == true)
            {
                try
                {
                    await mux!.GetDatabase().PingAsync();
                }
                catch
                {
                    redis = false;
                }
            }
        }

        return new BackendStatus
        {
            Persistence = persistence,
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
        var persistence = config["Persistence:Provider"] ?? "Memory";
        var postgresCs = config.GetConnectionString("Postgres");
        var redisCs = config.GetConnectionString("Redis");
        var guest = config.GetValue("GuestAuth:Enabled", false);
        var usePostgres = string.Equals(persistence, "Postgres", StringComparison.OrdinalIgnoreCase);
        log.LogInformation(
            "Server starting. persistence={Persistence} postgres={Postgres} redis={Redis} guest={Guest}",
            persistence,
            usePostgres ? Redact(postgresCs) : "off",
            string.IsNullOrWhiteSpace(redisCs) ? "off" : Redact(redisCs),
            guest);

        if (usePostgres)
        {
            await EnsurePostgresAsync(app, postgresCs);
        }
        else
        {
            log.LogInformation("Postgres off. Player/auth/pve data stays in process memory.");
        }

        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            await EnsureRedisAsync(app, redisCs);
        }
        else
        {
            log.LogInformation("Redis off. Tokens/locks/pvp queue stay in process memory.");
        }
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
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "ScoreTotal" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "CurrentLevelId" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "HighestClearedLevelId" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "ScoredLevelId" integer NOT NULL DEFAULT 0;""");
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

    private static async Task EnsureRedisAsync(WebApplication app, string connectionString)
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
