namespace Framework.Save
{
    /// <summary>
    /// Platform-agnostic key-value save API. Complex objects should be serialized by the caller.
    /// </summary>
    public interface ISaveService
    {
        bool HasKey(string key);

        string GetString(string key, string defaultValue = "");

        void SetString(string key, string value);

        int GetInt(string key, int defaultValue = 0);

        void SetInt(string key, int value);

        float GetFloat(string key, float defaultValue = 0f);

        void SetFloat(string key, float value);

        void DeleteKey(string key);

        void DeleteAll();

        void Save();
    }
}
