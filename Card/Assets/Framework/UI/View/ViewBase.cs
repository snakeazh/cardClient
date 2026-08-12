using System.Threading.Tasks;
using Framework.UI.Binding;
using UnityEngine;

namespace Framework.UI.View
{
    public interface IView
    {
        GameObject gameObject { get; }
        Transform transform { get; }
        ViewModelBase ViewModelObject { get; }
        Task Open(ViewModelBase viewModel, object args);
        Task Hide();
        Task Close();
    }

    public abstract class ViewBase<TVm> : MonoBehaviour, IView where TVm : ViewModelBase
    {
        [SerializeField] private UIReference _ui;

        private BindingContext _binding;
        private bool _opened;

        public TVm ViewModel { get; private set; }
        public ViewModelBase ViewModelObject => ViewModel;
        protected BindingContext Binding => _binding;

        protected UIReference UI
        {
            get
            {
                if (_ui == null)
                {
                    _ui = GetComponent<UIReference>();
                }

                return _ui;
            }
        }

        public async Task Open(ViewModelBase viewModel, object args)
        {
            if (_opened)
            {
                await Close();
            }

            if (UI == null)
            {
                throw new MissingComponentException(
                    $"{GetType().Name} requires a UIReference on the same GameObject.");
            }

            ViewModel = (TVm)viewModel;
            _binding = new BindingContext();
            _opened = true;

            gameObject.SetActive(true);
            await OnViewOpen();
            OnBind();
            await ViewModel.Open(args);
        }

        public async Task Hide()
        {
            if (!_opened)
            {
                return;
            }

            if (ViewModel != null)
            {
                await ViewModel.Hide();
            }

            await OnViewHide();
        }

        public async Task Close()
        {
            if (!_opened)
            {
                return;
            }

            _opened = false;
            Unbind();
            await OnViewClose();

            if (ViewModel != null)
            {
                await ViewModel.Close();
                ViewModel = null;
            }
        }

        protected abstract void OnBind();

        protected virtual Task OnViewOpen() => Task.CompletedTask;

        protected virtual Task OnViewHide() => Task.CompletedTask;

        protected virtual Task OnViewClose() => Task.CompletedTask;

        private void Unbind()
        {
            _binding?.Dispose();
            _binding = null;
        }

        protected virtual async void OnDestroy()
        {
            if (_opened)
            {
                await Close();
            }
        }
    }
}
