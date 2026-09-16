namespace CardShare.Contracts;

public sealed class LoginUserInfo
{
    public string? NickName { get; set; }

    public string? AvatarUrl { get; set; }
}

public sealed class LoginRequest
{
    public string Provider { get; set; } = string.Empty;

    /// <summary>WeChat/Douyin js code, or guest device fingerprint.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Optional social profile from the client SDK. Not used for identity.</summary>
    public LoginUserInfo? UserInfo { get; set; }
    /// <summary>Optional leftover settle from the client. Applied before any forfeit of an active run.</summary>
    public PveSettleRequest? PendingSettle { get; set; }
}

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Optional leftover settle from the client. Applied before any forfeit of an active run.</summary>
    public PveSettleRequest? PendingSettle { get; set; }
}

public sealed class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public int ExpiresInSeconds { get; set; }

    public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
}
