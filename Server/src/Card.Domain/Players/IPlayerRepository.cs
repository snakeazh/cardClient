using CardShare.Domain.Players;

namespace CardShare.Domain;

public interface IPlayerRepository
{
    Task<PlayerProfile?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveAsync(PlayerProfile profile, CancellationToken cancellationToken);

    /// <summary>后台搜索：按昵称子串或 UserId 片段匹配；keyword 为空时按最近更新返回。take 钳到 1..100。</summary>
    Task<IReadOnlyList<PlayerSummary>> SearchAsync(string keyword, int take, CancellationToken cancellationToken);
}

/// <summary>后台玩家列表用的轻量摘要，不含完整档案。</summary>
public sealed class PlayerSummary
{
    public Guid UserId { get; set; }

    public string NickName { get; set; } = string.Empty;

    public int Gold { get; set; }

    public int Energy { get; set; }

    public int PvpScore { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
