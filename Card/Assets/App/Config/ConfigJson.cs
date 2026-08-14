using System;
using UnityEngine;

namespace App.Config
{
    /// <summary>
    /// JsonUtility 辅助：顶层数组需包一层才能反序列化。
    /// </summary>
    public static class ConfigJson
    {
        [Serializable]
        private sealed class ArrayWrapper<T>
        {
            public T[] Items;
        }

        public static T FromObjectJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON text is empty.", nameof(json));
            }

            var result = JsonUtility.FromJson<T>(json);
            if (result == null)
            {
                throw new InvalidOperationException($"Failed to parse JSON as {typeof(T).Name}.");
            }

            return result;
        }

        public static T[] FromArrayJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON text is empty.", nameof(json));
            }

            var wrapped = "{\"Items\":" + json.Trim() + "}";
            var result = JsonUtility.FromJson<ArrayWrapper<T>>(wrapped);
            if (result?.Items == null)
            {
                throw new InvalidOperationException($"Failed to parse JSON array as {typeof(T).Name}[].");
            }

            return result.Items;
        }
    }
}
