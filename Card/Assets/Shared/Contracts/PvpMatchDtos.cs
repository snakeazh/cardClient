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

        /// <summary>对局结束后的名次 PVP 分数加减；未结束为 0。可为负。</summary>
        public int RewardScore { get; set; }

        /// <summary>当前 PvP 连胜。野怪轮也计入。</summary>
        public int WinStreak { get; set; }

        /// <summary>当前 PvP 连败。野怪轮也计入。</summary>
        public int LoseStreak { get; set; }

        /// <summary>当前持有的圣物（商店购入），观战可见。</summary>
        public int[] RelicIds { get; set; } = System.Array.Empty<int>();

        /// <summary>与 RelicIds 对齐。每份为 1。</summary>
        public int[] RelicPower { get; set; } = System.Array.Empty<int>();

        /// <summary>当前已解锁槽位数。</summary>
        public int RelicSlots { get; set; }

        /// <summary>凤凰羽毛等已激活：本次比牌受到的伤害将变为 0，结算后清除。</summary>
        public bool NullifyDamage { get; set; }
    }

    public sealed class PvpDuelSummaryDto
    {
        public string LeftUserId { get; set; } = string.Empty;

        public string RightUserId { get; set; } = string.Empty;

        public string MonsterName { get; set; } = string.Empty;

        public bool Resolved { get; set; }
    }

    /// <summary>PVP 英雄特效节拍。Kind / Phase 取值见 <see cref="PvpHeroFxKinds"/> / <see cref="PvpHeroFxPhases"/>。</summary>
    public sealed class PvpHeroFxDto
    {
        public string Kind { get; set; } = string.Empty;

        public string Phase { get; set; } = string.Empty;

        public string CasterUserId { get; set; } = string.Empty;

        /// <summary>受伤方 UserId；打野怪时为空。</summary>
        public string TargetUserId { get; set; } = string.Empty;

        public int CasterHeroId { get; set; }

        public int Damage { get; set; }
    }

    /// <summary>PvpHeroFxDto.Kind 取值。</summary>
    public static class PvpHeroFxKinds
    {
        public const string HolyLight = "holy_light";
        public const string ExtraAttack = "extra_attack";
        public const string MonsterChip = "monster_chip";
    }

    /// <summary>PvpHeroFxDto.Phase 取值。</summary>
    public static class PvpHeroFxPhases
    {
        public const string Opening = "opening";
        public const string Pair = "pair";
        public const string Chase = "chase";
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

    /// <summary>PVP 商店：某个技能的购买状态（花金币补充次数）。</summary>
    public sealed class PvpShopSkillDto
    {
        /// <summary>技能 Id，见 <see cref="PvpSkillIds"/>。</summary>
        public int Id { get; set; }

        /// <summary>下一次购买的金币价（本商店已买次数越多越贵）；0 = 不可购买。</summary>
        public int Price { get; set; }

        /// <summary>当前剩余次数。商店阶段拿到的是上一轮打完剩下的量。</summary>
        public int Left { get; set; }

        /// <summary>下一轮开始时的次数上限（基础 + 圣物/英雄/天赋 + 本商店已购）。购买后立即 +1。</summary>
        public int Max { get; set; }
    }

    public sealed class PvpShopStateDto
    {
        public int[] OfferIds { get; set; } = System.Array.Empty<int>();

        public int[] OfferPrices { get; set; } = System.Array.Empty<int>();

        public int RefreshCost { get; set; }

        public int FreeRefreshLeft { get; set; }

        public int[] OwnedRelicIds { get; set; } = System.Array.Empty<int>();

        /// <summary>与 OwnedRelicIds 对齐。每份为 1。</summary>
        public int[] OwnedRelicPower { get; set; } = System.Array.Empty<int>();

        public int[] OwnedSellPrices { get; set; } = System.Array.Empty<int>();

        public int RelicSlots { get; set; }

        /// <summary>再解锁一槽的金币。已满为 0。</summary>
        public int NextSlotCost { get; set; }

        /// <summary>三个技能的补充次数入口（下标即 <see cref="PvpSkillIds"/>）。</summary>
        public PvpShopSkillDto[] Skills { get; set; } = System.Array.Empty<PvpShopSkillDto>();

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

        /// <summary>自己这桌结算后的实际总伤害（主伤+追击+对子圣光）。0 = 未结算或平局。客户端只做状态同步，不要重算。</summary>
        public int DuelDamage { get; set; }

        /// <summary>比牌主伤（不含追击/对子圣光）。</summary>
        public int DuelMainDamage { get; set; }

        /// <summary>英雄 ExtraAttackOneTime 第二刀伤害；0 = 未追击。</summary>
        public int DuelChaseDamage { get; set; }

        /// <summary>对子获胜圣光追加伤害；0 = 无。</summary>
        public int DuelHolyBurnDamage { get; set; }

        /// <summary>
        /// 本桌待播的英雄特效节拍（开局圣光/自伤等）。客户端按 Phase 排队演出；
        /// 权威 HP 已是结算后，开播前需先把对应伤害加回显示血再演。
        /// </summary>
        public PvpHeroFxDto[] HeroFx { get; set; } = System.Array.Empty<PvpHeroFxDto>();

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
