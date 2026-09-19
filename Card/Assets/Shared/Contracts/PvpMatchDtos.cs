#nullable enable

namespace CardShare.Contracts
{
    public sealed class PvpFighterDto
    {
        public string UserId { get; set; } = string.Empty;

        public string NickName { get; set; } = string.Empty;

        public int Hp { get; set; }

        public int MaxHp { get; set; }

        public int HeroId { get; set; }

        /// <summary>基础攻击力：英雄底值 + 天赋加成（BuildSeat 时的面板攻击），比牌前的展示值。</summary>
        public int Attack { get; set; }

        public int Gold { get; set; }

        public bool Alive { get; set; } = true;

        public int Rank { get; set; }

        public int RubLeft { get; set; }

        public int ReplaceLeft { get; set; }

        public int PeekLeft { get; set; }

        public bool IsBot { get; set; }
    }

    public sealed class PvpDuelSummaryDto
    {
        public string LeftUserId { get; set; } = string.Empty;

        public string RightUserId { get; set; } = string.Empty;

        public string MonsterName { get; set; } = string.Empty;

        public bool Resolved { get; set; }
    }

    public sealed class PvpMatchStateDto
    {
        public string RoomId { get; set; } = string.Empty;

        public int Seed { get; set; }

        public int ModeId { get; set; }

        public string ModeName { get; set; } = string.Empty;

        public int Round { get; set; }

        public string Phase { get; set; } = string.Empty;

        public string FightKind { get; set; } = string.Empty;

        public PvpFighterDto[] Players { get; set; } = System.Array.Empty<PvpFighterDto>();

        public PvpDuelSummaryDto[] Duels { get; set; } = System.Array.Empty<PvpDuelSummaryDto>();

        public BattleStateDto? Duel { get; set; }

        /// <summary>自己这桌结算后的实际伤害（含轮次系数，即扣血量）。0 = 未结算或平局。客户端只做状态同步，不要重算。</summary>
        public int DuelDamage { get; set; }

        /// <summary>当前选牌阶段的服务器截止时刻（UTC 毫秒）。0 = 无倒计时（非选牌阶段或未配置时长）。</summary>
        public long PhaseDeadlineUtcMs { get; set; }

        /// <summary>快照生成时的服务器当前时刻（UTC 毫秒）。客户端用它校准时钟偏移，不要拿本地时钟直接比 deadline。</summary>
        public long ServerNowUtcMs { get; set; }
    }
}
