using System.Threading.Tasks;
using App.AdShop;
using App.Atlas;
using App.Audio;
using App.Bag;
using App.Config;
using App.Energy;
using App.Game;
using App.Guide;
using App.Level;
using App.Score;
using App.Talent;
using App.Unlock;
using App.Wallet;
using App.UI;
using App.UI.Splash;
using Framework.Assets;
using Framework.Log;
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
            await CardShadowPool.PreloadAsync(_resources.Resources);
            await AttackTuningConfig.PreloadAsync(_resources.Resources);

            _services.Register(SaveFramework.Create());
            RegisterAudio(_services);
            await _services.Resolve<IAudioService>().PreloadUiClickAsync(_resources.Resources);
            _services.Register(new GameSession());
            _services.Container.AddSingleton<GameTableViewModel>();
            _services.Container.AddSingleton<NavigationViewModel>();
            _services.Container.AddSingleton<MainResourceViewModel>();

            await ConfigTables.LoadAsync(_resources.Resources);
            await PortraitLoader.PreloadAsync(_resources.Resources);
            RegisterBag(_services);
            RegisterLevel(_services);
            RegisterScore(_services);
            RegisterTalent(_services);
            RegisterWallet(_services);
            RegisterEnergy(_services);
            RegisterAdShop(_services);
            RegisterUnlock(_services);
            LogConfigSmoke();

            await _resources.Resources.LoadAsync<UnityEngine.GameObject>(Framework.UI.Navigation.UIRoot.ResourcesPath);
            _ui = UIFramework.Create(_services.Container);

            // Toast 提示服务：依赖 IUINavigator，须在 UIFramework.Create 之后注册；懒实例化
            _services.Container.AddSingleton<ToastService>();
            RegisterGuide(_services);

            if (HealthAdvisoryPolicy.ShouldShowOnLaunch())
            {
                await _ui.UI.Open(_services.Resolve<HealthAdvisoryViewModel>());
            }
            else
            {
                await OpenHomeWithNavigation();
            }
        }

        private async Task OpenHomeWithNavigation()
        {
            await _ui.UI.Open(_services.Resolve<HomeViewModel>());
            await _services.Resolve<NavigationViewModel>().EnsureShown();
            await _services.Resolve<MainResourceViewModel>().EnsureShown();
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
            ItemBgSpriteLibrary.Bind(atlas);
        }

        private static void RegisterAudio(AppServicesHost services)
        {
            var audio = new AudioService(services.Resolve<ISaveService>(), services.gameObject);
            audio.Load();
            services.Register(audio);
            services.Register<IAudioService>(audio);
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

        private static void RegisterTalent(AppServicesHost services)
        {
            var talent = new TalentService(services.Resolve<ISaveService>());
            talent.Load();
            services.Register(talent);
            services.Register<ITalentService>(talent);
            services.Register(new TalentBonusManager(talent));
        }

        private static void RegisterWallet(AppServicesHost services)
        {
            var wallet = new WalletService(services.Resolve<ISaveService>());
            wallet.Load();
            services.Register(wallet);
            services.Register<IWalletService>(wallet);
        }

        private static void RegisterEnergy(AppServicesHost services)
        {
            var energy = new EnergyService(services.Resolve<ISaveService>());
            energy.Load();
            services.Register(energy);
            services.Register<IEnergyService>(energy);
        }

        private static void RegisterAdShop(AppServicesHost services)
        {
            var shop = new AdShopService(
                services.Resolve<ISaveService>(),
                services.Resolve<IEnergyService>(),
                services.Resolve<IWalletService>());
            shop.Load();
            services.Register(shop);
            services.Register<IAdShopService>(shop);
        }

        private static void RegisterUnlock(AppServicesHost services)
        {
            var unlock = new UnlockConditionService(services.Resolve<ISaveService>());
            unlock.Load();
            services.Register(unlock);
            services.Register<IUnlockConditionService>(unlock);
        }

        private static void RegisterGuide(AppServicesHost services)
        {
            var targets = new GuideTargetRegistry();
            services.Register(targets);

            var progress = new GuideProgressService(services.Resolve<ISaveService>());
            progress.Load();
            services.Register(progress);
            services.Register<IGuideProgressService>(progress);

            var guide = new GuideService(
                services.Resolve<IUIManager>(),
                progress,
                services.Resolve<GameSession>(),
                targets);
            services.Register(guide);
            services.Register<IGuideService>(guide);
        }

        private static void LogConfigSmoke()
        {
            var sample = ItemConfig.Get(101);
            AppLog.Info(
                LogChannel.Config,
                $"loaded: GameFps={GameConst.Instance.GameFps}, " +
                $"ItemCount={ItemConfig.Count}, " +
                $"ItemName={sample?.Name ?? "(missing)"}, " +
                $"Item(101)={sample?.Desc ?? "(missing)"}");
        }
    }
}
