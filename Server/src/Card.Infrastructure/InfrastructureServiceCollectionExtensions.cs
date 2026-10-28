using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Ads;
using CardShare.Domain.Config;
using CardShare.Domain.Pvp;
using CardShare.Infrastructure.Ads;
using CardShare.Infrastructure.Auth;
using CardShare.Infrastructure.Config;
using CardShare.Infrastructure.Postgres;
using CardShare.Infrastructure.Redis;
using CardShare.Infrastructure.Storage;
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
        services.Configure<WeChatAuthOptions>(configuration.GetSection("WeChat"));
        services.Configure<DouyinAuthOptions>(configuration.GetSection("Douyin"));
        services.Configure<CosAvatarOptions>(configuration.GetSection("Cos"));
        if (string.IsNullOrWhiteSpace(configuration["Cos:Bucket"]))
        {
            services.AddSingleton<IAvatarStore, DisabledAvatarStore>();
        }
        else
        {
            services.AddSingleton<IAvatarStore, CosAvatarStore>();
        }

        var configPath = configuration["Game:ConfigPath"] ?? string.Empty;
        var timeZone = configuration["Game:TimeZone"] ?? "Asia/Shanghai";
        var gameConfig = JsonGameConfig.Load(configPath, timeZone);
        services.AddSingleton<IGameTables>(gameConfig.Tables);
        services.AddSingleton<IGameConfig>(gameConfig);
        services.AddSingleton<IGameConfigLoader>(_ => new FileGameConfigLoader(configPath));
        services.AddSingleton<IClock, SystemClock>();

        var postgresCs = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(postgresCs))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is required. Player/auth/pve data lives in Postgres.");
        }

        services.AddDbContext<CardDbContext>(o => o.UseNpgsql(postgresCs));
        services.AddScoped<IPlayerRepository, PostgresPlayerRepository>();
        services.AddScoped<IAuthBindingRepository, PostgresAuthBindingRepository>();
        services.AddScoped<IPveRunRepository, PostgresPveRunRepository>();

        var accessMinutes = configuration.GetValue("Auth:AccessTokenMinutes", 120);
        var accessTtl = TimeSpan.FromMinutes(accessMinutes);

        var redisCs = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisCs))
        {
            throw new InvalidOperationException("ConnectionStrings:Redis is required. Access tokens/locks/pvp queue live in Redis.");
        }

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

        services.AddHttpClient<WeChatCodeSessionClient>();
        services.AddHttpClient<DouyinCodeSessionClient>();
        services.AddSingleton<ICodeSessionClient, GuestCodeSessionClient>();
        services.AddTransient<ICodeSessionClient>(sp => sp.GetRequiredService<WeChatCodeSessionClient>());
        services.AddTransient<ICodeSessionClient>(sp => sp.GetRequiredService<DouyinCodeSessionClient>());

        services.Configure<OceanEngineOptions>(configuration.GetSection("OceanEngine"));
        services.AddHttpClient(OceanEngineHttp.ClientName);
        services.AddSingleton<IAdConversionReporter, OceanEngineConversionReporter>();
        services.AddSingleton<IAdWatchStats, RedisAdWatchStats>();
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
