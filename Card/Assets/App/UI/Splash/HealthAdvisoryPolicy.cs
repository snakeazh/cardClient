namespace App.UI.Splash
{
    /// <summary>
    /// 决定是否展示健康游戏忠告。接入微信 SDK 后可改为按平台/宏开关。
    /// </summary>
    public static class HealthAdvisoryPolicy
    {
        public static bool ShouldShowOnLaunch()
        {
            // 暂始终展示，便于 Editor 与各平台联调；后续接入 SDK 时再引入 WECHAT_MINIGAME 等条件。
            return true;
        }
    }
}
