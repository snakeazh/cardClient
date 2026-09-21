using CardShare.Domain;
using CardShare.Domain.Config;
using CardShare.Domain.Players;
using CardShare.Domain.Pve;
using CardShare.Domain.Services;
using CardShare.Infrastructure.Memory;

namespace CardShare.Domain.Tests;

internal static class TestApp
{
    public static (PveRunService Pve, PlayerMetaService Meta) Create(
        IPlayerRepository players,
        IPveRunRepository? runs = null,
        IGameConfig? config = null,
        IClock? clock = null,
        IPlayerLock? locks = null)
    {
        config ??= TestConfig.Create();
        clock ??= new TestClock();
        runs ??= new MemoryPveRunRepository();
        var session = new PlayerSession(players, config, clock, locks ?? new MemoryPlayerLock());
        return (new PveRunService(session, runs, config, clock), new PlayerMetaService(session, config));
    }
}
