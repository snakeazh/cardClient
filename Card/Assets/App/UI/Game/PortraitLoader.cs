using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Resources;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 角色 / 怪物头像加载器。启动只预热 _attack；局内对上场玩家和怪物再补 _damage / _dead。
    /// </summary>
    public static class PortraitLoader
    {
        private static readonly string[] HurtSuffixes =
        {
            ResResourcePaths.PortraitDamage,
            ResResourcePaths.PortraitDead
        };

        private static readonly Dictionary<string, Sprite> Sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private static IResourceService _resources;

        /// <summary>本局补载的 _damage/_dead key，退局时由 ReleaseBattleStates 释放。</summary>
        private static readonly HashSet<string> BattleStateKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>配置表加载完成后预热全部英雄和怪物的 _attack。</summary>
        public static async Task PreloadAsync(IResourceService resources)
        {
            _resources = resources;
            if (resources == null)
            {
                return;
            }

            var roleIcons = UniqueIcons(HeroConfig.All, row => row.Icon);
            var enemyIcons = UniqueIcons(MonsterConfig.All, row => row.Icon);
            for (var i = 0; i < roleIcons.Count; i++)
            {
                await LoadOne(resources, PortraitPath(false, roleIcons[i], ResResourcePaths.PortraitAttack));
            }

            for (var i = 0; i < enemyIcons.Count; i++)
            {
                await LoadOne(resources, PortraitPath(true, enemyIcons[i], ResResourcePaths.PortraitAttack));
            }
        }

        /// <summary>
        /// 局内补齐上场玩家和怪物的 _damage / _dead。已加载过的会跳过。
        /// 返回是否新加载了资源，调用方据此决定要不要再刷一次头像。
        /// </summary>
        public static async Task<bool> EnsureBattleStatesAsync(SeatState player, SeatState[] enemies)
        {
            if (_resources == null)
            {
                return false;
            }

            var pending = new List<(bool enemy, string icon)>(4);
            TryQueueHurt(pending, false, player != null ? player.Icon : null);
            if (enemies != null)
            {
                for (var i = 0; i < enemies.Length; i++)
                {
                    var enemy = enemies[i];
                    if (enemy == null || !enemy.ActiveInStage)
                    {
                        continue;
                    }

                    TryQueueHurt(pending, true, enemy.Icon);
                }
            }

            if (pending.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < pending.Count; i++)
            {
                await LoadHurtSet(_resources, pending[i].enemy, pending[i].icon);
            }

            return true;
        }

        /// <summary>退局时释放本局补载的 _damage/_dead 立绘（_attack 常驻）。重复调用安全。</summary>
        public static void ReleaseBattleStates()
        {
            if (BattleStateKeys.Count == 0)
            {
                return;
            }

            foreach (var key in BattleStateKeys)
            {
                Sprites.Remove(key);
                _resources?.Release(key);
            }

            BattleStateKeys.Clear();
        }

        public static Sprite Get(SeatState seat)
        {
            if (seat == null)
            {
                return null;
            }

            return seat.IsPlayer
                ? GetRole(seat.Icon, seat.Hp, seat.MaxHp)
                : GetEnemy(seat.Icon, seat.Hp, seat.MaxHp);
        }

        public static Sprite GetRole(string icon, int hp, int maxHp)
        {
            return Pick(false, icon, ResResourcePaths.PortraitSuffix(hp, maxHp));
        }

        public static Sprite GetRole(string icon, string suffix = null)
        {
            return Pick(false, icon, string.IsNullOrEmpty(suffix) ? ResResourcePaths.PortraitAttack : suffix);
        }

        public static Sprite GetEnemy(string icon, int hp, int maxHp)
        {
            return Pick(true, icon, ResResourcePaths.PortraitSuffix(hp, maxHp));
        }

        public static Sprite GetEnemy(string icon, string suffix = null)
        {
            return Pick(true, icon, string.IsNullOrEmpty(suffix) ? ResResourcePaths.PortraitAttack : suffix);
        }

        private static void TryQueueHurt(List<(bool enemy, string icon)> pending, bool enemy, string icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return;
            }

            icon = icon.Trim();
            if (HasHurtStates(enemy, icon))
            {
                return;
            }

            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i].enemy == enemy &&
                    string.Equals(pending[i].icon, icon, StringComparison.Ordinal))
                {
                    return;
                }
            }

            pending.Add((enemy, icon));
        }

        private static bool HasHurtStates(bool enemy, string icon)
        {
            return Sprites.ContainsKey(PortraitPath(enemy, icon, ResResourcePaths.PortraitDamage)) &&
                   Sprites.ContainsKey(PortraitPath(enemy, icon, ResResourcePaths.PortraitDead));
        }

        private static Sprite Pick(bool enemy, string icon, string suffix)
        {
            var sprite = GetCached(PortraitPath(enemy, icon, suffix));
            if (sprite != null || string.Equals(suffix, ResResourcePaths.PortraitAttack, StringComparison.Ordinal))
            {
                return sprite;
            }

            return GetCached(PortraitPath(enemy, icon, ResResourcePaths.PortraitAttack));
        }

        private static Sprite GetCached(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return Sprites.TryGetValue(key, out var sprite) ? sprite : null;
        }

        private static async Task LoadHurtSet(IResourceService resources, bool enemy, string icon)
        {
            for (var i = 0; i < HurtSuffixes.Length; i++)
            {
                var key = PortraitPath(enemy, icon, HurtSuffixes[i]);
                await LoadOne(resources, key);
                BattleStateKeys.Add(key);
            }
        }

        private static async Task LoadOne(IResourceService resources, string key)
        {
            if (string.IsNullOrEmpty(key) || Sprites.ContainsKey(key))
            {
                return;
            }

            try
            {
                Sprites[key] = await resources.LoadAsync<Sprite>(key);
            }
            catch (Exception ex)
            {
                Sprites[key] = null;
                AppLog.Warn(LogChannel.UI, $"Failed to load portrait '{key}': {ex.Message}");
            }
        }

        private static string PortraitPath(bool enemy, string icon, string suffix)
        {
            return enemy
                ? ResResourcePaths.EnemyPortrait(icon, suffix)
                : ResResourcePaths.RolePortrait(icon, suffix);
        }

        private static List<string> UniqueIcons<T>(IReadOnlyDictionary<int, T> rows, Func<T, string> iconOf)
        {
            var list = new List<string>();
            if (rows == null || iconOf == null)
            {
                return list;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in rows)
            {
                var icon = iconOf(pair.Value);
                if (string.IsNullOrWhiteSpace(icon))
                {
                    continue;
                }

                icon = icon.Trim();
                if (seen.Add(icon))
                {
                    list.Add(icon);
                }
            }

            return list;
        }
    }
}
