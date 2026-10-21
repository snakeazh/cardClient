using System.Text.Json;

namespace CardShare.Domain.Ads;

/// <summary>
/// 巨量转化回传请求体拼接（新版事件管理接口）：POST https://analytics.oceanengine.com/api/v2/conversion
/// {"event_type":"active_pay","context":{"ad":{"callback":"&lt;clickid&gt;"}},"properties":{"pay_amount":N},"timestamp":ms}
/// 付费金额单位分（int）；os 已知时附带 context.device.platform（0=android，1=ios）。
/// 官方文档：https://event-manager.oceanengine.com/docs/8650/5bltdv1c
/// </summary>
public static class AdConversionPayloadBuilder
{
    public const string PaidEventType = "active_pay";

    public static string BuildPaidConversionBody(
        string callback,
        int os,
        int payAmountFen,
        long timestampMs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("event_type", PaidEventType);
            writer.WriteStartObject("context");
            writer.WriteStartObject("ad");
            writer.WriteString("callback", callback);
            writer.WriteEndObject();
            var platform = PlatformOf(os);
            if (platform != null)
            {
                writer.WriteStartObject("device");
                writer.WriteString("platform", platform);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteStartObject("properties");
            writer.WriteNumber("pay_amount", payAmountFen);
            writer.WriteEndObject();
            writer.WriteNumber("timestamp", timestampMs);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? PlatformOf(int os)
    {
        return os switch
        {
            0 => "android",
            1 => "ios",
            _ => null
        };
    }
}
