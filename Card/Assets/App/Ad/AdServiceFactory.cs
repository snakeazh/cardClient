namespace App.Ad
{
    /// <summary>
    /// 按平台宏创建广告实现。抖音与微信宏出包时应互斥；编辑器使用模拟广告面板。
    /// </summary>
    public static class AdServiceFactory
    {
        public static IAdService Create()
        {
#if UNITY_EDITOR
            return new EditorSimulatedAdService();
#elif DOUYINMINIGAME
            return new DouyinAdService();
#elif WEIXINMINIGAME || PLATFORM_WEIXINMINIGAME
            // TODO: 微信平台待接入，接入微信激励视频后替换为 WeChatAdService。
            return new SimulatedAdService();
#else
            return new SimulatedAdService();
#endif
        }
    }
}
