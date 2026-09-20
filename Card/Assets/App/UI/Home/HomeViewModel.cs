using System;
using System.Threading.Tasks;
using App.Audio;
using App.Config;
using App.Energy;
using App.Resources;
using App.Talent;
using App.Unlock;
using App.UI.Popup;
using Framework.Assets;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    public sealed class HomeViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly TalentBonusManager _talentBonus;
        private readonly IAudioService _audio;
        private readonly IUnlockConditionService _unlock;
        private readonly ToastService _toast;

        public HomeViewModel(
            IUIManager ui,
            NavigationViewModel navigation,
            TalentBonusManager talentBonus,
            IResourceService resources,
            IAudioService audio,
            IUnlockConditionService unlock,
            ToastService toast)
        {
            _ui = ui;
            _navigation = navigation;
            _talentBonus = talentBonus;
            _audio = audio;
            _unlock = unlock;
            _toast = toast;
            Resources = resources;
            StaminaText = new ObservableProperty<string>();
            StartCommand = new RelayCommand(OpenLevelUI);
            Hero = HeroConfig.Get(LevelUIViewModel.GetDefaultHeroId());
        }

        public IResourceService Resources { get; }

        public HeroConfig Hero { get; }

        public HeroPanelStats PanelStats =>
            _talentBonus != null ? _talentBonus.Evaluate(Hero) : TalentBonusManager.EvaluateBase(Hero);

        /// <summary>开始按钮上的每局体力消耗标注，如 "x1"。当前体力在顶部资源栏显示。</summary>
        public ObservableProperty<string> StaminaText { get; }

        public IRelayCommand StartCommand { get; }

        protected override async Task OnOpen(object args)
        {
            await RefreshOnReturnAsync();
        }

        /// <summary>
        /// 从对局返回时复用压在 Page 栈底、未销毁的 Home：重跑打开时的刷新
        /// （体力/BGM/待解锁弹窗）。View 绑定在 Hide 期间保持存活，属性刷新直接生效。
        /// </summary>
        public async Task RefreshOnReturnAsync()
        {
            RefreshStamina();
            await StartHomeBgmAsync();
            await PresentPendingUnlocksAsync();
        }

        /// <summary>局内积压的新解锁遗物：弹 GetEquipDetail，不再 Toast。</summary>
        private async Task PresentPendingUnlocksAsync()
        {
            if (_unlock == null || _ui == null)
            {
                return;
            }

            var ids = _unlock.ConsumePendingUnlockRelicIds();
            if (ids == null || ids.Count == 0)
            {
                return;
            }

            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(GetEquipDetailViewModel));
                var vm = (GetEquipDetailViewModel)_ui.Registry.CreateViewModel(registration);
                vm.Setup(ids);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }

        /// <summary>
        /// HealthAdvisory 关闭后（或跳过忠告直进 Home）开始播主 BGM。
        /// 开关已在启动时从 audio.bgm.enabled.v1 读入；关闭则只挂曲不播。
        /// 再次打开 Home 时同一 clip 不会从头重播。
        /// </summary>
        private async Task StartHomeBgmAsync()
        {
            if (_audio == null || Resources == null)
            {
                return;
            }

            try
            {
                var clip = await Resources.LoadAsync<AudioClip>(ResResourcePaths.BgmLobby);
                _audio.PlayBgm(clip);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Assets, "Home BGM load failed: " + ex.Message);
            }
        }

        private void RefreshStamina()
        {
            StaminaText.Value = $"x{EnergyBalance.CostPerRun}";
        }

        private async void OpenLevelUI()
        {
            try
            {
                _navigation.HideBar();
                var registration = _ui.Registry.GetByViewModelType(typeof(LevelUIViewModel));
                var vm = (LevelUIViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                // async void 吞异常会让按钮"点了没反应"：记录并恢复导航栏，提示重试。
                AppLog.Exception(LogChannel.UI, ex);
                await _navigation.EnsureShown();
                _toast?.ShowWarning("选关界面打开失败，请重试");
            }
        }
    }
}
