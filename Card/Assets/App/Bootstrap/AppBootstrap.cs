using System;
using System.Threading.Tasks;
using App.AdShop;
using App.Atlas;
using App.Audio;
using App.Bag;
using App.Config;
using CardShare.Contracts.Config;
using App.Energy;
using App.Game;
using App.Guide;
using App.Level;
using App.Score;
using App.Talent;
using App.TTReward;
using App.Unlock;
using App.Wallet;
using App.Net;
using App.UI;
using App.UI.Popup;
using App.UI.Splash;
using Framework.Assets;
using Framework.Log;
using Framework.Save;
using Framework.UI;
using Framework.UI.Dialog;
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

        // 发布包关闭全部日志输出（编辑器保留）。Debug/AppLog 最终都走 unityLogger。
        // 调黑屏问题临时打开：定位完恢复为 false 再发布。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void DisableReleaseLogging()
        {
#if !UNITY_EDITOR && !ENABLE_APP_LOG
            Debug.unityLogger.logEnabled = false;
#endif
        }

        private async void Start()
        {
            // 微信小游戏端锁 60：设 120 会在高刷屏上满负载发热降频，帧率反而更低更不稳
#if WEIXINMINIGAME || PLATFORM_WEIXINMINIGAME
            Application.targetFrameRate = 60;
#else
            Application.targetFrameRate = 120;
#endif
            _services = AppServices.Create();

            _resources = ResourceFramework.Create();
            await _resources.InitializeAsync();
            _services.Register(_resources.Resources);
            AppLog.Info(LogChannel.UI, "[Boot] resources initialized"); // 临时排查黑屏，定位后删除

            // 忠告页只需最小初始化：存档（钱包/体力依赖它）、UIRoot 与 UI 框架。
            // 重型初始化（图集/配置表/头像/业务服务/列表项预热）在忠告展示期间并发执行。
            _services.Register(SaveFramework.Create());
            RegisterWallet(_services);
            RegisterEnergy(_services);
            _services.Register(new GameSession());
            _services.Container.AddSingleton<PvpWsClient>();
            _services.Container.AddSingleton<PvpMatchSession>();
            _services.Container.AddSingleton<GameTableViewModel>();
            _services.Container.AddSingleton<NavigationViewModel>();
            _services.Container.AddSingleton<MainResourceViewModel>();

            await _resources.Resources.LoadAsync<UnityEngine.GameObject>(Framework.UI.Navigation.UIRoot.ResourcesPath);
            _ui = UIFramework.Create(_services.Container);
            AppLog.Info(LogChannel.UI, "[Boot] ui framework ready"); // 临时排查黑屏，定位后删除

            // Toast 提示服务：依赖 IUINavigator，须在 UIFramework.Create 之后注册；懒实例化
            _services.Container.AddSingleton<ToastService>();

            var launchInit = new LaunchInitialization();
            _services.Register(launchInit);

            if (HealthAdvisoryPolicy.ShouldShowOnLaunch())
            {
                AppLog.Info(LogChannel.UI, "[Boot] show health advisory"); // 临时排查黑屏，定位后删除
                // 先开忠告页（独占资源加载），再并发跑重型初始化；
                // 忠告页倒计时与初始化两者都完成后由忠告页自行进首页。
                await _ui.UI.Open(_services.Resolve<HealthAdvisoryViewModel>());
                _ = RunHeavyInitAsync(launchInit);
            }
            else
            {
                AppLog.Info(LogChannel.UI, "[Boot] skip advisory, heavy init"); // 临时排查黑屏，定位后删除
                await RunHeavyInitAsync(launchInit);
                AppLog.Info(LogChannel.UI, "[Boot] heavy init done, open home"); // 临时排查黑屏，定位后删除
                await OpenHomeWithNavigation();
                AppLog.Info(LogChannel.UI, "[Boot] home opened"); // 临时排查黑屏，定位后删除
            }
        }

        private async Task RunHeavyInitAsync(LaunchInitialization launchInit)
        {
            try
            {
                await InitializeHeavyAsync();
                launchInit.Complete();
            }
            catch (Exception ex)
            {
                launchInit.Fail(ex);
                throw;
            }
        }

        private async Task InitializeHeavyAsync()
        {
            await RegisterAtlas(_services, _resources.Resources);
            await CardShadowPool.PreloadAsync(_resources.Resources);
            await AttackTuningConfig.PreloadAsync(_resources.Resources);

            RegisterAudio(_services);
            await _services.Resolve<IAudioService>().PreloadUiClickAsync(_resources.Resources);

            await ConfigTables.LoadAsync(_resources.Resources);
            UnityGameConfigLoader.LoadFromAppConfig();
            CardShare.Battle.HandEvaluator.Tables = UnityGameConfigLoader.Current;
            await PortraitLoader.PreloadAsync(_resources.Resources);
            RegisterBag(_services);
            RegisterLevel(_services);
            RegisterScore(_services);
            RegisterTalent(_services);
            RegisterAdShop(_services);
            RegisterTTReward(_services);
            RegisterUnlock(_services);
            LogConfigSmoke();

            RegisterGuide(_services);

            await PrewarmLevelItemsAsync();
        }

        /// <summary>趁忠告展示期把选关列表项与图鉴/天赋卡槽实例化进对象池，
        /// 首次打开这些界面不再逐个 Instantiate。卡槽数量按视口可见量估，不足由 Rent 兜底补建。</summary>
        private async Task PrewarmLevelItemsAsync()
        {
            var heroCount = HeroConfig.All != null ? HeroConfig.All.Count : 0;
            var levelCount = 0;
            var levels = _services.Resolve<ILevelService>();
            var diffs = levels.GetDifficulties();
            if (diffs != null)
            await ConnectAndPrepareAsync();

            if (HealthAdvisoryPolicy.ShouldShowOnLaunch())
            {
                for (var i = 0; i < diffs.Count; i++)
                {
                    if (levels.Get(diffs[i], 1) != null)
                    {
                        levelCount++;
                    }
                }
            }

            await LevelItemPool.PrewarmAsync(_resources.Resources, heroCount, levelCount);
            await UiCardPool.PrewarmAsync(_resources.Resources, itemCardCount: 16, playerItemCount: 8);
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
            PlayItemSpriteLibrary.Bind(atlas);
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

        private static void RegisterTTReward(AppServicesHost services)
        {
            var reward = new TTRewardService(
                services.Resolve<ISaveService>(),
                services.Resolve<IWalletService>());
            reward.Load();
            services.Register(reward);
            services.Register<ITTRewardService>(reward);
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

        private async Task ConnectAndPrepareAsync()
        {
            var client = new GameApiClient(_services.Resolve<ISaveService>());
            _services.Register(client);
            GameApi.Bind(client);

            var dialogs = _services.Resolve<IDialogService>();
            await PveSessionGate.ConnectWithRetryAsync(
                dialogs,
                SystemInfo.deviceUniqueIdentifier,
                "Editor");
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
