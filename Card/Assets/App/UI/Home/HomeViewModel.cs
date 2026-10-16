using System;
using System.Threading.Tasks;
using App.Audio;
using App.Config;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using App.Energy;
using App.Game;
using App.Level;
using App.Net;
using App.PvpScore;
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
        private readonly TalentBonusManager _talentBonus;
        private readonly IAudioService _audio;
        private readonly IUnlockConditionService _unlock;
        private readonly ToastService _toast;
        private readonly GameSession _session;
        private readonly GameTableViewModel _table;
        private readonly PvpMatchSession _pvp;
        private readonly IPvpScoreService _pvpScore;
        private bool _openingPvp;
        private bool _openingLevel;
        private bool _pvpResumeChecked;
        private Task _resumeTask;

        public HomeViewModel(
            IUIManager ui,
            TalentBonusManager talentBonus,
            IResourceService resources,
            IAudioService audio,
            IUnlockConditionService unlock,
            ToastService toast,
            GameSession session,
            GameTableViewModel table,
            PvpMatchSession pvp,
            IPvpScoreService pvpScore)
        {
            _ui = ui;
            _talentBonus = talentBonus;
            _audio = audio;
            _unlock = unlock;
            _toast = toast;
            _session = session;
            _table = table;
            _pvp = pvp;
            _pvpScore = pvpScore ?? throw new ArgumentNullException(nameof(pvpScore));
            Resources = resources;
            StaminaText = new ObservableProperty<string>();
            PvpValueText = new ObservableProperty<string>();
            StartCommand = new RelayCommand(OpenLevelUI, () => !_openingLevel);
            OpenTTRewardCommand = new RelayCommand(OpenTTReward);
            OpenIllustrateCommand = new RelayCommand(OpenIllustrate);
            OpenTalentCommand = new RelayCommand(OpenTalent);
            OpenSettingCommand = new RelayCommand(OpenSetting);
            PvpCommand = new RelayCommand(OpenPvpPage, () => !_openingPvp);
            Hero = HeroConfig.Get(LevelUIViewModel.GetDefaultHeroId());
        }

        public IResourceService Resources { get; }

        public HeroConfig Hero { get; }

        public HeroPanelStats PanelStats =>
            _talentBonus != null ? _talentBonus.Evaluate(Hero) : TalentBonusManager.EvaluateBase(Hero);

        /// <summary>开始按钮上的每局体力消耗标注，如 "x1"。当前体力在顶部资源栏显示。</summary>
        public ObservableProperty<string> StaminaText { get; }

        /// <summary>Home 页 pvpValue：局外 PVP 分数，如 "0分"。</summary>
        public ObservableProperty<string> PvpValueText { get; }

        public IRelayCommand StartCommand { get; }

        /// <summary>打开抖音侧边栏奖励弹窗（Home 左侧栏 TTRewardItem 按钮）。</summary>
        public IRelayCommand OpenTTRewardCommand { get; }

        /// <summary>打开图鉴弹窗（Home IllustrateBtn）。</summary>
        public IRelayCommand OpenIllustrateCommand { get; }

        /// <summary>打开天赋弹窗（Home TalentBtn）。</summary>
        public IRelayCommand OpenTalentCommand { get; }

        /// <summary>打开设置弹窗（Home SettingBtn）。</summary>
        public IRelayCommand OpenSettingCommand { get; }

        public IRelayCommand PvpCommand { get; }

        protected override async Task OnOpen(object args)
        {
            _pvpScore.Changed += OnPvpScoreChanged;
            await RefreshOnReturnAsync();
            // 重连探测不能放在 OnOpen：此时 Home 尚未入栈，若在这里 Open(GameTable)，
            // 随后 Home 入栈会盖住桌面，表现为「还显示着主页」。
        }

        /// <summary>Home 已入栈并 Present 后调用：探测断线对局并弹重连。</summary>
        public void OnPresented()
        {
            // 仅自动探测残局；手动点匹配会 CancelProbe，不会等这个任务。
            if (_resumeTask == null)
            {
                _resumeTask = TryOfferPvpResumeAsync();
            }
        }

        protected override Task OnClose()
        {
            _pvpScore.Changed -= OnPvpScoreChanged;
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            _pvpScore.Changed -= OnPvpScoreChanged;
        }

        /// <summary>
        /// 从对局返回时复用压在 Page 栈底、未销毁的 Home：重跑打开时的刷新
        /// （体力/BGM/待解锁弹窗）。View 绑定在 Hide 期间保持存活，属性刷新直接生效。
        /// </summary>
        public async Task RefreshOnReturnAsync()
        {
            await RefreshProfileAsync();
            RefreshStamina();
            RefreshPvpValue();
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

        private async Task RefreshProfileAsync()
        {
            if (!GameApiSettings.Enabled || !GameApi.IsReady || GameApi.Client == null)
            {
                return;
            }

            try
            {
                var profile = await GameApi.Client.GetProfileAsync();
                GameApi.ApplyProfile(profile);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "Home profile refresh failed: " + ex.Message);
            }
        }

        private void RefreshStamina()
        {
            StaminaText.Value = $"x{EnergyBalance.CostPerRun}";
        }

        private void RefreshPvpValue()
        {
            PvpValueText.Value = $"{_pvpScore.Score}分";
        }

        private void OnPvpScoreChanged()
        {
            RefreshPvpValue();
        }

        private async void OpenLevelUI()
        {
            if (_openingLevel)
            {
                return;
            }

            _openingLevel = true;
            StartCommand.RaiseCanExecuteChanged();
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(LevelUIViewModel));
                var vm = (LevelUIViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                // async void 吞异常会让按钮"点了没反应"：记录并提示重试。
                AppLog.Exception(LogChannel.UI, ex);
                _toast?.ShowWarning("选关界面打开失败，请重试");
            }
            finally
            {
                _openingLevel = false;
                StartCommand.RaiseCanExecuteChanged();
            }
        }

        private async void OpenTTReward()
        {
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(TTRewardPopViewModel));
                var vm = (TTRewardPopViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                _toast?.ShowWarning("奖励界面打开失败，请重试");
            }
        }

        private async void OpenIllustrate()
        {
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(IllustratedBookPopViewModel));
                var vm = (IllustratedBookPopViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                _toast?.ShowWarning("图鉴打开失败，请重试");
            }
        }

        private async void OpenTalent()
        {
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(TalentPopupViewModel));
                var vm = (TalentPopupViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                _toast?.ShowWarning("天赋界面打开失败，请重试");
            }
        }

        private async void OpenSetting()
        {
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(SettingPopViewModel));
                var vm = (SettingPopViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                _toast?.ShowWarning("设置界面打开失败，请重试");
            }
        }

        /// <summary>
        /// 断线重登：sync 探测未结束对局 → CommonTop 确认返回对局，取消则 leave 判负逃跑。
        /// 每个登录会话只探测一次（Home 压栈复用时不重复弹）。
        /// </summary>
        private async Task TryOfferPvpResumeAsync()
        {
            if (_pvpResumeChecked || !GameApiSettings.Enabled || !GameApi.IsReady || _pvp == null)
            {
                return;
            }

            _pvpResumeChecked = true;
            try
            {
                var hasMatch = await _pvp.TryProbeActiveMatchAsync();
                if (!hasMatch)
                {
                    return;
                }

                var confirmed = await ShowCommonTopAsync("是否重连回对局？");
                if (confirmed)
                {
                    var self = _pvp.Self;
                    if (_pvp.State == null
                        || string.Equals(_pvp.State.Phase, PvpPhases.Finished, StringComparison.OrdinalIgnoreCase)
                        || self == null
                        || !self.Alive)
                    {
                        Toast.Error(self != null && !self.Alive ? "你已淘汰，无法重连" : "对局已结束");
                        await _pvp.StopAsync();
                        return;
                    }

                    _pvp.ActivateResumedMatch();
                    _session.BeginPvp();
                    await _ui.Open(_table);
                    return;
                }

                // 取消 = 直接逃跑（leave → Abandon 判负）
                await _pvp.StopAsync();
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                try
                {
                    await _pvp.StopAsync();
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task<bool> ShowCommonTopAsync(string tip)
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(CommonTopViewModel));
            var popup = (CommonTopViewModel)_ui.Registry.CreateViewModel(registration);
            return await _ui.Dialogs.ShowCustomAsync<CommonTopViewModel, bool>(popup, tip);
        }

        /// <summary>竞技场入口：打开 PVPPage，匹配与接受都在该页进行。</summary>
        private async void OpenPvpPage()
        {
            if (_openingPvp)
            {
                return;
            }

            _openingPvp = true;
            PvpCommand.RaiseCanExecuteChanged();
            var nav = _ui.FindOpen<NavigationViewModel>();
            try
            {
                _pvpResumeChecked = true;
                _pvp.CancelProbe();
                nav?.HideBar();
                var registration = _ui.Registry.GetByViewModelType(typeof(PvpPageViewModel));
                var vm = (PvpPageViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                _toast?.ShowWarning("竞技场打开失败，请重试");
                if (nav != null)
                {
                    await nav.EnsureShown();
                }
            }
            finally
            {
                _openingPvp = false;
                PvpCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
