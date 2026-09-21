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

        public bool Disconnected { get; set; }

        /// <summary>对局结束后的名次奖励金币；未结束为 0。</summary>
        public int RewardGold { get; set; }

        /// <summary>当前持有的圣物（商店购入），观战可见。</summary>
        public int[] RelicIds { get; set; } = System.Array.Empty<int>();
    }

    public sealed class PvpDuelSummaryDto
    {
        public string LeftUserId { get; set; } = string.Empty;

        public string RightUserId { get; set; } = string.Empty;

        public string MonsterName { get; set; } = string.Empty;

        public bool Resolved { get; set; }
    }

    public sealed class PvpMatchEventDto
    {
        public long Version { get; set; }

        /// <summary>round_start / settle_start / shop_start / duel_resolved / player_eliminated /
        /// player_offline / player_online / match_finished。</summary>
        public string Kind { get; set; } = string.Empty;

        public int Round { get; set; }

        public string UserId { get; set; } = string.Empty;

        public int Value { get; set; }
    }

    public sealed class PvpShopStateDto
    {
        public int[] OfferIds { get; set; } = System.Array.Empty<int>();

        public int[] OfferPrices { get; set; } = System.Array.Empty<int>();

        public int RefreshCost { get; set; }

        public int FreeRefreshLeft { get; set; }

        public int[] OwnedRelicIds { get; set; } = System.Array.Empty<int>();

        public int[] OwnedSellPrices { get; set; } = System.Array.Empty<int>();

        public bool Done { get; set; }
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

        /// <summary>状态版本号：每次状态变更单调递增。客户端用它过滤乱序/重复快照。</summary>
        public long StateVersion { get; set; }

        /// <summary>自上一快照以来的增量事件；广播另行通过 match_event 下发时这里为空。</summary>
        public PvpMatchEventDto[] Events { get; set; } = System.Array.Empty<PvpMatchEventDto>();

        /// <summary>商店阶段本人视角的商店状态；非商店阶段或非本人为 null。</summary>
        public PvpShopStateDto? Shop { get; set; }
    }
}
