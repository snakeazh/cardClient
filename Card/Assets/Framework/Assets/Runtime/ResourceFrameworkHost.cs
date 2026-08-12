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
#if UNITY_EDITOR
            if (ResourceLoadMode.UseEditorRes)
            {
                _service = new EditorResResourceService();
            }
            else
            {
                _service = new AssetBundleResourceService(bundleRoot);
            }
#else
            _service = new AssetBundleResourceService(bundleRoot);
#endif
            return new ResourceFrameworkContext(_service);
        }

        private void OnDestroy()
        {
            _service?.ReleaseAll();
        }
    }
}
