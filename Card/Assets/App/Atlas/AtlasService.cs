using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using Framework.Assets;
using UnityEngine;
using UnityEngine.U2D;

namespace App.Atlas
{
    /// <summary>
    /// Loads SpriteAtlas assets via IResourceService at boot and caches GetSprite clones.
    /// </summary>
    public sealed class AtlasService : IAtlasService
    {
        public static readonly IReadOnlyList<string> StartupAtlasKeys = new[]
        {
            ResResourcePaths.CardAtlas
        };

        private const string CloneSuffix = "(Clone)";

        private readonly IResourceService _resources;
        private readonly Dictionary<string, SpriteAtlas> _atlases =
            new Dictionary<string, SpriteAtlas>(StringComparer.Ordinal);
        private readonly Dictionary<string, SpriteAtlas> _atlasesByName =
            new Dictionary<string, SpriteAtlas>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> _sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private static AtlasService _current;
        private static bool _listenerHooked;

        public AtlasService(IResourceService resources)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public bool IsLoaded => _atlases.Count > 0;

        public async Task PreloadAsync()
        {
            EnsureAtlasRequestedListener();

            for (var i = 0; i < StartupAtlasKeys.Count; i++)
            {
                await LoadAtlasAsync(StartupAtlasKeys[i]);
            }
        }

        public SpriteAtlas GetAtlas(string atlasKey)
        {
            if (string.IsNullOrWhiteSpace(atlasKey))
            {
                throw new ArgumentException("Atlas key cannot be empty.", nameof(atlasKey));
            }

            if (_atlases.TryGetValue(atlasKey, out var atlas) && atlas != null)
            {
                return atlas;
            }

            throw new InvalidOperationException(
                $"Atlas '{atlasKey}' is not loaded. Call PreloadAsync at startup, or add it to AtlasService.StartupAtlasKeys.");
        }

        public Sprite GetSprite(string atlasKey, string spriteName)
        {
            if (TryGetSprite(atlasKey, spriteName, out var sprite) && sprite != null)
            {
                return sprite;
            }

            Debug.LogWarning($"[Atlas] Sprite '{spriteName}' not found in '{atlasKey}'.");
            return null;
        }

        public bool TryGetSprite(string atlasKey, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrWhiteSpace(atlasKey) || string.IsNullOrWhiteSpace(spriteName))
            {
                return false;
            }

            var cacheKey = SpriteCacheKey(atlasKey, spriteName);
            if (_sprites.TryGetValue(cacheKey, out sprite) && sprite != null)
            {
                return true;
            }

            if (!_atlases.TryGetValue(atlasKey, out var atlas) || atlas == null)
            {
                return false;
            }

            sprite = atlas.GetSprite(spriteName);
            if (sprite == null)
            {
                return false;
            }

            _sprites[cacheKey] = sprite;
            return true;
        }

        private async Task LoadAtlasAsync(string atlasKey)
        {
            if (_atlases.ContainsKey(atlasKey))
            {
                return;
            }

            var atlas = await _resources.LoadAsync<SpriteAtlas>(atlasKey);
            if (atlas == null)
            {
                throw new InvalidOperationException($"Failed to load SpriteAtlas '{atlasKey}'.");
            }

            _atlases[atlasKey] = atlas;
            if (!string.IsNullOrEmpty(atlas.name))
            {
                _atlasesByName[atlas.name] = atlas;
            }

            WarmSprites(atlasKey, atlas);
            Debug.Log($"[Atlas] loaded '{atlasKey}' ({atlas.spriteCount} sprites).");
        }

        private void WarmSprites(string atlasKey, SpriteAtlas atlas)
        {
            var count = atlas.spriteCount;
            if (count <= 0)
            {
                return;
            }

            var packed = new Sprite[count];
            atlas.GetSprites(packed);
            for (var i = 0; i < packed.Length; i++)
            {
                var clone = packed[i];
                if (clone == null)
                {
                    continue;
                }

                var name = StripCloneSuffix(clone.name);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                _sprites[SpriteCacheKey(atlasKey, name)] = clone;
            }
        }

        private void EnsureAtlasRequestedListener()
        {
            _current = this;
            if (_listenerHooked)
            {
                return;
            }

            SpriteAtlasManager.atlasRequested += OnAtlasRequestedStatic;
            _listenerHooked = true;
        }

        private static void OnAtlasRequestedStatic(string atlasName, Action<SpriteAtlas> callback)
        {
            _current?.OnAtlasRequested(atlasName, callback);
        }

        private void OnAtlasRequested(string atlasName, Action<SpriteAtlas> callback)
        {
            if (string.IsNullOrEmpty(atlasName) || callback == null)
            {
                return;
            }

            if (_atlasesByName.TryGetValue(atlasName, out var byName) && byName != null)
            {
                callback(byName);
                return;
            }

            foreach (var pair in _atlases)
            {
                if (pair.Value != null && pair.Value.name == atlasName)
                {
                    callback(pair.Value);
                    return;
                }
            }
        }

        private static string SpriteCacheKey(string atlasKey, string spriteName) => atlasKey + "/" + spriteName;

        private static string StripCloneSuffix(string name)
        {
            if (!string.IsNullOrEmpty(name) && name.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - CloneSuffix.Length);
            }

            return name;
        }
    }
}
