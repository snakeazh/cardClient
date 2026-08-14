using System;

namespace App.Config
{
    /// <summary>
    /// 常量表基类：全表一个实例，子类通过 Instance 访问。
    /// </summary>
    [Serializable]
    public abstract class ConfigConstBase<T> where T : ConfigConstBase<T>
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException(
                        $"{typeof(T).Name} is not loaded. Call ConfigTables.LoadAsync first.");
                }

                return _instance;
            }
        }

        public static bool IsLoaded => _instance != null;

        internal static void Load(T data)
        {
            _instance = data ?? throw new ArgumentNullException(nameof(data));
        }
    }
}
