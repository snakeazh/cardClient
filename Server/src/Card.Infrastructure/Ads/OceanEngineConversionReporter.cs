using System.Text;
using System.Text.Json;
using CardShare.Domain.Ads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardShare.Infrastructure.Ads;

public sealed class OceanEngineOptions
{
    /// <summary>总开关：false 时完全不回传。</summary>
    public bool Enabled { get; set; }

    /// <summary>转化回传地址，新版事件管理接口。</summary>
    public string ConversionUrl { get; set; } = "https://analytics.oceanengine.com/api/v2/conversion";
}

internal static class OceanEngineHttp
{
    public const string ClientName = "OceanEngine";
}

/// <summary>
/// 巨量转化回传（新版事件管理接口）：广告观看完成后以 game_addiction（关键行为）事件 POST JSON 回传，
/// 即回传观看激励视频次数 / IPU 次数，不带金额。跳过条件：未启用、玩家无 clickid。官方文档：
/// https://event-manager.oceanengine.com/docs/8650/5bltdv1c
/// </summary>
public sealed class OceanEngineConversionReporter : IAdConversionReporter
{
    private const int MaxBodyLogLength = 200;

    private readonly IHttpClientFactory _httpFactory;
    private readonly OceanEngineOptions _options;
    private readonly ILogger<OceanEngineConversionReporter> _logger;

    public OceanEngineConversionReporter(
        IHttpClientFactory httpFactory,
        IOptions<OceanEngineOptions> options,
        ILogger<OceanEngineConversionReporter> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public void ReportAdWatched(AdAttributionState attribution, string scene)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (attribution == null || !attribution.HasCallback)
        {
            _logger.LogDebug("OceanEngine conversion skipped ({Scene}): no usable attribution.", scene);
            return;
        }

        _ = ReportSafelyAsync(attribution.Snapshot(), scene ?? string.Empty);
    }

    private async Task ReportSafelyAsync(AdAttributionState attribution, string scene)
    {
        try
        {
            var body = AdConversionPayloadBuilder.BuildKeyBehaviorBody(
                attribution.Callback,
                attribution.Os,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            using var response = await _httpFactory.CreateClient(OceanEngineHttp.ClientName)
                .PostAsync(_options.ConversionUrl, new StringContent(body, Encoding.UTF8, "application/json"), CancellationToken.None);
            var responseBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
            var code = TryReadCode(responseBody);
            if (!response.IsSuccessStatusCode || (code.HasValue && code.Value != 0))
            {
                _logger.LogWarning(
                    "OceanEngine conversion failed ({Scene}): status={Status} body={Body}",
                    scene,
                    (int)response.StatusCode,
                    Truncate(responseBody));
                return;
            }

            _logger.LogInformation(
                "OceanEngine conversion reported ({Scene}): event={EventType} os={Os}",
                scene,
                AdConversionPayloadBuilder.KeyBehaviorEventType,
                attribution.Os);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OceanEngine conversion error ({Scene})", scene);
        }
    }

    private static int? TryReadCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
            {
                return code.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string Truncate(string body)
    {
        return body.Length <= MaxBodyLogLength ? body : body.Substring(0, MaxBodyLogLength);
    }
}
