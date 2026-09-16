using CardShare.Contracts;
using CardShare.Domain.Config;
using CardShare.Domain.Players;

namespace CardShare.Domain.Services;

public sealed class PlayerMetaService
{
    private readonly PlayerSession _session;
    private readonly IGameConfig _config;
    private readonly Random _random = new Random();

    public PlayerMetaService(PlayerSession session, IGameConfig config)
    {
        _session = session;
        _config = config;
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

    public Task<EnergyRefillResponse> RefillEnergyByAdAsync(Guid userId, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.RefillEnergyByAd(_config);
            return new EnergyRefillResponse { Profile = ProfileMapper.ToDto(profile) };
        }, cancellationToken);

    public Task<AdShopClaimResponse> ClaimAdShopAsync(Guid userId, string kind, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.ClaimAdShop(kind, _config);
            return new AdShopClaimResponse { Profile = ProfileMapper.ToDto(profile) };
        }, cancellationToken);

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

    public Task<PlayerProfileDto> DebugGrantGoldAsync(Guid userId, int amount, CancellationToken cancellationToken)
        => _session.MutateAsync(userId, profile =>
        {
            profile.GrantWalletGold(amount);
            return ProfileMapper.ToDto(profile);
        }, cancellationToken);

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
