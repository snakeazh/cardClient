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

            _ui = UIFramework.Create(_container);
            RegisterBoardController(_container);

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

        private static void RegisterBoardController(ServiceContainer container)
        {
            var hud = GameObject.Find("GameHud");
            if (hud == null)
            {
                hud = new GameObject("GameHud");
            }

            // 场景若仍挂着旧脚本名，替换为牌桌控制器
            var legacy = hud.GetComponent("GameTableController");
            if (legacy != null)
            {
                Object.Destroy(legacy);
            }

            var board = hud.GetComponent<GameBoardController>();
            if (board == null)
            {
                board = hud.AddComponent<GameBoardController>();
            }

            board.enabled = false;
            container.RegisterInstance(board);
        }
    }
}
