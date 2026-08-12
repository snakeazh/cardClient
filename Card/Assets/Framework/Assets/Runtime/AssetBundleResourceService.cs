using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Framework.Assets
{
    /// <summary>
    /// Loads assets from AssetBundles under StreamingAssets/Bundles.
    /// Key format: "UI/Home" -> bundle "ui", asset name "Home".
    /// </summary>
    public sealed class AssetBundleResourceService : IResourceService
    {
        private const string ManifestBundleName = "Bundles";

        private readonly string _bundleRoot;
        private readonly Dictionary<string, AssetBundle> _bundles =
            new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        private AssetBundleManifest _manifest;
        private bool _initialized;

        public AssetBundleResourceService(string bundleRoot = null)
        {
            _bundleRoot = bundleRoot ?? Path.Combine(Application.streamingAssetsPath, "Bundles");
        }

        public bool IsInitialized => _initialized;

        public string BundleVersion { get; private set; }

        public string BundleRoot => _bundleRoot;

        public Task InitializeAsync()
        {
            lock (_gate)
            {
                if (_initialized)
                {
                    return Task.CompletedTask;
                }

                if (!Directory.Exists(_bundleRoot))
                {
                    throw new DirectoryNotFoundException(
                        $"Bundle root not found: {_bundleRoot}. Run menu Res/Build AssetBundles.");
                }

                BundleVersion = ReadBundleVersion();
                LoadManifestBundle();
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
                _manifest = null;

                foreach (var pair in _bundles)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.Unload(true);
                    }
                }

                _bundles.Clear();
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

                var bundleName = ResolveBundleName(key);
                var assetName = ResolveAssetName(key);
                var bundle = LoadBundle(bundleName);
                var loaded = bundle.LoadAsset<T>(assetName);
                if (loaded == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load '{assetName}' as {typeof(T).Name} from bundle '{bundleName}' (key '{key}').");
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
                    "Resource system is not initialized. Call ResourceFramework.InitializeAsync() first.");
            }
        }

        private string ReadBundleVersion()
        {
            var path = Path.Combine(_bundleRoot, "version.txt");
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllText(path).Trim();
        }

        private void LoadManifestBundle()
        {
            var manifestPath = Path.Combine(_bundleRoot, ManifestBundleName);
            if (!File.Exists(manifestPath))
            {
                return;
            }

            var manifestBundle = AssetBundle.LoadFromFile(manifestPath);
            if (manifestBundle == null)
            {
                return;
            }

            _bundles[ManifestBundleName] = manifestBundle;
            _manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }

        private AssetBundle LoadBundle(string bundleName)
        {
            if (_bundles.TryGetValue(bundleName, out var loaded) && loaded != null)
            {
                return loaded;
            }

            if (_manifest != null)
            {
                var dependencies = _manifest.GetAllDependencies(bundleName);
                for (var i = 0; i < dependencies.Length; i++)
                {
                    LoadBundle(dependencies[i]);
                }
            }

            var bundlePath = Path.Combine(_bundleRoot, bundleName);
            if (!File.Exists(bundlePath))
            {
                throw new FileNotFoundException(
                    $"AssetBundle not found: {bundlePath}. Run menu Res/Build AssetBundles.");
            }

            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                throw new InvalidOperationException($"Failed to load AssetBundle from {bundlePath}.");
            }

            _bundles[bundleName] = bundle;
            return bundle;
        }

        private static string ResolveBundleName(string key)
        {
            var slash = key.IndexOf('/');
            if (slash <= 0)
            {
                return key.ToLowerInvariant();
            }

            return key.Substring(0, slash).ToLowerInvariant();
        }

        private static string ResolveAssetName(string key)
        {
            var slash = key.LastIndexOf('/');
            return slash >= 0 ? key.Substring(slash + 1) : key;
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
