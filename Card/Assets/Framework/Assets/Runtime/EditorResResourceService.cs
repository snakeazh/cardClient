#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Framework.Assets
{
    /// <summary>
    /// Editor play mode: resolves keys like "UI/Home" to Assets/Res/UI/Home.prefab via AssetDatabase.
    /// No AssetBundle build required while iterating on UI.
    /// </summary>
    public sealed class EditorResResourceService : IResourceService
    {
        private static readonly string[] TryExtensions = { ".prefab", ".asset", ".mat", ".sprite", ".png", ".jpg", ".wav", ".mp3" };

        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private bool _initialized;

        public bool IsInitialized => _initialized;

        public string BundleVersion { get; private set; }

        public string BundleRoot => ResPaths.AssetRoot;

        public Task InitializeAsync()
        {
            lock (_gate)
            {
                if (_initialized)
                {
                    return Task.CompletedTask;
                }

                BundleVersion = ReadBundleVersionHint();
                _initialized = true;
            }

            return Task.CompletedTask;
        }

        public Task<T> LoadAsync<T>(string key) where T : Object
        {
            return Task.FromResult(LoadInternal<T>(key));
        }

        public Task<ResourceHandle<T>> LoadHandleAsync<T>(string key) where T : Object
        {
            var asset = LoadInternal<T>(key);
            return Task.FromResult(new ResourceHandle<T>(this, key, asset));
        }

        public bool TryGetCached<T>(string key, out T asset) where T : Object
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                asset = null;
                return false;
            }

            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.Asset is T typed)
                {
                    asset = typed;
                    return true;
                }
            }

            asset = null;
            return false;
        }

        public void Release(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (_gate)
            {
                if (!_cache.TryGetValue(key, out var entry))
                {
                    return;
                }

                entry.RefCount--;
                if (entry.RefCount > 0)
                {
                    return;
                }

                _cache.Remove(key);
            }
        }

        public void ReleaseAll()
        {
            lock (_gate)
            {
                _cache.Clear();
                _initialized = false;
                BundleVersion = null;
            }
        }

        private T LoadInternal<T>(string key) where T : Object
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Resource key cannot be empty.", nameof(key));
            }

            lock (_gate)
            {
                EnsureInitialized();

                if (_cache.TryGetValue(key, out var existing))
                {
                    if (!(existing.Asset is T typedExisting))
                    {
                        throw new InvalidOperationException(
                            $"Cached asset '{key}' is {existing.Asset.GetType().Name}, requested {typeof(T).Name}.");
                    }

                    existing.RefCount++;
                    return typedExisting;
                }

                var path = ResolveAssetPath<T>(key);
                var loaded = AssetDatabase.LoadAssetAtPath<T>(path);
                if (loaded == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load '{path}' as {typeof(T).Name} (key '{key}').");
                }

                _cache[key] = new CacheEntry(loaded, 1);
                return loaded;
            }
        }

        private static string ResolveAssetPath<T>(string key) where T : Object
        {
            var basePath = ResPaths.KeyToAssetBasePath(key);
            foreach (var ext in TryExtensions)
            {
                var path = basePath + ext;
                if (AssetDatabase.LoadAssetAtPath<T>(path) != null)
                {
                    return path;
                }
            }

            foreach (var ext in TryExtensions)
            {
                var path = basePath + ext;
                if (File.Exists(path))
                {
                    return path;
                }
            }

            throw new FileNotFoundException(
                $"No asset for key '{key}' under {ResPaths.AssetRoot}. Expected e.g. {basePath}.prefab");
        }

        private static string ReadBundleVersionHint()
        {
            var streamingVersion = Path.Combine(Application.streamingAssetsPath, "Bundles", "version.txt");
            if (File.Exists(streamingVersion))
            {
                return File.ReadAllText(streamingVersion).Trim();
            }

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var savedVersion = Path.Combine(projectRoot, "Bundles", "version.txt");
            if (File.Exists(savedVersion))
            {
                return File.ReadAllText(savedVersion).Trim();
            }

            return "editor";
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Resource system is not initialized. Call ResourceFramework.InitializeAsync() first.");
            }
        }

        private sealed class CacheEntry
        {
            public CacheEntry(Object asset, int refCount)
            {
                Asset = asset;
                RefCount = refCount;
            }

            public Object Asset { get; }
            public int RefCount { get; set; }
        }
    }
}
#endif
