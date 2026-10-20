using System.Net.Http.Json;
using System.Text.Json;
using CardShare.Domain.Ads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CardShare.Infrastructure.Ads;

public sealed class OceanEngineOptions
{
    /// <summary>总开关：false 时完全不回传、不请求 token。</summary>
    public bool Enabled { get; set; }

    /// <summary>Marketing API 应用 APPID（数字，配置为字符串即可）。</summary>
    public string AppId { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    /// <summary>单次广告观看的回传金额（分）；&lt;=0 时不回传。平台无单次价格 API，按运营折算配置。</summary>
    public int PayAmountFen { get; set; }

    /// <summary>转化回传地址；老接口为 track/activate，新版接口按运营文档替换。</summary>
    public string ConversionUrl { get; set; } = "https://ad.toutiao.com/track/activate/";

    public string AccessTokenUrl { get; set; } = "https://ad.oceanengine.com/open_api/oauth2/access_token/";

    public string RefreshTokenUrl { get; set; } = "https://ad.oceanengine.com/open_api/oauth2/refresh_token/";

    /// <summary>回传时是否附带 access_token（新版接口需要；老 track/activate 可关）。</summary>
    public bool AttachAccessToken { get; set; } = true;
}

internal static class OceanEngineHttp
{
    public const string ClientName = "OceanEngine";
}

/// <summary>
/// 巨量 Marketing API token：access_token 存 Redis（TTL=expires_in−5min），refresh_token 存 Redis（29 天）；
/// access 过期自动用 refresh 换新（刷新后旧 refresh 作废，必须保存新值）；refresh 被拒则清除并等待重新授权。
/// </summary>
public sealed class OceanEngineTokenProvider : IOceanEngineTokenProvider
{
    private const string AccessKey = "ocean:access_token";
    private const string RefreshKey = "ocean:refresh_token";
    private static readonly TimeSpan AccessExpiryBuffer = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshTtl = TimeSpan.FromDays(29);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IDatabase _db;
    private readonly OceanEngineOptions _options;
    private readonly ILogger<OceanEngineTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

    public OceanEngineTokenProvider(
        IHttpClientFactory httpFactory,
        IConnectionMultiplexer redis,
        IOptions<OceanEngineOptions> options,
        ILogger<OceanEngineTokenProvider> logger)
    {
        _httpFactory = httpFactory;
        _db = redis.GetDatabase();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured())
        {
            return null;
        }

        var access = await _db.StringGetAsync(AccessKey);
        if (access.HasValue)
        {
            return access.ToString();
        }

        return await RefreshAsync(cancellationToken);
    }

    public async Task<OceanEngineAuthorizeResult> AuthorizeAsync(string authCode, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
        {
            return new OceanEngineAuthorizeResult { Error = "OceanEngine AppId/Secret is not configured." };
        }

        if (string.IsNullOrWhiteSpace(authCode))
        {
            return new OceanEngineAuthorizeResult { Error = "auth_code is required." };
        }

        var body = new Dictionary<string, object>
        {
            ["app_id"] = AppIdValue(),
            ["secret"] = _options.Secret,
            ["auth_code"] = authCode.Trim()
        };
        var result = await RequestTokenAsync(_options.AccessTokenUrl, body, cancellationToken);
        if (result.Bundle == null)
        {
            return new OceanEngineAuthorizeResult { Error = result.Error };
        }

        await StoreAsync(result.Bundle);
        _logger.LogInformation("OceanEngine authorized; access/refresh token stored.");
        return new OceanEngineAuthorizeResult { Success = true };
    }

    private async Task<string?> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var access = await _db.StringGetAsync(AccessKey);
            if (access.HasValue)
            {
                return access.ToString();
            }

            var refresh = await _db.StringGetAsync(RefreshKey);
            if (!refresh.HasValue)
            {
                _logger.LogWarning("OceanEngine is not authorized: no refresh token stored; complete the OAuth authorize step first.");
                return null;
            }

            var body = new Dictionary<string, object>
            {
                ["app_id"] = AppIdValue(),
                ["secret"] = _options.Secret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh.ToString()
            };
            var result = await RequestTokenAsync(_options.RefreshTokenUrl, body, cancellationToken);
            if (result.Bundle == null)
            {
                if (result.Rejected)
                {
                    // refresh 已过期或已被刷新作废：清除后只能重新授权
                    await _db.KeyDeleteAsync(RefreshKey);
                    _logger.LogWarning("OceanEngine refresh token rejected and cleared; re-authorization is required. {Error}", result.Error);
                }

                return null;
            }

            await StoreAsync(result.Bundle);
            return result.Bundle.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task StoreAsync(TokenBundle bundle)
    {
        var ttl = TimeSpan.FromSeconds(bundle.ExpiresInSeconds) - AccessExpiryBuffer;
        if (ttl < TimeSpan.FromMinutes(1))
        {
            ttl = TimeSpan.FromMinutes(1);
        }

        await _db.StringSetAsync(AccessKey, bundle.AccessToken, ttl);
        await _db.StringSetAsync(RefreshKey, bundle.RefreshToken, RefreshTtl);
    }

    private async Task<TokenRequestResult> RequestTokenAsync(
        string url,
        Dictionary<string, object> body,
        CancellationToken cancellationToken)
    {
        string json;
        try
        {
            using var response = await _httpFactory.CreateClient(OceanEngineHttp.ClientName)
                .PostAsJsonAsync(url, body, cancellationToken);
            json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return TokenRequestResult.Failure($"HTTP {(int)response.StatusCode}: {json}", rejected: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OceanEngine token request error: {Url}", url);
            return TokenRequestResult.Failure(ex.Message, rejected: false);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
            if (code != 0)
            {
                var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : json;
                return TokenRequestResult.Failure($"code={code} message={message}", rejected: true);
            }

            if (!root.TryGetProperty("data", out var data))
            {
                return TokenRequestResult.Failure("missing data in token response: " + json, rejected: true);
            }

            var access = data.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var refresh = data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = data.TryGetProperty("expires_in", out var exp) && exp.ValueKind == JsonValueKind.Number
                ? exp.GetInt32()
                : 86400;
            if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
            {
                return TokenRequestResult.Failure("token response missing access/refresh token: " + json, rejected: true);
            }

            return new TokenRequestResult(new TokenBundle(access!, refresh!, expiresIn), string.Empty, false);
        }
        catch (JsonException)
        {
            return TokenRequestResult.Failure("invalid token response: " + json, rejected: false);
        }
    }

    private object AppIdValue()
    {
        return long.TryParse(_options.AppId, out var id) ? id : _options.AppId;
    }

    private bool IsConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_options.AppId) && !string.IsNullOrWhiteSpace(_options.Secret))
        {
            return true;
        }

        _logger.LogWarning("OceanEngine AppId/Secret is not configured.");
        return false;
    }

    private sealed record TokenBundle(string AccessToken, string RefreshToken, int ExpiresInSeconds);

    private sealed record TokenRequestResult(TokenBundle? Bundle, string Error, bool Rejected)
    {
        public static TokenRequestResult Failure(string error, bool rejected) => new TokenRequestResult(null, error, rejected);
    }
}
