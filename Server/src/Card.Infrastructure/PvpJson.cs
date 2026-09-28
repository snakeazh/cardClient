using System.Text.Json;

namespace CardShare.Infrastructure;

/// <summary>PvP 协议 JSON 选项：WS 直发与 Redis 转发是同一协议的两条投递路径，必须字节一致。</summary>
public static class PvpJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
