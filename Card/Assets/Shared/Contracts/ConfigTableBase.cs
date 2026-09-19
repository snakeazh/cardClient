using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

#nullable enable

namespace CardShare.Contracts.Config
{
    /// <summary>
    /// 配置行公共字段（非泛型，便于 JsonUtility 反序列化）。
    /// </summary>
    [Serializable]
    public abstract class ConfigRow
    {
        public int Id;
    }

    /// <summary>
    /// 普通配置表访问基类：按 Id 索引，子类通过静态接口访问。重复 Id 是配表错误，加载时直接抛出。
    /// </summary>
    [Serializable]
    public abstract class ConfigRowBase<T> : ConfigRow where T : ConfigRowBase<T>
    {
        private static Dictionary<int, T> _map = new Dictionary<int, T>();

        public static IReadOnlyDictionary<int, T> All => _map;

        public static int Count => _map.Count;

        public static T Get(int id)
        {
            return _map.TryGetValue(id, out var row) ? row : null!;
        }

        public static bool TryGet(int id, [MaybeNullWhen(false)] out T row)
        {
            return _map.TryGetValue(id, out row!);
        }

        public static void Load(T[] rows)
        {
            var map = new Dictionary<int, T>(rows.Length);
            foreach (var row in rows)
            {
                if (map.ContainsKey(row.Id))
                {
                    throw new InvalidOperationException($"{typeof(T).Name} duplicate Id={row.Id}.");
                }

                map[row.Id] = row;
            }

            _map = map;
        }
    }

    /// <summary>
    /// 常量表基类：全表一个实例，子类通过 Instance 访问。
    /// </summary>
    [Serializable]
    public abstract class ConfigConstBase<T> where T : ConfigConstBase<T>
    {
        private static T? _instance;

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

        public static void Load(T data)
        {
            _instance = data;
        }
    }
}
