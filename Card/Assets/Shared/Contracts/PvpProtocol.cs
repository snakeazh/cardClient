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
        public const string ShopDone = "shop_done";

        /// <summary>编辑器测试：向本人座位添加圣物（index = relicId）。生产环境不应使用。</summary>
        public const string DebugGrant = "debug_grant";
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
