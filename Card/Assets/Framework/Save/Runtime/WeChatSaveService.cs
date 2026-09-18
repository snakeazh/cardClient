#if (WEIXINMINIGAME || PLATFORM_WEIXINMINIGAME) && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace Framework.Save
{
    /// <summary>
    /// WeChat mini-game ISaveService backed by the WX-WASM-SDK storage adapter
    /// (the SDK's PlayerPrefs-to-wx.storage bridge). Reads are synchronous via a
    /// memory cache over wx.getStorageSync; writes update the cache and flush to
    /// wx.setStorage on an async queue, so Save() is a no-op.
    /// Only compiled into WeChat mini-game player builds; editor and other
    /// platforms use PlayerPrefsSaveService instead.
    /// </summary>
    public sealed class WeChatSaveService : ISaveService
    {
        [DllImport("__Internal")]
        private static extern int WXStorageHasKeySync(string key);

        [DllImport("__Internal")]
        private static extern string WXStorageGetStringSync(string key, string defaultValue);

        [DllImport("__Internal")]
        private static extern void WXStorageSetStringSync(string key, string value);

        [DllImport("__Internal")]
        private static extern int WXStorageGetIntSync(string key, int defaultValue);

        [DllImport("__Internal")]
        private static extern void WXStorageSetIntSync(string key, int value);

        [DllImport("__Internal")]
        private static extern float WXStorageGetFloatSync(string key, float defaultValue);

        [DllImport("__Internal")]
        private static extern void WXStorageSetFloatSync(string key, float value);

        [DllImport("__Internal")]
        private static extern void WXStorageDeleteKeySync(string key);

        [DllImport("__Internal")]
        private static extern void WXStorageDeleteAllSync();

        public bool HasKey(string key)
        {
            return WXStorageHasKeySync(key) != 0;
        }

        public string GetString(string key, string defaultValue = "")
        {
            return WXStorageGetStringSync(key, defaultValue);
        }

        public void SetString(string key, string value)
        {
            WXStorageSetStringSync(key, value);
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            return WXStorageGetIntSync(key, defaultValue);
        }

        public void SetInt(string key, int value)
        {
            WXStorageSetIntSync(key, value);
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            return WXStorageGetFloatSync(key, defaultValue);
        }

        public void SetFloat(string key, float value)
        {
            WXStorageSetFloatSync(key, value);
        }

        public void DeleteKey(string key)
        {
            WXStorageDeleteKeySync(key);
        }

        public void DeleteAll()
        {
            WXStorageDeleteAllSync();
        }

        public void Save()
        {
            // The adapter flushes writes to wx storage asynchronously; nothing to force here.
        }
    }
}
#endif
