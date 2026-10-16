#nullable enable

namespace CardShare.Battle
{
    /// <summary>PvP 演出时间窗：服务端阶段 deadline 与客户端演出共用同一份常量，客户端演出必须在该时长内播完。</summary>
    public static class PvpTiming
    {
        /// <summary>发牌演出：fight 阶段倒计时的前置缓冲。</summary>
        public const int DealAnimMs = 2000;

        /// <summary>比牌演出：settle 阶段总时长（翻牌+攻击+扣血）。</summary>
        public const int SettleAnimMs = 6000;

        /// <summary>机器人定牌：发牌演出结束后再等的最短毫秒。</summary>
        public const int BotLockMinMs = 3000;

        /// <summary>机器人定牌：发牌演出结束后再等的最长毫秒（含）。</summary>
        public const int BotLockMaxMs = 5000;
    }
}
