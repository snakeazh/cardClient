using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace Framework.Assets
{
    /// <summary>
    /// Application-facing resource API. Call InitializeAsync before loading.
    /// </summary>
    public interface IResourceService : IResourceInitializer
    {
        Task<T> LoadAsync<T>(string key) where T : Object;

        Task<ResourceHandle<T>> LoadHandleAsync<T>(string key) where T : Object;

        bool TryGetCached<T>(string key, out T asset) where T : Object;

        void Release(string key);
    }
}
