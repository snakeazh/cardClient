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
        services.AddScoped<IPveRunService>(sp => sp.GetRequiredService<PveRunService>());
        services.AddScoped(sp => new AuthService(
            sp.GetServices<ICodeSessionClient>(),
            sp.GetRequiredService<IAuthBindingRepository>(),
            sp.GetRequiredService<IPlayerRepository>(),
            sp.GetRequiredService<ITokenService>(),
            sp.GetRequiredService<IGameConfig>(),
            sp.GetRequiredService<IClock>(),
            configuration.GetValue("GuestAuth:Enabled", false),
            sp.GetRequiredService<IPveRunService>()));
        services.AddSingleton<PvpConnectionHub>();
        services.AddSingleton<PvpBattleHost>();
        return services;
    }
}
