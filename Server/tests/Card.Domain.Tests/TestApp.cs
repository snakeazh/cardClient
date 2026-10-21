using CardShare.Domain;
using CardShare.Domain.Ads;
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
        IPlayerLock? locks = null,
        IAvatarStore? avatars = null,
        IAdConversionReporter? conversions = null)
    {
        config ??= TestConfig.Create();
        clock ??= new TestClock();
        runs ??= new MemoryPveRunRepository();
        avatars ??= new FakeAvatarStore();
        conversions ??= new FakeAdConversionReporter();
        var session = new PlayerSession(players, config, clock, locks ?? new MemoryPlayerLock());
        return (new PveRunService(session, runs, config, clock), new PlayerMetaService(session, config, avatars, conversions));
    }

    private sealed class FakeAvatarStore : IAvatarStore
    {
        public Task<string> PutAsync(Guid userId, byte[] data, string extension, CancellationToken cancellationToken)
            => Task.FromResult($"https://fake.cos.test/avatars/{userId:N}/fake.{extension}");
    }

    private sealed class FakeAdConversionReporter : IAdConversionReporter
    {
        public void ReportAdWatched(AdAttributionState attribution, string scene)
        {
        }
    }
}
