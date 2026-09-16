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
        var refreshDays = configuration.GetValue("Auth:RefreshTokenDays", 14);
        var accessTtl = TimeSpan.FromMinutes(accessMinutes);
        var refreshTtl = TimeSpan.FromDays(refreshDays);

        var redisCs = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redisCs);
                options.AbortOnConnectFail = true;
                options.ConnectTimeout = 5000;
                options.ConnectRetry = 2;
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<IPlayerLock, RedisPlayerLock>();
            services.AddSingleton<IPvpMatchmaker, RedisPvpMatchmaker>();
            services.AddSingleton<ITokenService>(sp => new RedisTokenService(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                accessTtl,
                refreshTtl));
        }
        else
        {
            services.AddSingleton<IPlayerLock, MemoryPlayerLock>();
            services.AddSingleton<IPvpMatchmaker, InMemoryPvpMatchmaker>();
            services.AddSingleton<ITokenService>(sp => new MemoryTokenService(
                sp.GetRequiredService<IClock>(),
                accessTtl,
                refreshTtl));
        }

        services.AddHttpClient<WeChatCodeSessionClient>();
        services.AddHttpClient<DouyinCodeSessionClient>();
        services.AddSingleton<ICodeSessionClient, GuestCodeSessionClient>();
        services.AddTransient<ICodeSessionClient>(sp => sp.GetRequiredService<WeChatCodeSessionClient>());
        services.AddTransient<ICodeSessionClient>(sp => sp.GetRequiredService<DouyinCodeSessionClient>());
        return services;
    }
}
