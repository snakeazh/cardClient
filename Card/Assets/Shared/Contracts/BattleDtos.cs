using System;
using System.Collections.Generic;

#nullable enable

namespace CardShare.Contracts
{
    public enum BattleCommandType
    {
        Deal = 1,
        Open = 2,
        Showdown = 3,
        DealHole = 4,
        Pick = 5,
        Rub = 6,
        Replace = 7
    }

    public sealed class BattleCommand
    {
        public BattleCommandType Type { get; set; }

        public int SeatId { get; set; }

        public int Index { get; set; }

        public int[] Indexes { get; set; } = Array.Empty<int>();
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

        public IReadOnlyList<int> RelicIds { get; set; } = Array.Empty<int>();

        /// <summary>商店可购圣物池（已解锁集合）；仅 PvP 开局时由服务端填入。</summary>
        public int[] ShopPoolIds { get; set; } = Array.Empty<int>();

        /// <summary>各牌型本局亮出次数（炸弹恶魔）。PVP 由对局维护，空则公式为 0。</summary>
        public IReadOnlyList<int> HandTypeShowCounts { get; set; } = Array.Empty<int>();

        /// <summary>高档饮品剩余自衰减倍率。PVP 由对局维护。</summary>
        public IReadOnlyDictionary<int, float>? RelicSelfDecayMag { get; set; }

        /// <summary>消费主义商店刷新计数。PVP 由对局维护。</summary>
        public IReadOnlyDictionary<int, int>? RelicShopRefreshCounts { get; set; }

        /// <summary>贪婪胜负累计倍率。PVP 由对局维护。</summary>
        public IReadOnlyDictionary<int, float>? RelicWinLoseMag { get; set; }

        /// <summary>本局消耗品使用次数。PVP 当前无消耗入口，一般为 0。</summary>
        public int ConsumableUsesThisRun { get; set; }

        /// <summary>复制本回合选中的圣物 Id。PVP 由对局每回合刷新。</summary>
        public int CopiedRelicId { get; set; }

        /// <summary>当前剩余搓牌次数。仅在比牌结算时读取（NoSkill/EveryRubbingNum 等词条）；PVP 由对局在 Compare 前回写。</summary>
        public int RubLeft { get; set; }

        /// <summary>当前剩余透视次数。同上。</summary>
        public int PeekLeft { get; set; }

        /// <summary>当前剩余换牌次数。同上。</summary>
        public int ReplaceLeft { get; set; }
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

        /// <summary>已锁定本轮 3 张。双方都锁定后才比牌。</summary>
        public bool Locked { get; set; }

        /// <summary>未摊牌时对手为 null，只亮自己的手牌。PVP 洞牌为 5 张。</summary>
        public IReadOnlyList<CardDto>? Cards { get; set; }

        /// <summary>5 选 3 的下标；未揭示时为 null。</summary>
        public IReadOnlyList<int>? Selected { get; set; }

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

        public int Index { get; set; }

        public int[] Indexes { get; set; } = Array.Empty<int>();
    }
}
