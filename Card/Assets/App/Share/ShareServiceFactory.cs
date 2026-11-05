namespace App.Share
{
    /// <summary>
    /// 按平台宏创建分享实现。抖音与微信宏出包时应互斥；编辑器使用模拟分享面板。
    /// 注意：抖音实现依赖 TTSDK（`DOUYINMINIGAME` 宏），微信实现依赖 WX-WASM-SDK（`WEIXINMINIGAME` 宏）。
    /// </summary>
    public static class ShareServiceFactory
    {
        public static IShareService Create()
        {
#if UNITY_EDITOR
            return new EditorSimulatedShareService();
#elif DOUYINMINIGAME
            return new DouyinShareService();
#elif WEIXINMINIGAME || PLATFORM_WEIXINMINIGAME
            return new WeChatShareService();
#else
            return new SimulatedShareService();
#endif
        }
    }
}
