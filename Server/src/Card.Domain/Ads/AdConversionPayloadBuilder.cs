using System.Text.Json;

namespace CardShare.Domain.Ads;

/// <summary>
/// 巨量转化回传请求体拼接（新版事件管理接口）：POST https://analytics.oceanengine.com/api/v2/conversion
/// {"event_type":"game_addiction","context":{"ad":{"callback":"&lt;clickid&gt;"}},"timestamp":ms}
/// 关键行为事件（观看激励视频次数 / IPU 次数），不带金额；os 已知时附带 context.device.platform（0=android，1=ios）。
/// 官方文档：https://event-manager.oceanengine.com/docs/8650/5bltdv1c
/// </summary>
public static class AdConversionPayloadBuilder
{
    public const string KeyBehaviorEventType = "game_addiction";

    public static string BuildKeyBehaviorBody(
        string callback,
        int os,
        long timestampMs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("event_type", KeyBehaviorEventType);
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
