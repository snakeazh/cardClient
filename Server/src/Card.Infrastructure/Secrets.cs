using System.Text.RegularExpressions;

namespace CardShare.Infrastructure;

/// <summary>
/// 连接串脱敏。日志会进 journald（并可能被贴到聊天/工单里），密码一律不得原样输出。
/// 同时兼容 Postgres 的 "Password=..." 与 Redis 的 "...;password=..."、",password=..." 两种写法。
/// </summary>
public static class Secrets
{
    private static readonly Regex PasswordPattern = new(
        @"(?<key>\b(?:Password|Pwd)\s*=\s*)(?<value>[^;,}]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "(empty)";
        }

        return PasswordPattern.Replace(connectionString, "${key}***");
    }
}
