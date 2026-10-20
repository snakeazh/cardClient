using System.Text;

namespace CardShare.Domain.Ads;

/// <summary>
/// 巨量转化回传 URL 拼接：{base}?callback=..&amp;os=..&amp;event_type=2&amp;props=urlencode({"pay_amount":N})。
/// 付费事件 event_type=2，金额单位分；新版接口有出入时优先改配置里的 ConversionUrl。
/// </summary>
public static class AdConversionUrlBuilder
{
    public const int PaidEventType = 2;

    public static string BuildPaidConversionUrl(
        string baseUrl,
        string callback,
        int os,
        int payAmountFen,
        string? accessToken)
    {
        var props = "{\"pay_amount\":" + payAmountFen + "}";
        var builder = new StringBuilder();
        builder.Append(baseUrl.TrimEnd('?'));
        builder.Append(baseUrl.Contains('?') ? '&' : '?');
        builder.Append("callback=").Append(Uri.EscapeDataString(callback));
        builder.Append("&os=").Append(os);
        builder.Append("&event_type=").Append(PaidEventType);
        builder.Append("&props=").Append(Uri.EscapeDataString(props));
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            builder.Append("&access_token=").Append(Uri.EscapeDataString(accessToken));
        }

        return builder.ToString();
    }
}
