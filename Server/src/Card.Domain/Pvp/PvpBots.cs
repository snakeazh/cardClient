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

/// <summary>
/// 机器人补房：真人排队超过 GameConst.PvpBotFillDelaySeconds 秒还没满 4 人，用机器人补满开房。
/// 配 0 = 入队立即补满（旧行为）。到期检测由外部定时器调 FillDueBots 驱动。
/// </summary>
public sealed class PvpBotFillMatchmaker : IPvpMatchmaker
{
    private readonly IPvpMatchmaker _inner;
    private readonly IGameTables _tables;
    private readonly Func<long> _nowUtcMs;
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, long> _pendingDueUtcMs = new Dictionary<Guid, long>();

    public PvpBotFillMatchmaker(IPvpMatchmaker inner, IGameTables tables, Func<long>? nowUtcMs = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _nowUtcMs = nowUtcMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public int FillDelayMs => Math.Max(0, _tables.GameConst.PvpBotFillDelaySeconds) * 1000;

    public MatchEvent Enqueue(PlayerPublic player)
    {
        if (FillDelayMs <= 0)
        {
            return FillNow(_inner.Enqueue(player), player);
        }

        var evt = _inner.Enqueue(player);
        ClearSeatedPending(evt);
        if (evt.RoomOpened || player.IsBot || !Guid.TryParse(player.UserId, out var userId))
        {
            return evt;
        }

        lock (_gate)
        {
            _pendingDueUtcMs[userId] = _nowUtcMs() + FillDelayMs;
        }

        return evt;
    }

    /// <summary>到期真人用机器人补满开房，返回开房事件；无到期或无变化返回 null。每次最多开一房。</summary>
    public MatchEvent? FillDueBots()
    {
        var now = _nowUtcMs();
        while (true)
        {
            Guid dueUser;
            lock (_gate)
            {
                var min = long.MaxValue;
                dueUser = Guid.Empty;
                foreach (var pair in _pendingDueUtcMs)
                {
                    if (pair.Value <= now && pair.Value < min)
                    {
                        min = pair.Value;
                        dueUser = pair.Key;
                    }
                }

                if (dueUser == Guid.Empty)
                {
                    return null;
                }

                _pendingDueUtcMs.Remove(dueUser);
            }

            // 已取消/已被队列超时踢出：丢弃这条，继续看下一条
            if (!_inner.IsWaiting(dueUser))
            {
                continue;
            }

            var evt = FillRoom();
            ClearSeatedPending(evt);
            return evt;
        }
    }

    public MatchEvent? Cancel(Guid userId)
    {
        lock (_gate)
        {
            _pendingDueUtcMs.Remove(userId);
        }

        return _inner.Cancel(userId);
    }

    public void Leave(Guid userId) => Cancel(userId);

    public bool IsWaiting(Guid userId) => _inner.IsWaiting(userId);

    public IReadOnlyList<Guid> SweepExpired(long nowUtcMs, long timeoutMs, out IReadOnlyList<PlayerPublic> remaining)
        => _inner.SweepExpired(nowUtcMs, timeoutMs, out remaining);

    /// <summary>配 0 时的旧行为：真人入队立刻补满。</summary>
    private MatchEvent FillNow(MatchEvent evt, PlayerPublic player)
    {
        if (evt.RoomOpened || player.IsBot)
        {
            return evt;
        }

        return FillRoom();
    }

    private MatchEvent FillRoom()
    {
        MatchEvent evt;
        var pad = 0;
        do
        {
            evt = _inner.Enqueue(PvpBots.Create(_tables, pad));
            pad++;
        }
        while (!evt.RoomOpened && pad < PvpRules.RoomSize);

        return evt;
    }

    /// <summary>开房后清掉已入座真人的待补房截止时间。</summary>
    private void ClearSeatedPending(MatchEvent evt)
    {
        if (!evt.RoomOpened)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var player in evt.Players)
            {
                if (Guid.TryParse(player.UserId, out var id))
                {
                    _pendingDueUtcMs.Remove(id);
                }
            }
        }
    }
}
