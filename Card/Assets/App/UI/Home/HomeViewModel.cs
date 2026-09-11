using System;
using System.Threading.Tasks;
using App.Audio;
using App.Config;
using App.Energy;
using App.Level;
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
        private readonly ILevelService _levels;
        private readonly ILevelProgressService _progress;
        private readonly TalentBonusManager _talentBonus;
        private readonly IAudioService _audio;
        private readonly IUnlockConditionService _unlock;

        public HomeViewModel(
            IUIManager ui,
            NavigationViewModel navigation,
            ILevelService levels,
            ILevelProgressService progress,
            TalentBonusManager talentBonus,
            IResourceService resources,
            IAudioService audio,
            IUnlockConditionService unlock)
        {
            _ui = ui;
            _navigation = navigation;
            _levels = levels;
            _progress = progress;
            _talentBonus = talentBonus;
            _audio = audio;
            _unlock = unlock;
            Resources = resources;
            LastStageInfo = new ObservableProperty<string>();
            StaminaText = new ObservableProperty<string>();
            StartCommand = new RelayCommand(OpenLevelUI);
            Hero = HeroConfig.Get(LevelUIViewModel.GetDefaultHeroId());
        }

        public IResourceService Resources { get; }

        public HeroConfig Hero { get; }

        public HeroPanelStats PanelStats =>
            _talentBonus != null ? _talentBonus.Evaluate(Hero) : TalentBonusManager.EvaluateBase(Hero);

        public ObservableProperty<string> LastStageInfo { get; }

        /// <summary>开始按钮上的每局体力消耗标注，如 "x1"。当前体力在顶部资源栏显示。</summary>
        public ObservableProperty<string> StaminaText { get; }

        public IRelayCommand StartCommand { get; }

        protected override async Task OnOpen(object args)
        {
            RefreshLastStage();
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
                var clip = await Resources.LoadAsync<AudioClip>(ResResourcePaths.Bgm);
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

        private void RefreshLastStage()
        {
            if (_progress.LastLevelId > 0 && _levels.TryGetById(_progress.LastLevelId, out var snapshot) &&
                snapshot != null)
            {
                LastStageInfo.Value = $"难度{snapshot.Difficulty} 第{snapshot.Level}关";
                return;
            }

            LastStageInfo.Value = "尚未闯关";
        }

        private async void OpenLevelUI()
        {
            _navigation.HideBar();
            var registration = _ui.Registry.GetByViewModelType(typeof(LevelUIViewModel));
            var vm = (LevelUIViewModel)_ui.Registry.CreateViewModel(registration);
            await _ui.Open(vm);
        }
    }
}
