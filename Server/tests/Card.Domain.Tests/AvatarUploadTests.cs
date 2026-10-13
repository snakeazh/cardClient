using CardShare.Domain;
using CardShare.Domain.Players;
using CardShare.Infrastructure.Memory;
using Xunit;

namespace CardShare.Domain.Tests;

public class AvatarUploadTests
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] JpgBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    [Fact]
    public async Task UploadPngStoresUrlOnProfile()
    {
        var players = new MemoryPlayerRepository();
        var userId = Guid.NewGuid();
        var config = TestConfig.Create();
        await players.SaveAsync(PlayerProfile.CreateNew(userId, config, new TestClock().UtcNow), CancellationToken.None);
        var (_, meta) = TestApp.Create(players, config: config);

        var dto = await meta.UploadAvatarAsync(userId, PngBytes, CancellationToken.None);

        Assert.EndsWith(".png", dto.AvatarUrl);
        var saved = await players.GetAsync(userId, CancellationToken.None);
        Assert.Equal(dto.AvatarUrl, saved!.AvatarUrl);
    }

    [Fact]
    public async Task UploadRejectsNonImage()
    {
        var players = new MemoryPlayerRepository();
        var userId = Guid.NewGuid();
        var config = TestConfig.Create();
        await players.SaveAsync(PlayerProfile.CreateNew(userId, config, new TestClock().UtcNow), CancellationToken.None);
        var (_, meta) = TestApp.Create(players, config: config);

        await Assert.ThrowsAsync<DomainException>(
            () => meta.UploadAvatarAsync(userId, new byte[] { 1, 2, 3, 4 }, CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(
            () => meta.UploadAvatarAsync(userId, Array.Empty<byte>(), CancellationToken.None));
    }

    [Fact]
    public async Task UploadJpgUsesJpgExtension()
    {
        var players = new MemoryPlayerRepository();
        var userId = Guid.NewGuid();
        var config = TestConfig.Create();
        await players.SaveAsync(PlayerProfile.CreateNew(userId, config, new TestClock().UtcNow), CancellationToken.None);
        var (_, meta) = TestApp.Create(players, config: config);

        var dto = await meta.UploadAvatarAsync(userId, JpgBytes, CancellationToken.None);

        Assert.EndsWith(".jpg", dto.AvatarUrl);
    }
}
