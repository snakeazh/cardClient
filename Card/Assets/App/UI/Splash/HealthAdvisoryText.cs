namespace App.UI.Splash
{
    /// <summary>
    /// 微信小游戏健康游戏忠告文案与展示参数。
    /// </summary>
    public static class HealthAdvisoryText
    {
        public const string Title = "健康游戏忠告";

        public const string Body =
            "抵制不良游戏，拒绝盗版游戏。注意自我保护，谨防受骗上当。\n" +
            "适度游戏益脑，沉迷游戏伤身。合理安排时间，享受健康生活。";

        /// <summary>底部合规信息，可按实际上线信息替换。</summary>
        public const string WeChatFooter = "适龄提示：12+";

        /// <summary>展示时长，结束后自动进入首页。</summary>
        public const int DisplayDurationMs = 3500;
    }
}
