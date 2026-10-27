#nullable enable

namespace CardShare.Contracts
{
    /// <summary>PvP battle 消息的 action 字段取值（战斗 + 商店操作）。双端协议常量，只增不改。</summary>
    public static class PvpActions
    {
        public const string Pick = "pick";
        public const string Rub = "rub";
        public const string Replace = "replace";
        public const string Peek = "peek";
        public const string Showdown = "showdown";
        public const string Open = "open";
        public const string Buy = "buy";
        public const string Sell = "sell";
        public const string Refresh = "refresh";

        /// <summary>商店阶段花金币解锁下一个圣物槽（第 5~10 槽）。</summary>
        public const string UnlockSlot = "unlock_slot";

        /// <summary>PVP 商店专属：花金币补充技能次数（index = 技能 Id，见 <see cref="PvpSkillIds"/>）。</summary>
        public const string BuySkill = "buy_skill";

        public const string ShopDone = "shop_done";

        /// <summary>使用消耗型圣物（index = relicId），仅 fight 阶段（服务端 Act 校验）。</summary>
        public const string Use = "use";

        /// <summary>编辑器测试：向本人座位添加圣物（index = relicId）。生产环境不应使用。</summary>
        public const string DebugGrant = "debug_grant";
    }

    /// <summary>
    /// 技能 Id：<see cref="PvpActions.BuySkill"/> 的 index 与 <see cref="PvpShopSkillDto.Id"/> 共用。
    /// 与 <see cref="PvpFighterDto"/> 的字段对应：Rub=搓牌(RubLeft)、Replace=替换(ReplaceLeft)、Peek=透视(PeekLeft)。
    /// </summary>
    public static class PvpSkillIds
    {
        public const int Rub = 0;

        public const int Replace = 1;

        public const int Peek = 2;

        public const int Count = 3;
    }

    /// <summary>PvP match_event 的 kind 取值。双端协议常量，只增不改。</summary>
    public static class PvpEventKinds
    {
        public const string RoundStart = "round_start";
        public const string SettleStart = "settle_start";
        public const string ShopStart = "shop_start";
        public const string DuelResolved = "duel_resolved";
        public const string PlayerEliminated = "player_eliminated";
        public const string PlayerOffline = "player_offline";
        public const string PlayerOnline = "player_online";
        public const string MatchFinished = "match_finished";

        /// <summary>开局英雄特效已结算（圣光/自伤等），Value=本桌节拍数；客户端跟快照 HeroFx 播演出。</summary>
        public const string HeroFx = "hero_fx";
    }

    /// <summary>PvP match_update 的 phase 取值。PvpMatch.PhaseXxx 常量引用此处，保持单一来源。</summary>
    public static class PvpPhases
    {
        public const string Fight = "fight";
        public const string Settle = "settle";
        public const string Shop = "shop";
        public const string Finished = "finished";
    }
}
