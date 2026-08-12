using App.UI;
using Framework.Assets;
using Framework.UI;
using Framework.UI.DI;
using Framework.UI.Navigation;
using UnityEngine;

namespace App.Bootstrap
{
    /// <summary>
    /// Application entry: creates DI container, initializes resources and UI, opens home screen.
    /// Place on a GameObject in the bootstrap scene.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        public static readonly ScreenId HomeScreenId = new ScreenId("App.Home");

        private ServiceContainer _container;
        private ResourceFrameworkContext _resources;
        private UIFrameworkContext _ui;

        private async void Start()
        {
            DontDestroyOnLoad(gameObject);

            _container = CreateContainer();
            _resources = ResourceFramework.Create();
            await _resources.InitializeAsync();
            RegisterResources(_container, _resources);

            _ui = UIFramework.Create(_container);
            RegisterAppServices(_container);
            RegisterAppScreens(_ui);

            await _ui.UI.Open(_container.Resolve<HomeViewModel>());
        }

        private static ServiceContainer CreateContainer()
        {
            var container = new ServiceContainer();
            container.RegisterInstance(container);
            return container;
        }

        private static void RegisterResources(ServiceContainer container, ResourceFrameworkContext resources)
        {
            var service = resources.Resources;
            container.RegisterInstance(service);
            container.RegisterInstance<IResourceService>(service);
        }

        private static void RegisterAppServices(ServiceContainer container)
        {
            // Register app-level services here, e.g.:
            // container.AddSingleton<IDeckService, DeckService>();
        }

        private static void RegisterAppScreens(UIFrameworkContext ui)
        {
            ui.UI.RegisterScreen<HomeView, HomeViewModel>(
                HomeScreenId,
                UILayer.Page,
                HomeView.ResourcesPath);
        }
    }
}
