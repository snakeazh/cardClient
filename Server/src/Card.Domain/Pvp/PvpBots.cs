using System;
using System.Collections.Generic;
using System.Linq;
using CardShare.Contracts;
using CardShare.Contracts.Config;

namespace CardShare.Domain.Pvp;

public static class PvpBots
{
    public static PlayerPublic Create(IGameTables tables, int index)
    {
        if (tables == null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var row = Pick(tables.PvpBots, index);
        return new PlayerPublic
        {
            UserId = Guid.NewGuid().ToString("N"),
            NickName = string.IsNullOrWhiteSpace(row.NickName) ? $"机器人{index + 1}" : row.NickName,
            AvatarUrl = row.AvatarUrl ?? string.Empty,
            IsBot = true,
            BotConfigId = row.Id
        };
    }

    private static PvpBotConfig Pick(IReadOnlyList<PvpBotConfig> bots, int index)
    {
        var enabled = bots
            .Where(b => b != null && b.Id > 0 && b.Enabled)
            .OrderBy(b => b.Id)
            .ToArray();
        if (enabled.Length == 0)
        {
            throw new InvalidOperationException("PvpBotConfig 无可用机器人，无法补房。");
        }

        return enabled[index % enabled.Length];
    }
}

/// <summary>开发用：真人入队后立刻用机器人补满一房。</summary>
public sealed class PvpBotFillMatchmaker : IPvpMatchmaker
{
    private readonly IPvpMatchmaker _inner;
    private readonly IGameTables _tables;

    public PvpBotFillMatchmaker(IPvpMatchmaker inner, IGameTables tables)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
    }

    public MatchEvent Enqueue(PlayerPublic player)
    {
        var evt = _inner.Enqueue(player);
        if (evt.RoomOpened || player.IsBot)
        {
            return evt;
        }

        var pad = 0;
        while (!evt.RoomOpened && pad < PvpRules.RoomSize)
        {
            evt = _inner.Enqueue(PvpBots.Create(_tables, pad));
            pad++;
        }

        return evt;
    }

    public MatchEvent? Cancel(Guid userId) => _inner.Cancel(userId);

    public void Leave(Guid userId) => _inner.Leave(userId);

    public IReadOnlyList<Guid> SweepExpired(long nowUtcMs, long timeoutMs, out IReadOnlyList<PlayerPublic> remaining)
        => _inner.SweepExpired(nowUtcMs, timeoutMs, out remaining);
}
