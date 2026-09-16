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
        var persistence = config["Persistence:Provider"] ?? "Memory";
        var redisCs = config.GetConnectionString("Redis");
        var guest = config.GetValue("GuestAuth:Enabled", false);
        app.Logger.LogInformation(
            "backends persistence={Persistence} redis={Redis} guest={Guest}",
            persistence,
            string.IsNullOrWhiteSpace(redisCs) ? "off" : redisCs,
            guest);

        var status = await BackendHealth.ProbeAsync(app.Services, config, CancellationToken.None);
        if (string.Equals(persistence, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (status.Postgres != true)
            {
                throw new InvalidOperationException(
                    "Postgres is configured but not reachable. Check ConnectionStrings:Postgres and that PostgreSQL is running.");
            }

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CardDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "ScoreTotal" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "CurrentLevelId" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "HighestClearedLevelId" integer NOT NULL DEFAULT 0;""");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE pve_runs ADD COLUMN IF NOT EXISTS "ScoredLevelId" integer NOT NULL DEFAULT 0;""");
            app.Logger.LogInformation("Postgres ready, schema ensured.");
        }

        if (!string.IsNullOrWhiteSpace(redisCs) && status.Redis != true)
        {
            throw new InvalidOperationException(
                "Redis is configured but not reachable. Check ConnectionStrings:Redis and that Redis is running.");
        }

        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            app.Logger.LogInformation("Redis ready, tokens/locks/pvp queue persist there.");
        }
    }
}
