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
                _service = CreateBundleService(bundleRoot);
            }
#else
            _service = CreateBundleService(bundleRoot);
#endif
            return new ResourceFrameworkContext(_service);
        }

        private static IResourceService CreateBundleService(string bundleRoot)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: no file system, bundles are fetched over HTTP.
            return new WebGLAssetBundleResourceService(bundleRoot);
#else
            // Android copies APK StreamingAssets to persistentDataPath, then LoadFromFile.
            // iOS/PC/editor bundle mode read StreamingAssets directly.
            return new AssetBundleResourceService(bundleRoot);
#endif
        }

        private void OnDestroy()
        {
            _service?.ReleaseAll();
        }
    }
}
