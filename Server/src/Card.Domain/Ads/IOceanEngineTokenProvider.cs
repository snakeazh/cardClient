namespace CardShare.Domain.Ads;

/// <summary>巨量 Marketing API token 体系：access_token（24h）+ refresh_token（30d），刷新后旧 refresh 作废。</summary>
public interface IOceanEngineTokenProvider
{
    /// <summary>有效 access_token；未授权或 refresh 失效返回 null（原因由实现记日志）。</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>首次授权：auth_code 换 access/refresh 并持久化。</summary>
    Task<OceanEngineAuthorizeResult> AuthorizeAsync(string authCode, CancellationToken cancellationToken);
}

public sealed class OceanEngineAuthorizeResult
{
    public bool Success { get; set; }

    public string Error { get; set; } = string.Empty;
}
