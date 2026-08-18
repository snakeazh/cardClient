using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Framework.Assets
{
    /// <summary>
    /// Default IResourceService backed by Unity Resources with shared cache + ref-count.
    /// </summary>
    public sealed class UnityResourcesService : IResourceService
    {
        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        private readonly object _gate = new object();
        private bool _initialized;

        public bool IsInitialized => _initialized;

        public string BundleVersion { get; private set; }

        public string BundleRoot => Path.Combine(Application.dataPath, "Resources");

        public Task InitializeAsync()
        {
            lock (_gate)
            {
                _initialized = true;
            }

            return Task.CompletedTask;
        }

        public Task<T> LoadAsync<T>(string key) where T : Object
        {
            return Task.FromResult(LoadInternal<T>(key));
        }

        public Task<T[]> LoadAllAsync<T>(string bundleName) where T : Object
        {
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                throw new ArgumentException("Bundle name cannot be empty.", nameof(bundleName));
            }

            lock (_gate)
            {
                EnsureInitialized();
                return Task.FromResult(Resources.LoadAll<T>(bundleName));
            }
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
                UnloadEntry(entry);
            }
        }

        public void ReleaseAll()
        {
            lock (_gate)
            {
                foreach (var pair in _cache)
                {
                    UnloadEntry(pair.Value);
                }

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

                var loaded = Resources.Load<T>(key);
                if (loaded == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load resource '{key}' as {typeof(T).Name} from Resources.");
                }

                _cache[key] = new CacheEntry(loaded, 1);
                return loaded;
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Resource system is not initialized. Call InitializeAsync() first.");
            }
        }

        private static void UnloadEntry(CacheEntry entry)
        {
            if (entry?.Asset == null)
            {
                return;
            }

            if (entry.Asset is GameObject)
            {
                return;
            }

            Resources.UnloadAsset(entry.Asset);
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
