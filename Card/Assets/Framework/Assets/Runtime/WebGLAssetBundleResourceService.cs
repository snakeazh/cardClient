using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Log;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace Framework.Assets
{
    /// <summary>
    /// WebGL: bundles under StreamingAssets are fetched over HTTP via UnityWebRequestAssetBundle.
    /// System.IO and AssetBundle.LoadFromFile are unavailable on this platform.
    /// Key format: "UI/Home" -> bundle "ui", asset name "Home".
    /// </summary>
    public sealed class WebGLAssetBundleResourceService : IResourceService
    {
        private const string ManifestBundleName = "Bundles";

        private readonly string _bundleRoot;
        private readonly Dictionary<string, AssetBundle> _bundles =
            new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<AssetBundle>> _pendingBundles =
            new Dictionary<string, Task<AssetBundle>>(StringComparer.Ordinal);
        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        private AssetBundleManifest _manifest;
        private Task _initTask;
        private bool _initialized;

        public WebGLAssetBundleResourceService(string bundleRoot = null)
        {
            _bundleRoot = bundleRoot ?? Application.streamingAssetsPath + "/Bundles";
        }

        public bool IsInitialized => _initialized;

        public string BundleVersion { get; private set; }

        public string BundleRoot => _bundleRoot;

        public Task InitializeAsync()
        {
            lock (_gate)
            {
                return _initTask ?? (_initTask = InitializeInternalAsync());
            }
        }

        public Task<T> LoadAsync<T>(string key) where T : Object
        {
            return LoadInternalAsync<T>(key);
        }

        public async Task<T[]> LoadAllAsync<T>(string bundleName) where T : Object
        {
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                throw new ArgumentException("Bundle name cannot be empty.", nameof(bundleName));
            }

            EnsureInitialized();
            var bundle = await LoadBundleAsync(bundleName.ToLowerInvariant());
            return bundle.LoadAllAssets<T>();
        }

        public async Task<ResourceHandle<T>> LoadHandleAsync<T>(string key) where T : Object
        {
            var asset = await LoadInternalAsync<T>(key);
            return new ResourceHandle<T>(this, key, asset);
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
                _initTask = null;
                _initialized = false;
                BundleVersion = null;
            }
        }

        private async Task InitializeInternalAsync()
        {
            // version.txt 自身也按 URL 缓存，加时间戳保证每次启动拿到最新版本号
            var versionUrl = _bundleRoot + "/version.txt?t=" + DateTime.UtcNow.Ticks;
            BundleVersion = await FetchTextAsync(versionUrl);
            if (string.IsNullOrEmpty(BundleVersion))
            {
                throw new InvalidOperationException(
                    $"Failed to fetch bundle version from '{versionUrl}'. " +
                    "Check DATA_CDN / StreamingAssets deployment and WeChat downloadFile domains.");
            }

            AppLog.Info(LogChannel.Assets, $"WebGL bundles version={BundleVersion} root={_bundleRoot}");
            await LoadManifestBundleAsync();
            _initialized = true;
        }

        private async Task<T> LoadInternalAsync<T>(string key) where T : Object
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
            }

            var bundleName = ResourceKeyResolver.ResolveBundleName(key);
            var assetName = ResourceKeyResolver.ResolveAssetName(key);
            var bundle = await LoadBundleAsync(bundleName);
            var loaded = bundle.LoadAsset<T>(assetName);
            if (loaded == null)
            {
                throw new InvalidOperationException(
                    $"Failed to load '{assetName}' as {typeof(T).Name} from bundle '{bundleName}' (key '{key}').");
            }

            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var raced) && raced.Asset is T racedTyped)
                {
                    raced.RefCount++;
                    return racedTyped;
                }

                _cache[key] = new CacheEntry(loaded, 1);
            }

            return loaded;
        }

        private async Task LoadManifestBundleAsync()
        {
            AssetBundle manifestBundle;
            try
            {
                manifestBundle = await LoadBundleAsync(ManifestBundleName);
            }
            catch (Exception e)
            {
                AppLog.Warn(
                    LogChannel.Assets,
                    $"Manifest bundle '{ManifestBundleName}' not loaded, bundle dependencies will not resolve: {e.Message}");
                return;
            }

            _manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }

        private Task<AssetBundle> LoadBundleAsync(string bundleName)
        {
            lock (_gate)
            {
                if (_bundles.TryGetValue(bundleName, out var loaded) && loaded != null)
                {
                    return Task.FromResult(loaded);
                }

                if (_pendingBundles.TryGetValue(bundleName, out var pending))
                {
                    return pending;
                }

                var task = DownloadBundleAsync(bundleName);
                _pendingBundles[bundleName] = task;
                return task;
            }
        }

        private async Task<AssetBundle> DownloadBundleAsync(string bundleName)
        {
            try
            {
                if (_manifest != null)
                {
                    var dependencies = _manifest.GetAllDependencies(bundleName);
                    for (var i = 0; i < dependencies.Length; i++)
                    {
                        await LoadBundleAsync(dependencies[i]);
                    }
                }

                var url = _bundleRoot + "/" + bundleName;
                if (!string.IsNullOrEmpty(BundleVersion))
                {
                    // 微信 SDK 算 __GAME_FILE_CACHE 缓存文件路径时会剥掉 ? 后的查询参数，
                    // ?v= 破不了真机缓存；版本号必须放进 URL 路径才会真正重新下载。
                    // 对应目录由 Res/Build AssetBundles 生成（WebGL 目标才会拷贝版本子目录）。
                    url = _bundleRoot + "/" + BundleVersion + "/" + bundleName;
                }

                using (var request = UnityWebRequestAssetBundle.GetAssetBundle(url))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        throw new InvalidOperationException(
                            $"Failed to download AssetBundle '{url}': {request.error}. " +
                            "Run menu Res/Build AssetBundles with the WebGL build target.");
                    }

                    var bundle = DownloadHandlerAssetBundle.GetContent(request);
                    lock (_gate)
                    {
                        _bundles[bundleName] = bundle;
                    }

                    return bundle;
                }
            }
            finally
            {
                lock (_gate)
                {
                    _pendingBundles.Remove(bundleName);
                }
            }
        }

        private static async Task<string> FetchTextAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                return request.result == UnityWebRequest.Result.Success
                    ? request.downloadHandler.text.Trim()
                    : null;
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
