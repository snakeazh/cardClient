using System;

namespace Framework.Save
{
    /// <summary>
    /// WeChat mini-game ISaveService stub. Replace with wx storage when the SDK is integrated.
    /// </summary>
    public sealed class WeChatSaveService : ISaveService
    {
        public bool HasKey(string key)
        {
            throw NotImplemented();
        }

        public string GetString(string key, string defaultValue = "")
        {
            throw NotImplemented();
        }

        public void SetString(string key, string value)
        {
            throw NotImplemented();
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            throw NotImplemented();
        }

        public void SetInt(string key, int value)
        {
            throw NotImplemented();
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            throw NotImplemented();
        }

        public void SetFloat(string key, float value)
        {
            throw NotImplemented();
        }

        public void DeleteKey(string key)
        {
            throw NotImplemented();
        }

        public void DeleteAll()
        {
            throw NotImplemented();
        }

        public void Save()
        {
            throw NotImplemented();
        }

        private static NotImplementedException NotImplemented()
        {
            return new NotImplementedException("WeChatSaveService is not implemented yet.");
        }
    }
}
