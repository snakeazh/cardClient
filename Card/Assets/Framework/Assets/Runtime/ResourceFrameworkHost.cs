using UnityEngine;

namespace Framework.Assets
{
    /// <summary>
    /// Owns the resource service lifetime; releases bundles when this host is destroyed.
    /// </summary>
    internal sealed class ResourceFrameworkHost : MonoBehaviour
    {
        private IResourceService _service;

        public ResourceFrameworkContext CreateContext(string bundleRoot)
        {
            _service = new AssetBundleResourceService(bundleRoot);
            return new ResourceFrameworkContext(_service);
        }

        private void OnDestroy()
        {
            _service?.ReleaseAll();
        }
    }
}
