namespace Framework.Save
{
    /// <summary>
    /// Runtime entry for the save system. Selects the platform ISaveService implementation.
    /// </summary>
    public static class SaveFramework
    {
        public static ISaveService Create()
        {
#if (WEIXINMINIGAME || PLATFORM_WEIXINMINIGAME) && !UNITY_EDITOR
            return new WeChatSaveService();
#else
            return new PlayerPrefsSaveService();
#endif
        }
    }
}
