using CardShare.Contracts;
using CardShare.Domain.Config;
using CardShare.Domain.Players;
using CardShare.Domain.Pve;

namespace CardShare.Domain.Services;

public sealed class AuthService
{
    private readonly IEnumerable<ICodeSessionClient> _clients;
    private readonly IAuthBindingRepository _bindings;
    private readonly IPlayerRepository _players;
    private readonly ITokenService _tokens;
    private readonly IGameConfig _config;
    private readonly IClock _clock;
    private readonly bool _guestEnabled;
    private readonly IPveRunService _runs;

    public AuthService(
        IEnumerable<ICodeSessionClient> clients,
        IAuthBindingRepository bindings,
        IPlayerRepository players,
        ITokenService tokens,
        IGameConfig config,
        IClock clock,
        bool guestEnabled,
        IPveRunService runs)
    {
        _clients = clients;
        _bindings = bindings;
        _players = players;
        _tokens = tokens;
        _config = config;
        _clock = clock;
        _guestEnabled = guestEnabled;
        _runs = runs;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Code))
        {
            throw DomainException.Invalid("provider and code are required.");
        }

        var provider = request.Provider.Trim().ToLowerInvariant();
        if (provider == AuthProviders.Guest && !_guestEnabled)
        {
            throw new DomainException(ErrorCodes.GuestDisabled, "Guest login is disabled.");
        }

        var client = _clients.FirstOrDefault(c =>
            string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase));
        if (client == null)
        {
            throw DomainException.Invalid($"Unknown auth provider '{request.Provider}'.");
        }

        var session = await client.ExchangeAsync(request.Code.Trim(), cancellationToken);
        var userId = await _bindings.FindUserIdAsync(provider, session.OpenId, cancellationToken);
        PlayerProfile profile;
        if (userId == null)
        {
            userId = Guid.NewGuid();
            profile = PlayerProfile.CreateNew(userId.Value, _config, _clock.UtcNow);
            await _players.SaveAsync(profile, cancellationToken);
            await _bindings.BindAsync(provider, session.OpenId, userId.Value, cancellationToken);
        }
        else
        {
            profile = await LoadProfileAsync(userId.Value, cancellationToken);
        }

        profile.ApplyDailyReset(_config, _clock.UtcNow);
        profile.ApplyUserInfo(request.UserInfo);
        await _players.SaveAsync(profile, cancellationToken);

        var profileDto = await _runs.ResolveRunsOnLoginAsync(userId.Value, request.PendingSettle, cancellationToken);
        var ticket = await _tokens.IssueAsync(userId.Value, cancellationToken);
        return new LoginResponse
        {
            AccessToken = ticket.AccessToken,
            RefreshToken = ticket.RefreshToken,
            ExpiresInSeconds = ticket.ExpiresInSeconds,
            Profile = profileDto
        };
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw DomainException.Invalid("refreshToken is required.");
        }

        var ticket = await _tokens.RefreshAsync(request.RefreshToken, cancellationToken);
        if (ticket == null)
        {
            throw DomainException.Unauthorized("Invalid refresh token.");
        }

        await LoadProfileAsync(ticket.UserId, cancellationToken);
        var profileDto = await _runs.ResolveRunsOnLoginAsync(ticket.UserId, request.PendingSettle, cancellationToken);
        return new LoginResponse
        {
            AccessToken = ticket.AccessToken,
            RefreshToken = ticket.RefreshToken,
            ExpiresInSeconds = ticket.ExpiresInSeconds,
            Profile = profileDto
        };
    }

    public async Task<PlayerProfile> LoadProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await _players.GetAsync(userId, cancellationToken);
        if (profile == null)
        {
            throw DomainException.Unauthorized("Player not found.");
        }

        profile.ApplyDailyReset(_config, _clock.UtcNow);
        return profile;
    }
}
