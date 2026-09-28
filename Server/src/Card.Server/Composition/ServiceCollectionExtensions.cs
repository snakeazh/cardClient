using CardShare.Domain;
using CardShare.Domain.Config;
using CardShare.Domain.Players;
using CardShare.Domain.Pve;
using CardShare.Domain.Services;
using CardShare.Server.Pvp;

namespace CardShare.Server.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCardApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PlayerSession>();
        services.AddScoped<PlayerMetaService>();
        services.AddScoped<PveRunService>();
        // 手工工厂存在的唯一理由是传 GuestAuth:Enabled 布尔；Domain 不依赖 IOptions 包，为一个布尔引入不值。
        services.AddScoped(sp => new AuthService(
            sp.GetServices<ICodeSessionClient>(),
            sp.GetRequiredService<IAuthBindingRepository>(),
            sp.GetRequiredService<IPlayerRepository>(),
            sp.GetRequiredService<ITokenService>(),
            sp.GetRequiredService<IGameConfig>(),
            sp.GetRequiredService<IClock>(),
            configuration.GetValue("GuestAuth:Enabled", false),
            sp.GetRequiredService<PveRunService>()));
        services.AddSingleton<PvpConnectionHub>();
        services.AddSingleton<PvpMatchHost>();
        services.AddSingleton<PvpRewardService>();
        services.AddSingleton<PvpMessageRouter>();
        services.AddSingleton<PvpCommandDispatcher>();
        services.AddHostedService<PvpTimeoutSweeper>();
        services.AddHostedService<PvpBusSubscriber>();
        return services;
    }
}
