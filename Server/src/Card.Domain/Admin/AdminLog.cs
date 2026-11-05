namespace CardShare.Domain.Admin;

/// <summary>后台 GM 操作审计记录，落 admin_logs 表。</summary>
public sealed class AdminLogEntry
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string Command { get; set; } = string.Empty;

    public Guid TargetUserId { get; set; }

    /// <summary>命令参数，如 amount=1000；失败时追加错误信息。</summary>
    public string Detail { get; set; } = string.Empty;

    public string RemoteIp { get; set; } = string.Empty;

    public bool Succeeded { get; set; }
}

public interface IAdminLogRepository
{
    Task AddAsync(AdminLogEntry entry, CancellationToken cancellationToken);

    /// <summary>按时间倒序取最近 take 条，take 钳到 1..200。</summary>
    Task<IReadOnlyList<AdminLogEntry>> ListAsync(int take, CancellationToken cancellationToken);
}
