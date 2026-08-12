using System;
using System.Threading.Tasks;
using Framework.Assets;
using Framework.UI.Dialog;
using Framework.UI.DI;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;

namespace Framework.UI
{
    /// <summary>
    /// Application-facing facade that organizes UI screens, layers and dialogs.
    /// </summary>
    public interface IUIManager
    {
        UIRoot Root { get; }
        ScreenRegistry Registry { get; }
        IDialogService Dialogs { get; }
        IResourceService Resources { get; }

        Task Open(ViewModelBase viewModel, object args = null);
        Task Open(ScreenId screenId, ViewModelBase viewModel, object args = null);
        Task Close(ViewModelBase viewModel = null);
        Task Close(UILayer layer);

        IUIManager RegisterScreen<TView, TVm>(
            ScreenId id,
            UILayer layer,
            GameObject prefab,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase;

        IUIManager RegisterScreen<TView, TVm>(
            ScreenId id,
            UILayer layer,
            string assetKey,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase;
    }

    /// <summary>
    /// Central UI organizer: owns root layers, screen registry, navigation and dialog entry.
    /// </summary>
    public sealed class UIManager : IUIManager
    {
        private readonly ServiceContainer _container;
        private readonly IUINavigator _navigator;

        public UIManager(
            ServiceContainer container,
            UIRoot root,
            ScreenRegistry registry,
            IUINavigator navigator,
            IDialogService dialogs,
            IResourceService resources)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public UIRoot Root { get; }
        public ScreenRegistry Registry { get; }
        public IDialogService Dialogs { get; }
        public IResourceService Resources { get; }

        public Task Open(ViewModelBase viewModel, object args = null) =>
            _navigator.Open(viewModel, args);

        public Task Open(ScreenId screenId, ViewModelBase viewModel, object args = null) =>
            _navigator.Open(screenId, viewModel, args);

        public Task Close(ViewModelBase viewModel = null) =>
            _navigator.Close(viewModel);

        public Task Close(UILayer layer) =>
            _navigator.Close(layer);

        public IUIManager RegisterScreen<TView, TVm>(
            ScreenId id,
            UILayer layer,
            GameObject prefab,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase
        {
            EnsureViewModelRegistered<TVm>();
            Registry.Register<TView, TVm>(id, layer, prefab, viewModelFactory);
            return this;
        }

        public IUIManager RegisterScreen<TView, TVm>(
            ScreenId id,
            UILayer layer,
            string assetKey,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase
        {
            EnsureViewModelRegistered<TVm>();
            Registry.Register<TView, TVm>(id, layer, assetKey, viewModelFactory);
            return this;
        }

        private void EnsureViewModelRegistered<TVm>() where TVm : ViewModelBase
        {
            if (!_container.IsRegistered(typeof(TVm)))
            {
                _container.AddTransient<TVm>();
            }
        }
    }
}
