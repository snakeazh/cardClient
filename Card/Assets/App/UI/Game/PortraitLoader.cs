using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;
using App.Resources;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 角色 / 怪物头像。英雄仍按张预热与局内补载；怪物立绘走 <see cref="ResResourcePaths.EnemyAtlas"/>，
    /// 启动图集预载后即可取 _attack / _damage / _dead，不再按张 Load。
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
        private static IAtlasService _atlas;

        /// <summary>本局补载的英雄 _damage/_dead key，退局时由 ReleaseBattleStates 释放。</summary>
        private static readonly HashSet<string> BattleStateKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void Bind(IAtlasService atlas)
        {
            _atlas = atlas;
        }

        /// <summary>配置表加载完成后预热全部英雄 _attack；怪物由 enemy 图集在 AtlasService.PreloadAsync 时已就绪。</summary>
        public static async Task PreloadAsync(IResourceService resources)
        {
            _resources = resources;
            if (resources == null)
            {
                return;
            }

            var roleIcons = UniqueIcons(HeroConfig.All, row => row.Icon);
            for (var i = 0; i < roleIcons.Count; i++)
            {
                await LoadOne(resources, PortraitPath(false, roleIcons[i], ResResourcePaths.PortraitAttack));
            }
        }

        /// <summary>
        /// 局内补齐上场玩家的 _damage / _dead。怪物已在 enemy 图集，无需补载。
        /// 返回是否新加载了资源，调用方据此决定要不要再刷一次头像。
        /// </summary>
        public static async Task<bool> EnsureBattleStatesAsync(SeatState player, SeatState[] enemies)
        {
            if (_resources == null)
            {
                return false;
            }

            var pending = new List<(bool enemy, string icon)>(1);
            TryQueueHurt(pending, false, player != null ? player.Icon : null);
            // enemies：Altas/enemy 已含 _damage/_dead，不排队按张加载。

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

        /// <summary>退局时释放本局补载的英雄 _damage/_dead 立绘（_attack 常驻；怪物图集不释放）。重复调用安全。</summary>
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
            return PickRole(icon, ResResourcePaths.PortraitSuffix(hp, maxHp));
        }

        public static Sprite GetRole(string icon, string suffix = null)
        {
            return PickRole(icon, string.IsNullOrEmpty(suffix) ? ResResourcePaths.PortraitAttack : suffix);
        }

        public static Sprite GetEnemy(string icon, int hp, int maxHp)
        {
            return PickEnemy(icon, ResResourcePaths.PortraitSuffix(hp, maxHp));
        }

        public static Sprite GetEnemy(string icon, string suffix = null)
        {
            return PickEnemy(icon, string.IsNullOrEmpty(suffix) ? ResResourcePaths.PortraitAttack : suffix);
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
            if (enemy)
            {
                return HasEnemySprite(icon, ResResourcePaths.PortraitDamage) &&
                       HasEnemySprite(icon, ResResourcePaths.PortraitDead);
            }

            return Sprites.ContainsKey(PortraitPath(false, icon, ResResourcePaths.PortraitDamage)) &&
                   Sprites.ContainsKey(PortraitPath(false, icon, ResResourcePaths.PortraitDead));
        }

        private static Sprite PickRole(string icon, string suffix)
        {
            var sprite = GetCached(PortraitPath(false, icon, suffix));
            if (sprite != null || string.Equals(suffix, ResResourcePaths.PortraitAttack, StringComparison.Ordinal))
            {
                return sprite;
            }

            return GetCached(PortraitPath(false, icon, ResResourcePaths.PortraitAttack));
        }

        private static Sprite PickEnemy(string icon, string suffix)
        {
            var sprite = GetEnemyAtlasSprite(icon, suffix);
            if (sprite != null || string.Equals(suffix, ResResourcePaths.PortraitAttack, StringComparison.Ordinal))
            {
                return sprite;
            }

            return GetEnemyAtlasSprite(icon, ResResourcePaths.PortraitAttack);
        }

        private static bool HasEnemySprite(string icon, string suffix)
        {
            return GetEnemyAtlasSprite(icon, suffix) != null;
        }

        private static Sprite GetEnemyAtlasSprite(string icon, string suffix)
        {
            var spriteName = ResResourcePaths.EnemySpriteName(icon, suffix);
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            if (_atlas != null &&
                _atlas.TryGetSprite(ResResourcePaths.EnemyAtlas, spriteName, out var sprite) &&
                sprite != null)
            {
                return sprite;
            }

            return GetCached(PortraitPath(true, icon, suffix));
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
            if (enemy)
            {
                return;
            }

            for (var i = 0; i < HurtSuffixes.Length; i++)
            {
                var key = PortraitPath(false, icon, HurtSuffixes[i]);
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
