using System;
using System.Collections.Generic;
using CardShare.Contracts;

namespace CardShare.Battle;

/// <summary>单机主线：本地 <see cref="PveMode"/>，不联网。发 3 张公开牌，余牌用 <see cref="DrawExtra"/>。</summary>
public sealed class PveLocalSession
{
    public const string PlayerUserId = "pve-player";

    private readonly BattleEngine _engine;

    private PveLocalSession(BattleEngine engine)
    {
        _engine = engine;
    }

    public BattleEngine Engine => _engine;

    public BattleSnapshot Snapshot => _engine.Snapshot;

    public static IReadOnlyList<SeatSetup> DefaultSeats()
    {
        return new[]
        {
            new SeatSetup { SeatId = 0, UserId = PlayerUserId, NickName = "你", IsHuman = true, Alive = true },
            new SeatSetup { SeatId = 1, UserId = "pve-ai-1", NickName = "敌人A", IsHuman = false, Alive = true },
            new SeatSetup { SeatId = 2, UserId = "pve-ai-2", NickName = "敌人B", IsHuman = false, Alive = true },
            new SeatSetup { SeatId = 3, UserId = "pve-ai-3", NickName = "敌人C", IsHuman = false, Alive = true }
        };
    }

    public static PveLocalSession Create(IGameTables tables, int seed, IReadOnlyList<SeatSetup>? seats = null)
    {
        if (tables == null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var engine = new BattleEngine(new PveMode(), seed, seats ?? DefaultSeats(), tables);
        return new PveLocalSession(engine);
    }

    public BattleSnapshot Deal()
        => _engine.Apply(new BattleCommand { Type = BattleCommandType.Deal });

    public BattleSnapshot Showdown()
        => _engine.Apply(new BattleCommand { Type = BattleCommandType.Showdown });

    public Card DrawExtra() => _engine.DrawExtra();
}
