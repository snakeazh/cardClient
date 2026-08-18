using System.Threading.Tasks;
using App.Atlas;
using App.Bag;
using App.Config;
using App.Game;
using App.Level;
using App.Score;
using App.UI;
using Framework.Assets;
using Framework.Save;
using Framework.UI;
using UnityEngine;

namespace App.Bootstrap
{
    /// <summary>
    /// Application entry: builds the persistent AppServicesHost, initializes resources and UI,
    /// then opens the home screen. Place on a GameObject in the bootstrap scene.
    /// All services live on AppServicesHost, which survives scene loads.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        private AppServicesHost _services;
        private ResourceFrameworkContext _resources;
        private UIFrameworkContext _ui;

        private async void Start()
        {
            Application.targetFrameRate = 120;
            _services = AppServices.Create();

            _resources = ResourceFramework.Create();
            await _resources.InitializeAsync();
            _services.Register(_resources.Resources);

            await RegisterAtlas(_services, _resources.Resources);

            _services.Register(SaveFramework.Create());
            _services.Register(new GameSession());
            _services.Container.AddSingleton<GameTableViewModel>();

            await ConfigTables.LoadAsync(_resources.Resources);
            RegisterBag(_services);
            RegisterLevel(_services);
            RegisterScore(_services);
            LogConfigSmoke();

            _ui = UIFramework.Create(_services.Container);

            await _ui.UI.Open(_services.Resolve<HomeViewModel>());
        }

        private static async Task RegisterAtlas(
            AppServicesHost services,
            IResourceService resources)
        {
            var atlas = new AtlasService(resources);
            await atlas.PreloadAsync();
            services.Register(atlas);
            services.Register<IAtlasService>(atlas);
            CardSpriteLibrary.Bind(atlas);
        }

        private static void RegisterBag(AppServicesHost services)
        {
            var bag = new BagService(services.Resolve<ISaveService>());
            bag.Load();
            services.Register(bag);
            services.Register<IBagService>(bag);
        }

        private static void RegisterLevel(AppServicesHost services)
        {
            var level = new LevelService();
            services.Register(level);
            services.Register<ILevelService>(level);

            var progress = new LevelProgressService(services.Resolve<ISaveService>(), level);
            progress.Load();
            services.Register(progress);
            services.Register<ILevelProgressService>(progress);
        }

        private static void RegisterScore(AppServicesHost services)
        {
            var save = services.Resolve<ISaveService>();

            var score = new ScoreService(save);
            score.Load();
            services.Register(score);
            services.Register<IScoreService>(score);

            var hp = new HpService();
            services.Register(hp);
            services.Register<IHpService>(hp);

            var courage = new CourageService();
            services.Register(courage);
            services.Register<ICourageService>(courage);
        }

        private static void LogConfigSmoke()
        {
            var sample = ItemConfig.Get(1010001);
            Debug.Log(
                $"[Config] loaded: GameFps={GameConst.Instance.GameFps}, " +
                $"ItemCount={ItemConfig.Count}, " +
                $"ItemType={sample.Type}, " +
                $"Item(1010001)={sample?.Desc ?? "(missing)"}");
        }
    }
}
