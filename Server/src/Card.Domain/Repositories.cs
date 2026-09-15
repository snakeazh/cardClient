using CardShare.Domain.Players;

namespace CardShare.Domain;

public interface IPlayerRepository
{
    Task<PlayerProfile?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveAsync(PlayerProfile profile, CancellationToken cancellationToken);
}

public interface IAuthBindingRepository
{
    Task<Guid?> FindUserIdAsync(string provider, string openId, CancellationToken cancellationToken);

    Task BindAsync(string provider, string openId, Guid userId, CancellationToken cancellationToken);
}

public enum PveRunStatus
{
    Active = 0,
    Settled = 1
}

public sealed class PveRun
{
    public Guid RunId { get; set; }

    public Guid UserId { get; set; }

    public int LevelId { get; set; }

    public int HeroId { get; set; }

    public int[] ShopRelicIds { get; set; } = Array.Empty<int>();

    public int Gold { get; set; }

    public List<int> RelicIds { get; set; } = new List<int>();

    public List<int> ShopOfferIds { get; set; } = new List<int>();

    public int ShopRefreshCount { get; set; }

    public int FreeShopRefreshLeft { get; set; }

    public PveRunStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    public string? SettleFingerprint { get; set; }

    public int GoldGranted { get; set; }
}

public interface IPveRunRepository
{
    Task AddAsync(PveRun run, CancellationToken cancellationToken);

    Task<PveRun?> GetAsync(Guid runId, CancellationToken cancellationToken);

    Task SaveAsync(PveRun run, CancellationToken cancellationToken);
}

public sealed class AuthTicket
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public int ExpiresInSeconds { get; set; }

    public Guid UserId { get; set; }
}

public interface ITokenService
{
    Task<AuthTicket> IssueAsync(Guid userId, CancellationToken cancellationToken);

    Task<Guid?> ResolveAccessTokenAsync(string accessToken, CancellationToken cancellationToken);

    Task<AuthTicket?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}

public interface IPlayerLock
{
    Task<IAsyncDisposable> AcquireAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class CodeSession
{
    public CodeSession(string openId)
    {
        OpenId = openId;
    }

    public string OpenId { get; }
}

public interface ICodeSessionClient
{
    string Provider { get; }

    Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken);
}
