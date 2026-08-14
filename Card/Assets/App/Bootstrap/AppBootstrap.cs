using App.Config;
using App.Game;
using App.UI;
using Framework.Assets;
using Framework.UI;
using Framework.UI.DI;
using UnityEngine;

namespace App.Bootstrap
{
    /// <summary>
    /// Application entry: creates DI container, initializes resources and UI, opens home screen.
    /// Place on a GameObject in the bootstrap scene.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        private ServiceContainer _container;
        private ResourceFrameworkContext _resources;
        private UIFrameworkContext _ui;

        private async void Start()
        {
            DontDestroyOnLoad(gameObject);

            _container = CreateContainer();
            _container.RegisterInstance(new GameSession());
            _container.AddSingleton<GameTableViewModel>();

            _resources = ResourceFramework.Create();
            await _resources.InitializeAsync();
            RegisterResources(_container, _resources);

            await ConfigTables.LoadAsync(_resources.Resources);
            LogConfigSmoke();

            _ui = UIFramework.Create(_container);

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

        private static void LogConfigSmoke()
        {
            var sample = ItemConfig.Get(1010001);
            Debug.Log(
                $"[Config] loaded: GameFps={GameConst.Instance.GameFps}, " +
                $"ItemCount={ItemConfig.Count}, " +
                $"Item(1010001)={sample?.Desc ?? "(missing)"}");
        }
    }
}
