using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Framework.Assets
{
    /// <summary>
    /// Runtime entry for the asset loading system. Call InitializeAsync before UIFramework.
    /// </summary>
    public static class ResourceFramework
    {
        public static ResourceFrameworkContext Create(string bundleRoot = null)
        {
            var go = new GameObject(nameof(ResourceFrameworkHost));
            GameObject.DontDestroyOnLoad(go);
            var host = go.AddComponent<ResourceFrameworkHost>();
            return host.CreateContext(bundleRoot);
        }
    }

    public sealed class ResourceFrameworkContext
    {
        private readonly IResourceService _service;

        public ResourceFrameworkContext(IResourceService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public IResourceService Resources => _service;

        public IResourceInitializer Initializer => _service;

        public bool IsInitialized => _service.IsInitialized;

        public string BundleRoot => _service.BundleRoot;

        public string BundleVersion => _service.BundleVersion;

        public Task InitializeAsync() => _service.InitializeAsync();

        public void ReleaseAll() => _service.ReleaseAll();
    }
}
