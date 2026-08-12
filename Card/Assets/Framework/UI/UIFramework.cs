using System;
using System.Reflection;
using Framework.Assets;
using Framework.UI.Dialog;
using Framework.UI.DI;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;

namespace Framework.UI
{
    /// <summary>
    /// Boots UIRoot, navigator, dialogs on an existing <see cref="ServiceContainer"/>.
    /// Container and <see cref="IResourceService"/> must be created and registered by the app layer.
    /// </summary>
    public static class UIFramework
    {
        public static UIFrameworkContext Create(ServiceContainer container, Transform parent = null)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            if (!container.IsRegistered(typeof(IResourceService)))
            {
                throw new InvalidOperationException(
                    "IResourceService is not registered on ServiceContainer. " +
                    "Initialize ResourceFramework and register IResourceService before UIFramework.Create().");
            }

            var resources = container.Resolve<IResourceService>();
            if (!resources.IsInitialized)
            {
                throw new InvalidOperationException(
                    "IResourceService is not initialized. Call await InitializeAsync() first.");
            }

            var rootPrefab = resources.LoadAsync<GameObject>(UIRoot.ResourcesPath).GetAwaiter().GetResult();
            if (rootPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing UIRoot asset for key '{UIRoot.ResourcesPath}'. " +
                    "Build bundles: menu Res/Build AssetBundles");
            }

            var root = UIRoot.Instantiate(rootPrefab, parent);
            var registry = new ScreenRegistry();
            registry.BindContainer(container);

            // Auto-register all screens marked with [AutoScreen] via reflection.
            ScreenAutoRegistrar.AutoRegisterScreens(container, registry);

            var navigator = new UINavigator(root, registry, resources);

            container.RegisterInstance(root);
            container.RegisterInstance(registry);
            container.RegisterInstance<IUINavigator>(navigator);

            if (!container.IsRegistered(typeof(DialogService)))
            {
                container.AddSingleton<DialogService>();
            }

            if (!container.IsRegistered(typeof(IDialogService)))
            {
                container.AddSingleton<IDialogService>(c => c.Resolve<DialogService>());
            }

            if (!container.IsRegistered(typeof(ConfirmDialogViewModel)))
            {
                container.AddTransient<ConfirmDialogViewModel>();
            }

            var dialogs = container.Resolve<IDialogService>();
            var manager = new UIManager(container, root, registry, navigator, dialogs, resources);
            container.RegisterInstance<IUIManager>(manager);
            container.RegisterInstance(manager);

            return new UIFrameworkContext(container, manager);
        }
    }

    public sealed class UIFrameworkContext
    {
        public UIFrameworkContext(ServiceContainer container, IUIManager uiManager)
        {
            Container = container ?? throw new ArgumentNullException(nameof(container));
            UI = uiManager ?? throw new ArgumentNullException(nameof(uiManager));
        }

        public ServiceContainer Container { get; }
        public IUIManager UI { get; }

        public UIRoot Root => UI.Root;
        public ScreenRegistry Registry => UI.Registry;
        public IDialogService Dialogs => UI.Dialogs;
        public IResourceService Resources => UI.Resources;

        public UIFrameworkContext RegisterScreen<TView, TVm>(
            ScreenId id,
            UILayer layer,
            GameObject prefab,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase
        {
            UI.RegisterScreen<TView, TVm>(id, layer, prefab, viewModelFactory);
            return this;
        }

        /// <summary>
        /// Register a screen by reading <see cref="AutoScreenAttribute"/> on the view type.
        /// </summary>
        public UIFrameworkContext RegisterScreen<TView, TVm>(
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase
        {
            var attr = typeof(TView).GetCustomAttribute<AutoScreenAttribute>(inherit: false);
            if (attr == null)
            {
                throw new InvalidOperationException(
                    $"View type '{typeof(TView).Name}' must be marked with [AutoScreen].");
            }

            return RegisterScreen<TView, TVm>(
                new ScreenId(attr.ScreenId),
                attr.Layer,
                attr.AssetKey,
                viewModelFactory);
        }

        public UIFrameworkContext RegisterScreen<TView, TVm>(
            ScreenId id,
            UILayer layer,
            string assetKey,
            Func<TVm> viewModelFactory = null)
            where TView : ViewBase<TVm>
            where TVm : ViewModelBase
        {
            UI.RegisterScreen<TView, TVm>(id, layer, assetKey, viewModelFactory);
            return this;
        }
    }
}
