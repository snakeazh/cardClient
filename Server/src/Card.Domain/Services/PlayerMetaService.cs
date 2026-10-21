using CardShare.Contracts;
using CardShare.Domain.Ads;
using CardShare.Domain.Config;
using CardShare.Domain.Players;

namespace CardShare.Domain.Services;

public sealed class PlayerMetaService
{
    /// <summary>头像图片上限 2MB（Nginx client_max_body_size 同为 2m）。</summary>
    public const int AvatarMaxBytes = 2 * 1024 * 1024;

    private readonly PlayerSession _session;
    private readonly IGameConfig _config;
    private readonly IAvatarStore _avatars;
    private readonly IAdConversionReporter _conversions;
    private readonly Random _random = new Random();

    public PlayerMetaService(PlayerSession session, IGameConfig config, IAvatarStore avatars, IAdConversionReporter conversions)
    {
        _session = session;
        _config = config;
        _avatars = avatars;
        _conversions = conversions;
    }

    public Task<PlayerProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
        => _session.ReadAsync(userId, ProfileMapper.ToDto, cancellationToken);

    public Task<TalentDrawResponse> DrawTalentAsync(Guid userId, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            var talentId = PickTalent(profile);
            var result = profile.DrawTalent(talentId, _config);
            return new TalentDrawResponse
            {
                TalentId = result.TalentId,
                Count = result.Count,
                DrawCount = result.DrawCount,
                GoldSpent = result.GoldSpent,
                Profile = ProfileMapper.ToDto(profile)
            };
        }, cancellationToken);

    public async Task<EnergyRefillResponse> RefillEnergyByAdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var attribution = new AdAttributionState();
        var response = await _session.MutateAsync(userId, profile =>
        {
            profile.RefillEnergyByAd(_config);
            attribution = profile.AdAttribution.Snapshot();
            return new EnergyRefillResponse { Profile = ProfileMapper.ToDto(profile) };
        }, cancellationToken);
        _conversions.ReportAdWatched(attribution, "energy_refill_ad");
        return response;
    }

    public async Task<AdShopClaimResponse> ClaimAdShopAsync(Guid userId, string kind, CancellationToken cancellationToken)
    {
        var attribution = new AdAttributionState();
        var response = await _session.MutateAsync(userId, profile =>
        {
            profile.ClaimAdShop(kind, _config);
            attribution = profile.AdAttribution.Snapshot();
            return new AdShopClaimResponse { Profile = ProfileMapper.ToDto(profile) };
        }, cancellationToken);
        _conversions.ReportAdWatched(attribution, "adshop_" + kind);
        return response;
    }

    public Task<PlayerProfileDto> GrantBagAsync(Guid userId, int itemId, int amount, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.GrantBagItem(itemId, amount, _config);
            return ProfileMapper.ToDto(profile);
        }, cancellationToken);

    public Task<PlayerProfileDto> ConsumeBagAsync(Guid userId, int itemId, int amount, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.ConsumeBagItem(itemId, amount, _config);
            return ProfileMapper.ToDto(profile);
        }, cancellationToken);

    public Task<PlayerProfileDto> CompleteGuideAsync(Guid userId, int groupId, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.CompleteGuideGroup(groupId);
            return ProfileMapper.ToDto(profile);
        }, cancellationToken);

    public Task<PlayerProfileDto> UnlockHeroAsync(Guid userId, int heroId, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.UnlockHero(heroId, _config);
            return ProfileMapper.ToDto(profile);
        }, cancellationToken);

    public Task<PlayerProfileDto> DebugGrantGoldAsync(Guid userId, int amount, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.GrantWalletGold(amount);
            return ProfileMapper.ToDto(profile);
        }, cancellationToken);

    public async Task<PlayerProfileDto> UploadAvatarAsync(Guid userId, byte[] data, CancellationToken cancellationToken)
    {
        var extension = DetectImageExtension(data);
        await using var gate = await _session.AcquireAsync(userId, cancellationToken);
        var profile = await _session.LoadAsync(userId, cancellationToken);
        var url = await _avatars.PutAsync(userId, data, extension, cancellationToken);
        profile.AvatarUrl = url;
        await _session.SaveAsync(profile, cancellationToken);
        return ProfileMapper.ToDto(profile);
    }

    private static string DetectImageExtension(byte[] data)
    {
        if (data.Length == 0 || data.Length > AvatarMaxBytes)
        {
            throw DomainException.Invalid($"Avatar image size must be 1..{AvatarMaxBytes} bytes.");
        }

        // PNG: 89 50 4E 47；JPEG: FF D8 FF
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return "png";
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "jpg";
        }

        throw DomainException.Invalid("Avatar image must be PNG or JPEG.");
    }

    private int PickTalent(PlayerProfile profile)
    {
        var pool = new List<int>();
        foreach (var talentId in _config.Tables.TalentRows.Select(t => t.TalentId).Distinct())
        {
            var count = profile.Talent.GetCount(talentId);
            var copies = _config.Balance.CopiesPerLevel <= 0 ? 1 : _config.Balance.CopiesPerLevel;
            var maxLevel = _config.Tables.GetTalentMaxLevel(talentId);
            var level = maxLevel <= 0 ? 0 : Math.Min(maxLevel, count / copies);
            if (maxLevel <= 0 || level < maxLevel)
            {
                pool.Add(talentId);
            }
        }

        if (pool.Count == 0)
        {
            throw new DomainException(ErrorCodes.TalentPoolEmpty, "All talents are maxed.");
        }

        lock (_random)
        {
            return pool[_random.Next(pool.Count)];
        }
    }
}
