using System;
using System.Collections.Generic;

namespace Framework.UI.Core
{
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items = new List<IDisposable>();
        private bool _disposed;

        public void Add(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            if (_disposed)
            {
                disposable.Dispose();
                return;
            }

            _items.Add(disposable);
        }

        public void Clear()
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Dispose();
            }

            _items.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Clear();
        }
    }
}
