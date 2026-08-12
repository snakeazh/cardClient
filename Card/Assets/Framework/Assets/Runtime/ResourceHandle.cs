using System;
using Object = UnityEngine.Object;

namespace Framework.Assets
{
    /// <summary>
    /// Disposable handle that releases one reference on Dispose.
    /// </summary>
    public sealed class ResourceHandle<T> : IDisposable where T : Object
    {
        private readonly IResourceService _service;
        private readonly string _key;
        private bool _disposed;

        public ResourceHandle(IResourceService service, string key, T asset)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Resource key cannot be empty.", nameof(key));
            }

            _key = key;
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        }

        public string Key => _key;
        public T Asset { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.Release(_key);
        }
    }
}
