using System.Net.Http.Json;
using System.Text.Json;
using CardShare.Contracts;
using CardShare.Domain;
using Microsoft.Extensions.Options;

namespace CardShare.Infrastructure.Auth;

public sealed class GuestAuthOptions
{
    public bool Enabled { get; set; }
}

public sealed class WeChatAuthOptions
{
    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;
}

public sealed class DouyinAuthOptions
{
    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;
}

public sealed class GuestCodeSessionClient : ICodeSessionClient
{
    private readonly GuestAuthOptions _options;

    public GuestCodeSessionClient(IOptions<GuestAuthOptions> options)
    {
        _options = options.Value;
    }

    public string Provider => AuthProviders.Guest;

    public Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new DomainException(ErrorCodes.GuestDisabled, "Guest login is disabled.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw DomainException.Invalid("Guest code (device id) is required.");
        }

        return Task.FromResult(new CodeSession("guest:" + code.Trim()));
    }
}

public sealed class WeChatCodeSessionClient : ICodeSessionClient
{
    private readonly WeChatAuthOptions _options;
    private readonly HttpClient _http;

    public WeChatCodeSessionClient(IOptions<WeChatAuthOptions> options, HttpClient http)
    {
        _options = options.Value;
        _http = http;
    }

    public string Provider => AuthProviders.WeChat;

    public async Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret))
        {
            throw new DomainException(ErrorCodes.ProviderNotConfigured, "WeChat AppId/AppSecret is not configured.");
        }

        var url =
            $"https://api.weixin.qq.com/sns/jscode2session?appid={Uri.EscapeDataString(_options.AppId)}&secret={Uri.EscapeDataString(_options.AppSecret)}&js_code={Uri.EscapeDataString(code)}&grant_type=authorization_code";
        using var response = await _http.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
        {
            var msg = doc.RootElement.TryGetProperty("errmsg", out var m) ? m.GetString() : "WeChat login failed.";
            throw DomainException.Invalid(msg ?? "WeChat login failed.");
        }

        if (!doc.RootElement.TryGetProperty("openid", out var openId) || string.IsNullOrWhiteSpace(openId.GetString()))
        {
            throw DomainException.Invalid("WeChat login did not return openid.");
        }

        return new CodeSession(openId.GetString()!);
    }
}

public sealed class DouyinCodeSessionClient : ICodeSessionClient
{
    private readonly DouyinAuthOptions _options;
    private readonly HttpClient _http;

    public DouyinCodeSessionClient(IOptions<DouyinAuthOptions> options, HttpClient http)
    {
        _options = options.Value;
        _http = http;
    }

    public string Provider => AuthProviders.Douyin;

    public async Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret))
        {
            throw new DomainException(ErrorCodes.ProviderNotConfigured, "Douyin AppId/AppSecret is not configured.");
        }

        var body = new
        {
            appid = _options.AppId,
            secret = _options.AppSecret,
            code
        };
        using var response = await _http.PostAsJsonAsync(
            "https://developer.toutiao.com/api/apps/v2/jscode2session",
            body,
            cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.TryGetProperty("data", out var nested) ? nested : doc.RootElement;
        if (!data.TryGetProperty("openid", out var openId) || string.IsNullOrWhiteSpace(openId.GetString()))
        {
            var err = doc.RootElement.TryGetProperty("err_tips", out var tips)
                ? tips.GetString()
                : "Douyin login failed.";
            throw DomainException.Invalid(err ?? "Douyin login failed.");
        }

        return new CodeSession(openId.GetString()!);
    }
}
