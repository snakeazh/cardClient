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
        IAdConversionReporter? conversions = null,
        IAdWatchStats? adStats = null)
    {
        config ??= TestConfig.Create();
        clock ??= new TestClock();
        runs ??= new MemoryPveRunRepository();
        avatars ??= new FakeAvatarStore();
        conversions ??= new FakeAdConversionReporter();
        adStats ??= new FakeAdWatchStats();
        var session = new PlayerSession(players, config, clock, locks ?? new MemoryPlayerLock());
        return (new PveRunService(session, runs, config, clock), new PlayerMetaService(session, config, avatars, conversions, adStats));
    }

    private sealed class FakeAvatarStore : IAvatarStore
    {
        public Task<string> PutAsync(Guid userId, byte[] data, string extension, CancellationToken cancellationToken)
            => Task.FromResult($"https://fake.cos.test/avatars/{userId:N}/fake.{extension}");
    }

    internal sealed class FakeAdConversionReporter : IAdConversionReporter
    {
        public List<string> Scenes { get; } = new List<string>();

        public void ReportAdWatched(AdAttributionState attribution, string scene)
        {
            Scenes.Add(scene);
        }
    }

    internal sealed class FakeAdWatchStats : IAdWatchStats
    {
        public List<string> Scenes { get; } = new List<string>();

        public void RecordWatch(string scene)
        {
            Scenes.Add(scene);
        }
    }
}
