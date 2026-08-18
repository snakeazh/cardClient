using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace Framework.Assets
{
    /// <summary>
    /// Loads assets from AssetBundles under StreamingAssets/Bundles via AssetBundle.LoadFromFile.
    /// Android packs StreamingAssets inside the APK (jar:file://): System.IO and LoadFromFile cannot
    /// read that path reliably, so InitializeAsync copies bundles to persistentDataPath first.
    /// WebGL uses <see cref="WebGLAssetBundleResourceService"/> instead.
    /// Key format: "UI/Home" -> bundle "ui", asset name "Home".
    /// </summary>
    public sealed class AssetBundleResourceService : IResourceService
    {
        private const string ManifestBundleName = "Bundles";
        private const string VersionFileName = "version.txt";
        private const string CatalogFileName = "catalog.txt";

        private static readonly string[] FallbackSharedBundles =
        {
            "animations", "materials", "altas"
        };

        private string _bundleRoot;
        private readonly string _packedStreamingRoot;
        private readonly Dictionary<string, AssetBundle> _bundles =
            new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        private readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        private AssetBundleManifest _manifest;
        private Task _initTask;
        private bool _initialized;

        public AssetBundleResourceService(string bundleRoot = null)
        {
            _bundleRoot = bundleRoot ?? Path.Combine(Application.streamingAssetsPath, "Bundles");
            _packedStreamingRoot = _bundleRoot;
        }

        private bool IsPackedStreamingPath =>
            _packedStreamingRoot.StartsWith("jar:", StringComparison.Ordinal);

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

                return _initTask ?? (_initTask = InitializeInternalAsync());
            }
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
                var bundle = LoadBundle(bundleName.ToLowerInvariant());
                return Task.FromResult(bundle.LoadAllAssets<T>());
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
            if (IsPackedStreamingPath)
            {
                await ExtractPackedBundlesAsync();
            }
            else if (!Directory.Exists(_bundleRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Bundle root not found: {_bundleRoot}. Run menu Res/Build AssetBundles.");
            }

            lock (_gate)
            {
                if (_initialized)
                {
                    return;
                }

                BundleVersion = ReadBundleVersion();
                LoadManifestBundle();
                _initialized = true;
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

                var bundleName = ResourceKeyResolver.ResolveBundleName(key);
                var assetName = ResourceKeyResolver.ResolveAssetName(key);
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
            var path = Path.Combine(_bundleRoot, VersionFileName);
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
                Debug.LogWarning(
                    $"Manifest bundle not found: {manifestPath}. Bundle dependencies will not be loaded. " +
                    "Run menu Res/Build AssetBundles.");
                return;
            }

            var manifestBundle = AssetBundle.LoadFromFile(manifestPath);
            if (manifestBundle == null)
            {
                Debug.LogWarning(
                    $"Failed to load manifest bundle: {manifestPath}. Bundle dependencies will not be loaded.");
                return;
            }

            _bundles[ManifestBundleName] = manifestBundle;
            _manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            if (_manifest == null)
            {
                Debug.LogWarning(
                    $"Manifest asset missing in bundle: {manifestPath}. Bundle dependencies will not be loaded.");
                return;
            }

            Debug.Log(
                $"[Assets] Manifest loaded: {_manifest.GetAllAssetBundles().Length} bundles, " +
                $"version {BundleVersion ?? "(unknown)"}.");
        }

        private AssetBundle LoadBundle(string bundleName)
        {
            if (_bundles.TryGetValue(bundleName, out var loaded) && loaded != null)
            {
                return loaded;
            }

            LoadDependencies(bundleName);

            if (_bundles.TryGetValue(bundleName, out loaded) && loaded != null)
            {
                return loaded;
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

        private void LoadDependencies(string bundleName)
        {
            if (_manifest != null)
            {
                var dependencies = _manifest.GetAllDependencies(bundleName);
                for (var i = 0; i < dependencies.Length; i++)
                {
                    LoadBundle(dependencies[i]);
                }

                return;
            }

            for (var i = 0; i < FallbackSharedBundles.Length; i++)
            {
                TryLoadOptionalBundle(FallbackSharedBundles[i]);
            }
        }

        private void TryLoadOptionalBundle(string bundleName)
        {
            if (_bundles.ContainsKey(bundleName))
            {
                return;
            }

            var path = Path.Combine(_bundleRoot, bundleName);
            if (!File.Exists(path))
            {
                return;
            }

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle != null)
            {
                _bundles[bundleName] = bundle;
            }
        }

        private async Task ExtractPackedBundlesAsync()
        {
            var persistentRoot = Path.Combine(Application.persistentDataPath, "Bundles");
            var remoteVersion = await FetchTextAsync(JoinUrl(_packedStreamingRoot, VersionFileName));
            if (HasCompleteExtract(persistentRoot, remoteVersion))
            {
                _bundleRoot = persistentRoot;
                Debug.Log($"[Assets] Using cached StreamingAssets extract at {persistentRoot} v{remoteVersion}.");
                return;
            }

            Directory.CreateDirectory(persistentRoot);

            var names = await ResolvePackedFileNamesAsync();
            if (names.Count == 0)
            {
                throw new DirectoryNotFoundException(
                    $"Bundle root not found: {_packedStreamingRoot}. " +
                    "Run menu Res/Build AssetBundles with the Android build target before making the APK.");
            }

            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                await FetchFileAsync(JoinUrl(_packedStreamingRoot, name), Path.Combine(persistentRoot, name));
            }

            File.WriteAllLines(Path.Combine(persistentRoot, CatalogFileName), names);
            if (!string.IsNullOrEmpty(remoteVersion))
            {
                File.WriteAllText(Path.Combine(persistentRoot, VersionFileName), remoteVersion);
            }

            _bundleRoot = persistentRoot;
            Debug.Log($"[Assets] Extracted {names.Count} bundles from APK to {persistentRoot} v{remoteVersion ?? "(unknown)"}.");
        }

        private async Task<List<string>> ResolvePackedFileNamesAsync()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var catalog = await FetchTextAsync(JoinUrl(_packedStreamingRoot, CatalogFileName));
            if (!string.IsNullOrEmpty(catalog))
            {
                var lines = catalog.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < lines.Length; i++)
                {
                    AddPackedFileName(names, seen, lines[i].Trim());
                }
            }

            if (names.Count == 0)
            {
                var manifestPath = Path.Combine(Application.temporaryCachePath, ManifestBundleName + ".ab");
                try
                {
                    await FetchFileAsync(JoinUrl(_packedStreamingRoot, ManifestBundleName), manifestPath);
                    var manifestBundle = AssetBundle.LoadFromFile(manifestPath);
                    if (manifestBundle != null)
                    {
                        var manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                        if (manifest != null)
                        {
                            AddPackedFileName(names, seen, ManifestBundleName);
                            var bundles = manifest.GetAllAssetBundles();
                            for (var i = 0; i < bundles.Length; i++)
                            {
                                AddPackedFileName(names, seen, bundles[i]);
                            }
                        }

                        manifestBundle.Unload(true);
                    }
                }
                finally
                {
                    if (File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                    }
                }
            }

            return names;
        }

        private static void AddPackedFileName(List<string> names, HashSet<string> seen, string name)
        {
            if (string.IsNullOrEmpty(name) ||
                name == CatalogFileName ||
                name == VersionFileName ||
                name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(name))
            {
                return;
            }

            names.Add(name);
        }

        private static bool HasCompleteExtract(string persistentRoot, string remoteVersion)
        {
            if (string.IsNullOrEmpty(remoteVersion) || !Directory.Exists(persistentRoot))
            {
                return false;
            }

            var localVersionPath = Path.Combine(persistentRoot, VersionFileName);
            if (!File.Exists(localVersionPath) ||
                !string.Equals(File.ReadAllText(localVersionPath).Trim(), remoteVersion, StringComparison.Ordinal))
            {
                return false;
            }

            var catalogPath = Path.Combine(persistentRoot, CatalogFileName);
            if (File.Exists(catalogPath))
            {
                var lines = File.ReadAllLines(catalogPath);
                for (var i = 0; i < lines.Length; i++)
                {
                    var name = lines[i].Trim();
                    if (string.IsNullOrEmpty(name) ||
                        name == CatalogFileName ||
                        name == VersionFileName ||
                        name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!File.Exists(Path.Combine(persistentRoot, name)))
                    {
                        return false;
                    }
                }

                return true;
            }

            return File.Exists(Path.Combine(persistentRoot, ManifestBundleName)) &&
                   File.Exists(Path.Combine(persistentRoot, "animations"));
        }

        private static string JoinUrl(string root, string relative)
        {
            root = root.Replace('\\', '/').TrimEnd('/');
            relative = relative.Replace('\\', '/').TrimStart('/');
            return root + "/" + relative;
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

        private static async Task FetchFileAsync(string url, string destPath)
        {
            var directory = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(destPath);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (File.Exists(destPath))
                    {
                        File.Delete(destPath);
                    }

                    throw new FileNotFoundException(
                        $"Failed to read '{url}' from StreamingAssets: {request.error}. " +
                        "Run menu Res/Build AssetBundles with the Android build target before making the APK.");
                }
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
