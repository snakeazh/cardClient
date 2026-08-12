using System;
using System.Threading.Tasks;
using Framework.UI.Core;

namespace Framework.UI.View
{
    public abstract class ViewModelBase : IDisposable
    {
        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private bool _disposed;

        public bool IsOpen { get; private set; }

        protected void AddDisposable(IDisposable disposable) => _disposables.Add(disposable);

        internal async Task Open(object args)
        {
            IsOpen = true;
            await OnOpen(args);
        }

        internal async Task Hide()
        {
            await OnHide();
        }

        internal async Task Close()
        {
            if (!IsOpen)
            {
                Dispose();
                return;
            }

            IsOpen = false;
            await OnClose();
            Dispose();
        }

        protected virtual Task OnOpen(object args) => Task.CompletedTask;

        protected virtual Task OnHide() => Task.CompletedTask;

        protected virtual Task OnClose() => Task.CompletedTask;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposables.Dispose();
            OnDispose();
            GC.SuppressFinalize(this);
        }

        protected virtual void OnDispose()
        {
        }
    }
}
