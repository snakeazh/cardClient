using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using CardShare.Contracts.Config;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 羁绊彩色图标（Textures/Battle）。启动时按羁绊表预热，商店卡直接取缓存。
    /// 持有引用直到进程结束，避免和 HUD 的 Release 互相拆掉同一张图。
    /// </summary>
    public static class BondIconLoader
    {
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Task> Pending = new Dictionary<string, Task>();
        private static readonly HashSet<string> Failed = new HashSet<string>();

        private static IResourceService _resources;

        public static void Bind(IResourceService resources)
        {
            _resources = resources;
        }

        public static async Task PreloadAsync(IResourceService resources)
        {
            Bind(resources);
            if (resources == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (var pair in BindingConfig.All)
            {
                var binding = pair.Value;
                if (binding == null)
                {
                    continue;
                }

                var stem = ResResourcePaths.BondArtName(binding.Type);
                if (string.IsNullOrEmpty(stem) || !seen.Add(stem))
                {
                    continue;
                }

                await EnsureAsync(stem);
            }
        }

        public static bool TryGet(string stem, out Sprite sprite)
        {
            sprite = null;
            var key = ResResourcePaths.BattleIcon(stem);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            return Sprites.TryGetValue(key, out sprite) && sprite != null;
        }

        public static Task EnsureAsync(string stem)
        {
            var key = ResResourcePaths.BattleIcon(stem);
            if (string.IsNullOrEmpty(key) || Sprites.ContainsKey(key) || Failed.Contains(key))
            {
                return Task.CompletedTask;
            }

            if (Pending.TryGetValue(key, out var running))
            {
                return running;
            }

            var task = Load(key);
            Pending[key] = task;
            return task;
        }

        private static async Task Load(string key)
        {
            Sprite sprite = null;
            try
            {
                if (_resources != null)
                {
                    sprite = await _resources.LoadAsync<Sprite>(key);
                }
            }
            catch (System.Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "Bond icon load failed " + key + ": " + ex.Message);
            }

            Pending.Remove(key);
            if (sprite != null)
            {
                Sprites[key] = sprite;
            }
            else
            {
                Failed.Add(key);
            }
        }
    }
}
