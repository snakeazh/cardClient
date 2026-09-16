namespace CardShare.Contracts;

public enum BattleCommandType
{
    Deal = 1,
    Open = 2,
    Showdown = 3
}

public sealed class BattleCommand
{
    public BattleCommandType Type { get; set; }

    public int SeatId { get; set; }
}

public enum BattleEventType
{
    Dealt = 1,
    Compared = 2,
    RoomReady = 3
}

public sealed class BattleEvent
{
    public BattleEventType Type { get; set; }

    public int SeatId { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class CombatTalentCount
{
    public int TalentId { get; set; }

    public int Count { get; set; }
}

public sealed class SeatSetup
{
    public int SeatId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string NickName { get; set; } = string.Empty;

    public bool IsHuman { get; set; }

    public bool Alive { get; set; } = true;

    public int Attack { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int HeroId { get; set; }

    public IReadOnlyList<CombatTalentCount> Talents { get; set; } = Array.Empty<CombatTalentCount>();
}

public enum BattleModeKind
{
    Pve = 1,
    Pvp = 2
}

public enum BattlePhase
{
    Idle = 0,
    Dealt = 1,
    Showdown = 2
}

public sealed class CardDto
{
    public int Suit { get; set; }

    public int Rank { get; set; }
}

public sealed class BattleSeatDto
{
    public int SeatId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string NickName { get; set; } = string.Empty;

    public bool IsHuman { get; set; }

    public bool Alive { get; set; } = true;

    /// <summary>未摊牌时对手为 null，只亮自己的手牌。</summary>
    public IReadOnlyList<CardDto>? Cards { get; set; }

    public string? HandType { get; set; }

    public string? Label { get; set; }

    public int? Level { get; set; }

    public float? Multiplier { get; set; }

    public int? Damage { get; set; }
}

public sealed class BattleStateDto
{
    public string RoomId { get; set; } = string.Empty;

    public int Seed { get; set; }

    public string Mode { get; set; } = string.Empty;

    public string Phase { get; set; } = string.Empty;

    public int ViewerSeat { get; set; }

    public IReadOnlyList<BattleSeatDto> Seats { get; set; } = Array.Empty<BattleSeatDto>();

    public IReadOnlyList<int> Winners { get; set; } = Array.Empty<int>();
}

public sealed class WsBattleActionPayload
{
    public string Action { get; set; } = string.Empty;
}
