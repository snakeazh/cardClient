using CardShare.Contracts;

namespace CardShare.Domain.Pvp;

public static class PvpBots
{
    private static readonly string[] Nicks = { "机器人甲", "机器人乙", "机器人丙" };

    public static PlayerPublic Create(int index)
    {
        return new PlayerPublic
        {
            UserId = Guid.NewGuid().ToString("N"),
            NickName = Nicks[index % Nicks.Length],
            IsBot = true
        };
    }
}

/// <summary>开发用：真人入队后立刻用机器人补满一房。</summary>
public sealed class PvpBotFillMatchmaker : IPvpMatchmaker
{
    private readonly IPvpMatchmaker _inner;

    public PvpBotFillMatchmaker(IPvpMatchmaker inner)
    {
        _inner = inner;
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
            evt = _inner.Enqueue(PvpBots.Create(pad));
            pad++;
        }

        return evt;
    }

    public MatchEvent? Cancel(Guid userId) => _inner.Cancel(userId);

    public void Leave(Guid userId) => _inner.Leave(userId);
}
