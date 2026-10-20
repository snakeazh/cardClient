using System.Text.Json;
using CardShare.Domain.Ads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardShare.Infrastructure.Ads;

/// <summary>
/// 巨量转化回传：广告观看完成后按"付费"事件 GET 回传 URL。
/// 跳过条件：未启用、单价未配置、玩家无 callback、OS 未知、（开启时）无可用 access_token。
/// </summary>
public sealed class OceanEngineConversionReporter : IAdConversionReporter
{
    private const int MaxBodyLogLength = 200;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOceanEngineTokenProvider _tokens;
    private readonly OceanEngineOptions _options;
    private readonly ILogger<OceanEngineConversionReporter> _logger;

    public OceanEngineConversionReporter(
        IHttpClientFactory httpFactory,
        IOceanEngineTokenProvider tokens,
        IOptions<OceanEngineOptions> options,
        ILogger<OceanEngineConversionReporter> logger)
    {
        _httpFactory = httpFactory;
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;
    }

    public void ReportAdWatched(AdAttributionState attribution, string scene)
    {
        if (!_options.Enabled || _options.PayAmountFen <= 0)
        {
            return;
        }

        if (attribution == null || !attribution.HasCallback || attribution.Os < 0)
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
            string? accessToken = null;
            if (_options.AttachAccessToken)
            {
                accessToken = await _tokens.GetAccessTokenAsync(CancellationToken.None);
                if (accessToken == null)
                {
                    _logger.LogWarning("OceanEngine conversion skipped ({Scene}): no access token; authorize first.", scene);
                    return;
                }
            }

            var url = AdConversionUrlBuilder.BuildPaidConversionUrl(
                _options.ConversionUrl,
                attribution.Callback,
                attribution.Os,
                _options.PayAmountFen,
                accessToken);
            using var response = await _httpFactory.CreateClient(OceanEngineHttp.ClientName)
                .GetAsync(url, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            var code = TryReadCode(body);
            if (!response.IsSuccessStatusCode || (code.HasValue && code.Value != 0))
            {
                _logger.LogWarning(
                    "OceanEngine conversion failed ({Scene}): status={Status} body={Body}",
                    scene,
                    (int)response.StatusCode,
                    Truncate(body));
                return;
            }

            _logger.LogInformation(
                "OceanEngine conversion reported ({Scene}): pay_amount={PayAmountFen} os={Os}",
                scene,
                _options.PayAmountFen,
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
