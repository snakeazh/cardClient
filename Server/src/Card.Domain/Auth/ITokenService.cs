namespace CardShare.Domain;

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
