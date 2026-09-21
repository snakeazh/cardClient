using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Config;
using CardShare.Domain.Pvp;
using CardShare.Infrastructure.Auth;
using CardShare.Infrastructure.Config;
using CardShare.Infrastructure.Memory;
using CardShare.Infrastructure.Postgres;
using CardShare.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CardShare.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCardInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GuestAuthOptions>(configuration.GetSection("GuestAuth"));
        services.Configure<WeChatAuthOptions>(configuration.GetSection("WeChat"));
        services.Configure<DouyinAuthOptions>(configuration.GetSection("Douyin"));

        var configPath = configuration["Game:ConfigPath"] ?? string.Empty;
        var timeZone = configuration["Game:TimeZone"] ?? "Asia/Shanghai";
        var gameConfig = JsonGameConfig.Load(configPath, timeZone);
        services.AddSingleton<IGameTables>(gameConfig.Tables);
        services.AddSingleton<IGameConfig>(gameConfig);
        services.AddSingleton<IGameConfigLoader>(_ => new FileGameConfigLoader(configPath));
        services.AddSingleton<IClock, SystemClock>();

        var provider = configuration["Persistence:Provider"] ?? "Memory";
        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var cs = configuration.GetConnectionString("Postgres")
                     ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required when Persistence:Provider=Postgres.");
            services.AddDbContext<CardDbContext>(o => o.UseNpgsql(cs));
            services.AddScoped<IPlayerRepository, PostgresPlayerRepository>();
            services.AddScoped<IAuthBindingRepository, PostgresAuthBindingRepository>();
            services.AddScoped<IPveRunRepository, PostgresPveRunRepository>();
        }
        else
        {
            services.AddSingleton<MemoryPlayerRepository>();
            services.AddSingleton<MemoryAuthBindingRepository>();
            services.AddSingleton<MemoryPveRunRepository>();
            services.AddSingleton<IPlayerRepository>(sp => sp.GetRequiredService<MemoryPlayerRepository>());
            services.AddSingleton<IAuthBindingRepository>(sp => sp.GetRequiredService<MemoryAuthBindingRepository>());
            services.AddSingleton<IPveRunRepository>(sp => sp.GetRequiredService<MemoryPveRunRepository>());
        }

        var accessMinutes = configuration.GetValue("Auth:AccessTokenMinutes", 120);
        var accessTtl = TimeSpan.FromMinutes(accessMinutes);

        var redisCs = configuration.GetConnectionString("Redis");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? configuration["ASPNETCORE_ENVIRONMENT"]
                          ?? string.Empty;
        var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
        if ((isDevelopment || string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(redisCs))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Redis is required in Development and when Persistence:Provider=Postgres. Access tokens live in Redis, not Postgres.");
        }

        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("CardShare.Redis");
                var options = ConfigurationOptions.Parse(redisCs);
                options.AbortOnConnectFail = true;
                options.ConnectTimeout = 5000;
                options.ConnectRetry = 2;
                log.LogInformation("Redis multiplexer connecting {Target} ...", redisCs);
                try
                {
                    var mux = ConnectionMultiplexer.Connect(options);
                    log.LogInformation(
                        "Redis multiplexer connected. endpoints={Endpoints} isConnected={IsConnected}",
                        string.Join(",", mux.GetEndPoints().Select(e => e.ToString())),
                        mux.IsConnected);
                    return mux;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Redis multiplexer connect failed. target={Target}", redisCs);
                    throw;
                }
            });
            services.AddSingleton<IPlayerLock, RedisPlayerLock>();
            services.AddSingleton<IPvpBus, RedisPvpBus>();
            services.AddSingleton<RedisPvpMatchmaker>();
            AddMatchmaker<RedisPvpMatchmaker>(services, configuration.GetValue("Pvp:FillWithBots", false));
            services.AddSingleton<ITokenService>(sp => new RedisTokenService(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                accessTtl));
        }
        else
        {
            services.AddSingleton<IPlayerLock, MemoryPlayerLock>();
            services.AddSingleton<IPvpBus, NullPvpBus>();
            services.AddSingleton<InMemoryPvpMatchmaker>();
            AddMatchmaker<InMemoryPvpMatchmaker>(services, configuration.GetValue("Pvp:FillWithBots", false));
            services.AddSingleton<ITokenService>(sp => new MemoryTokenService(
                sp.GetRequiredService<IClock>(),
                accessTtl));
        }

        services.AddHttpClient<WeChatCodeSessionClient>();
        services.AddHttpClient<DouyinCodeSessionClient>();
        services.AddSingleton<ICodeSessionClient, GuestCodeSessionClient>();
        services.AddTransient<ICodeSessionClient>(sp => sp.GetRequiredService<WeChatCodeSessionClient>());
        services.AddTransient<ICodeSessionClient>(sp => sp.GetRequiredService<DouyinCodeSessionClient>());
        return services;
    }

    private static void AddMatchmaker<TInner>(IServiceCollection services, bool fillWithBots)
        where TInner : class, IPvpMatchmaker
    {
        if (fillWithBots)
        {
            services.AddSingleton<PvpBotFillMatchmaker>(sp => new PvpBotFillMatchmaker(
                sp.GetRequiredService<TInner>(),
                sp.GetRequiredService<IGameTables>()));
            services.AddSingleton<IPvpMatchmaker>(sp => sp.GetRequiredService<PvpBotFillMatchmaker>());
            return;
        }

        services.AddSingleton<IPvpMatchmaker>(sp => sp.GetRequiredService<TInner>());
    }
}
