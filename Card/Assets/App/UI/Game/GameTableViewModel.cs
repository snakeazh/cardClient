using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Atlas;
using App.Bootstrap;
using App.Game;
using App.Guide;
using App.Level;
using App.Resources;
using App.UI.Popup;
using Framework.Assets;
using Framework.Log;
using Framework.Save;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    /// <summary>把 <see cref="GameSession"/> 的状态绑到桌面 HUD：下注、看牌、商店、攻击。</summary>
    public sealed class GameTableViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly IGuideService _guide;
        private readonly ISaveService _save;
        private GameResourceViewModel _gameResource;
        private bool _shopPopupOpen;
        private bool _resultPopupOpen;
        private bool _infoPopupOpen;
        private bool _remainListOpen;
        private bool _settleShownThisShop;
        private string _shownInfoKey;

        public GameTableViewModel(
            GameSession session,
            IResourceService resources,
            IUIManager ui,
            NavigationViewModel navigation,
            ILevelProgressService progress,
            IAtlasService atlas,
            IGuideService guide,
            ISaveService save)
        {
            Session = session;
            Resources = resources;
            Progress = progress;
            Atlas = atlas;
            _ui = ui;
            _navigation = navigation;
            _guide = guide ?? throw new ArgumentNullException(nameof(guide));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            Session.Changed += Refresh;
            BlindBetCommand = new RelayCommand(
                () => Session.BlindBet(),
                () => !Session.AiActing &&
                      !Session.Player.Folded &&
                      (Session.Phase == GamePhase.Betting ||
                       Session.Phase == GamePhase.WaitingLookChoice ||
                       (Session.Phase == GamePhase.WaitingRub && Session.Player.Looked)));
            RaiseCommand = new RelayCommand(() => Session.RaiseBet(), () => Session.PlayerMayRaise);
            RaiseHighCommand = new RelayCommand(() => Session.RaiseBetHigh(), () => Session.PlayerMayRaise);
            LookCommand = new RelayCommand(() => Session.LookCards(), () => Session.PlayerMayLookCards);
            FoldCommand = new RelayCommand(() => Session.Fold(), () => Session.PlayerMayFold);
            OpenCommand = new RelayCommand(
                () => Session.RequestShowdown(),
                () => Session.PlayerMayCompare);
            AllInCommand = new RelayCommand(() => Session.AllIn(), () => Session.PlayerMayAllIn);
            PeekGoodCommand = new RelayCommand(() => Session.UsePeekGood(), () => Session.PlayerMayUsePeekGood);
            ChaKanGoodCommand = new RelayCommand(() => Session.UseChaKanGood(), () => Session.PlayerMayUseChaKanGood);
            XRayPlayerCommand = new RelayCommand(
                () => Session.TryXRayPlayer(),
                () => Session.SelectingXRayTarget && Session.PlayerMayUseChaKanGood);
            TiHuanGoodCommand = new RelayCommand(() => Session.UseTiHuanGood(), () => Session.PlayerMayUseTiHuanGood);
            RubCommand = new RelayCommand(() => Session.TryRubSelected(), () => Session.Phase == GamePhase.WaitingRub);
            SkipRubCommand = new RelayCommand(() => Session.CancelLookOrRub(), () => Session.PlayerMayCancelLookOrRub);
            MinusBetCommand = new RelayCommand(() => Session.AdjustBetUnits(-GameBalance.MinBet), () => Session.Phase == GamePhase.Betting);
            PlusBetCommand = new RelayCommand(() => Session.AdjustBetUnits(GameBalance.MinBet), () => Session.Phase == GamePhase.Betting);
            ContinueCommand = new RelayCommand(
                () => Session.Continue(),
                () => Session.Phase == GamePhase.RoundSettle ||
                      (Session.Phase == GamePhase.WaitingAttack &&
                       !Session.AttackPlaying &&
                       !Session.SequentialCompare));
            BackCommand = new RelayCommand(OnBack);
            LeaveShopCommand = new RelayCommand(() => Session.LeaveShop(), () => Session.Phase == GamePhase.Shop);
            LoanCommand = new RelayCommand(() => Session.WatchAdLoan(), () => Session.Phase == GamePhase.StageFail);
            ReviveCommand = new RelayCommand(() => Session.WatchAdRevive(), () => Session.Phase == GamePhase.StageFail);
            RestartCommand = new RelayCommand(() => Session.RestartStage(), () => Session.Phase == GamePhase.StageFail);
            ExtraRubAdCommand = new RelayCommand(() => Session.WatchAdExtraRub());
            DoubleGoldAdCommand = new RelayCommand(() => Session.WatchAdDoubleGold(), () => Session.Phase == GamePhase.Shop);
            OpenRemainListCommand = new RelayCommand(OpenRemainList);
            ToggleSpeedCommand = new RelayCommand(TogglePlaybackSpeed);
            LoadPlaybackSpeed();
            AttackCommands = new IRelayCommand[3];
            for (var i = 0; i < AttackCommands.Length; i++)
            {
                var slot = i;
                AttackCommands[i] = new RelayCommand(
                    () => Session.AttackEnemyAtSlot(slot),
                    () => Session.CanAttackSlot(slot));
            }

            Refresh();
        }

        public GameSession Session { get; }
        public IResourceService Resources { get; }
        public ILevelProgressService Progress { get; }
        public IAtlasService Atlas { get; }

        public ObservableProperty<string> Title { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> Hint { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> GoldText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PotText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerChips { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerBet { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerState { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> StageInfoText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> RoundInfo { get; } = new ObservableProperty<string>();
        public ObservableProperty<bool> ShowRoundInfo { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<string> RoundBuffName { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<string> RoundBuffDesc { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<bool> RoundBuffVisible { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<string> BetAmount { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> LogText { get; } = new ObservableProperty<string>();
        public ObservableProperty<bool> ShowActions { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowTableButtons { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> ShowActionBar { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowLook { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowRub { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowCancel { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowBlind { get; } = new ObservableProperty<bool>();
        public ObservableProperty<string> BlindLabel { get; } = new ObservableProperty<string>("闷注");
        public ObservableProperty<string> RaiseLabel { get; } = new ObservableProperty<string>("x2下注");
        public ObservableProperty<string> RaiseHighLabel { get; } = new ObservableProperty<string>("x4下注");
        public ObservableProperty<string> AllInLabel { get; } = new ObservableProperty<string>("全部下注");
        public ObservableProperty<string> PeekGoodLabel { get; } = new ObservableProperty<string>("搓牌(3/3)");
        public ObservableProperty<bool> PeekGoodArmed { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<string> ChaKanGoodLabel { get; } = new ObservableProperty<string>("透视(1/1)");
        public ObservableProperty<string> TiHuanGoodLabel { get; } = new ObservableProperty<string>("替换(1/1)");
        public ObservableProperty<bool> ShowAllIn { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowFold { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowCompare { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowContinue { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowShop { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowFail { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowAttack { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowMask { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowHpText { get; } = new ObservableProperty<bool>();
        public ObservableProperty<string> HpText { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<bool> ShowCardInfo { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> ShowYiwuBtn { get; } = new ObservableProperty<bool>(true);
        public ObservableProperty<float> PlaybackSpeed { get; } = new ObservableProperty<float>(MinPlaybackSpeed);
        public ObservableProperty<string> SpeedLabel { get; } = new ObservableProperty<string>("x1");
        public ObservableProperty<Sprite> CardTypeIcon { get; } = new ObservableProperty<Sprite>();
        public ObservableProperty<Sprite> CardTypeLabel { get; } = new ObservableProperty<Sprite>();
        public ObservableProperty<string> CardTypeNum { get; } = new ObservableProperty<string>(string.Empty);
        public bool IsHandSettling { get; private set; }
        public readonly ObservableProperty<bool>[] ShowEnemy =
        {
            new ObservableProperty<bool>(),
            new ObservableProperty<bool>(),
            new ObservableProperty<bool>()
        };
        public readonly ObservableProperty<string>[] EnemyChips =
        {
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty)
        };
        public readonly ObservableProperty<string>[] EnemyBet =
        {
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty)
        };
        public readonly ObservableProperty<string>[] EnemyState =
        {
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty)
        };

        public IRelayCommand BlindBetCommand { get; }
        public IRelayCommand RaiseCommand { get; }
        public IRelayCommand RaiseHighCommand { get; }
        public IRelayCommand LookCommand { get; }
        public IRelayCommand FoldCommand { get; }
        public IRelayCommand OpenCommand { get; }
        public IRelayCommand AllInCommand { get; }
        public IRelayCommand PeekGoodCommand { get; }
        public IRelayCommand ChaKanGoodCommand { get; }
        public IRelayCommand XRayPlayerCommand { get; }
        public IRelayCommand TiHuanGoodCommand { get; }
        public IRelayCommand RubCommand { get; }
        public IRelayCommand SkipRubCommand { get; }
        public IRelayCommand MinusBetCommand { get; }
        public IRelayCommand PlusBetCommand { get; }
        public IRelayCommand ContinueCommand { get; }
        public IRelayCommand BackCommand { get; }
        public IRelayCommand LeaveShopCommand { get; }
        public IRelayCommand LoanCommand { get; }
        public IRelayCommand ReviveCommand { get; }
        public IRelayCommand RestartCommand { get; }
        public IRelayCommand ExtraRubAdCommand { get; }
        public IRelayCommand DoubleGoldAdCommand { get; }
        public IRelayCommand OpenRemainListCommand { get; }
        public IRelayCommand ToggleSpeedCommand { get; }
        public IRelayCommand[] AttackCommands { get; }
        public const float MinPlaybackSpeed = 1f;
        public const float MaxPlaybackSpeed = 2f;
        public const string PlaybackSpeedSaveKey = "game.playback.speed.v1";
        private int _seenDealSerial = -1;
        private int _openingCompletedSerial = -1;

        /// <summary>每关第一手开场（对话+VS）尚未完成时拦住视觉发牌。</summary>
        public bool HoldDealVisual => ShouldHoldDealVisual();

        public bool ShouldHoldDealVisual()
        {
            return Session != null &&
                   Session.DealSerial > 0 &&
                   Session.StageRoundIndex == 1 &&
                   _openingCompletedSerial != Session.DealSerial;
        }

        public void CompleteOpening()
        {
            if (Session != null)
            {
                _openingCompletedSerial = Session.DealSerial;
            }
        }

        public void NotifyDealReady()
        {
            ShowTableButtons.Value = true;
            ShowRoundInfo.Value = true;
            GuideSignals.NotifyDealFinished(Session.DealSerial);
            Refresh();
        }

        public void Refresh()
        {
            if (Session.DealSerial != _seenDealSerial)
            {
                _seenDealSerial = Session.DealSerial;
                if (Session.DealSerial > 0)
                {
                    ShowTableButtons.Value = false;
                    ShowRoundInfo.Value = false;
                }
            }

            var run = Session.Run;
            var entries = BossMechanics.ResolveAll(run);
            Title.Value = run.HasBoss
                ? $"第{run.Stage}关 BOSS"
                : $"第{run.Stage}关";
            StageInfoText.Value = $"第{run.Stage}关";
            RoundBuffVisible.Value = entries.Count > 0;
            RoundBuffName.Value = FormatEntryNames(entries);
            RoundBuffDesc.Value = FormatEntryDescs(entries);
            Hint.Value = Session.Hint ?? string.Empty;
            GoldText.Value = run.Gold.ToString();
            if (_gameResource != null)
            {
                _gameResource.ShowBackBtn.Value = !_shopPopupOpen && !_resultPopupOpen;
            }
            PotText.Value = string.Empty;
            PlayerChips.Value = $"勇气 {Session.Player.Courage}";
            PlayerBet.Value = BetLabel(Session.Player);
            PlayerState.Value = SeatLine(Session.Player);
            RoundInfo.Value = $"第{Session.StageRoundIndex}轮";
            BetAmount.Value = string.Empty;
            var canAct = !Session.Player.Folded && !Session.AiActing;
            var opening = Session.Phase == GamePhase.WaitingOpen && canAct;
            var rubbing = Session.Phase == GamePhase.WaitingRub && canAct;
            PeekGoodLabel.Value = FormatCharges(
                "搓牌",
                Session.Run.PeekGoodCharges,
                SkillChargeMax(
                    GameBalance.SkillRubUses + Session.Run.BonusRubCharges,
                    RelicMechanics.SumValue(Session.Run, App.Config.MechanismType.RubbingCardsNum) +
                    HeroMechanics.SumValue(Session.Run, App.Config.MechanismType.RubbingCardsNum)));
            PeekGoodArmed.Value = Session.SelectingRubTarget;
            ChaKanGoodLabel.Value = FormatCharges(
                "透视",
                Session.Run.ChaKanGoodCharges,
                SkillChargeMax(
                    GameBalance.SkillXRayUses + Session.Run.BonusXRayCharges,
                    RelicMechanics.SumValue(Session.Run, App.Config.MechanismType.PerspectiveNum) +
                    HeroMechanics.SumValue(Session.Run, App.Config.MechanismType.PerspectiveNum)));
            TiHuanGoodLabel.Value = FormatCharges(
                "替换",
                Session.Run.TiHuanGoodCharges,
                GameBalance.SkillReplaceUses + Session.Run.BonusReplaceCharges);
            ShowLook.Value = false;
            ShowBlind.Value = false;
            ShowActions.Value = false;
            ShowRub.Value = false;
            ShowCancel.Value = Session.PlayerMayCancelLookOrRub;
            ShowAllIn.Value = false;
            ShowFold.Value = false;
            ShowCompare.Value = ShowTableButtons.Value && opening && Session.PlayerMayCompare;
            ShowActionBar.Value = opening || rubbing || ShowCompare.Value;
            ShowContinue.Value = Session.Phase == GamePhase.RoundSettle ||
                                 (Session.Phase == GamePhase.WaitingAttack &&
                                  !Session.AttackPlaying &&
                                  !Session.SequentialCompare);
            ShowShop.Value = Session.Phase == GamePhase.Shop;
            ShowFail.Value = Session.Phase == GamePhase.StageFail;
            if (Session.Phase != GamePhase.Shop)
            {
                _settleShownThisShop = false;
            }

            TryPresentShopPopup();
            TryPresentResultPopup();
            TryPresentGamePopupInfo();
            ShowAttack.Value = !Session.SequentialCompare &&
                               (Session.Phase == GamePhase.WaitingAttack || Session.SelectingOpenTarget);
            if (!Session.AttackPlaying)
            {
                ShowMask.Value = false;
                ShowHpText.Value = false;
            }
            RefreshEnemies();
            RefreshCardInfo();

            var start = run.Log.Count > 8 ? run.Log.Count - 8 : 0;
            var log = string.Empty;
            for (var i = start; i < run.Log.Count; i++)
            {
                if (i > start)
                {
                    log += "\n";
                }

                log += run.Log[i];
            }

            LogText.Value = log;

            BlindBetCommand.RaiseCanExecuteChanged();
            RaiseCommand.RaiseCanExecuteChanged();
            RaiseHighCommand.RaiseCanExecuteChanged();
            LookCommand.RaiseCanExecuteChanged();
            FoldCommand.RaiseCanExecuteChanged();
            OpenCommand.RaiseCanExecuteChanged();
            AllInCommand.RaiseCanExecuteChanged();
            PeekGoodCommand.RaiseCanExecuteChanged();
            ChaKanGoodCommand.RaiseCanExecuteChanged();
            XRayPlayerCommand.RaiseCanExecuteChanged();
            TiHuanGoodCommand.RaiseCanExecuteChanged();
            RubCommand.RaiseCanExecuteChanged();
            SkipRubCommand.RaiseCanExecuteChanged();
            ContinueCommand.RaiseCanExecuteChanged();
            LeaveShopCommand.RaiseCanExecuteChanged();
            LoanCommand.RaiseCanExecuteChanged();
            ReviveCommand.RaiseCanExecuteChanged();
            RestartCommand.RaiseCanExecuteChanged();
            DoubleGoldAdCommand.RaiseCanExecuteChanged();
            for (var i = 0; i < AttackCommands.Length; i++)
            {
                AttackCommands[i].RaiseCanExecuteChanged();
            }
        }

        protected override async Task OnOpen(object args)
        {
            _shownInfoKey = null;
            Session.Changed -= Refresh;
            Session.Changed += Refresh;
            Refresh();
            await ShowGameResource();
            _guide.TryStart(App.Config.GuideTriggerType.ScreenOpen, AppScreenIds.GameUI);
        }

        protected override async Task OnClose()
        {
            RestorePlaybackSpeed();
            _guide.Abort();
            await CloseGameResource();
        }

        protected override void OnDispose()
        {
            RestorePlaybackSpeed();
            Session.Changed -= Refresh;
            _ = CloseGameResource();
        }

        private void TogglePlaybackSpeed()
        {
            var next = NormalizePlaybackSpeed(PlaybackSpeed.Value) >= MaxPlaybackSpeed
                ? MinPlaybackSpeed
                : MaxPlaybackSpeed;
            ApplyPlaybackSpeed(next);
            PersistPlaybackSpeed(next);
        }

        private void LoadPlaybackSpeed()
        {
            ApplyPlaybackSpeed(NormalizePlaybackSpeed(_save.GetFloat(PlaybackSpeedSaveKey, MinPlaybackSpeed)));
        }

        private void ApplyPlaybackSpeed(float speed)
        {
            PlaybackSpeed.Value = speed;
            SpeedLabel.Value = FormatPlaybackSpeed(speed);
        }

        private void PersistPlaybackSpeed(float speed)
        {
            _save.SetFloat(PlaybackSpeedSaveKey, speed);
            _save.Save();
        }

        public static void RestorePlaybackSpeed()
        {
            Time.timeScale = 1f;
        }

        private static float NormalizePlaybackSpeed(float speed)
        {
            return speed >= MaxPlaybackSpeed - 0.01f ? MaxPlaybackSpeed : MinPlaybackSpeed;
        }

        private static string FormatPlaybackSpeed(float speed)
        {
            return NormalizePlaybackSpeed(speed) >= MaxPlaybackSpeed ? "x2" : "x1";
        }

        private async Task ShowGameResource()
        {
            await CloseGameResource();
            if (_ui == null)
            {
                return;
            }

            var registration = _ui.Registry.GetByViewModelType(typeof(GameResourceViewModel));
            _gameResource = (GameResourceViewModel)_ui.Registry.CreateViewModel(registration);
            _gameResource.BindBack(BackCommand);
            await _ui.Open(_gameResource);
        }

        private async Task CloseGameResource()
        {
            var bar = _gameResource;
            _gameResource = null;
            if (bar == null || !bar.IsOpen || _ui == null)
            {
                return;
            }

            await _ui.Close(bar);
        }

        private void SetShowBackBtn(bool visible)
        {
            if (_gameResource != null)
            {
                _gameResource.ShowBackBtn.Value = visible;
            }
        }

        private async void TryPresentResultPopup()
        {
            if (!IsOpen ||
                _resultPopupOpen ||
                _shopPopupOpen ||
                _ui == null)
            {
                return;
            }

            if (Session.Phase != GamePhase.RunComplete && Session.Phase != GamePhase.StageFail)
            {
                return;
            }

            _resultPopupOpen = true;
            SetShowBackBtn(false);
            try
            {
                await PresentResultThenLeaveOrRetry();
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _resultPopupOpen = false;
                SetShowBackBtn(!_shopPopupOpen);
            }

            TryPresentGamePopupInfo();
        }

        private async Task PresentResultThenLeaveOrRetry()
        {
            var action = await ShowBattleResultAsync();
            if (action == BattleResultAction.Again)
            {
                return;
            }

            await LeaveToHome();
        }

        private async void TryPresentShopPopup()
        {
            if (!IsOpen || Session.Phase != GamePhase.Shop || _shopPopupOpen || _ui == null)
            {
                return;
            }

            _shopPopupOpen = true;
            SetShowBackBtn(false);
            try
            {
                if (Session.IsLastLevel)
                {
                    Session.LeaveShop();
                }
                else
                {
                    if (!_settleShownThisShop)
                    {
                        _settleShownThisShop = true;
                        try
                        {
                            var settleReg = _ui.Registry.GetByViewModelType(typeof(BattleSettleUpPopViewModel));
                            var settle = (BattleSettleUpPopViewModel)_ui.Registry.CreateViewModel(settleReg);
                            await _ui.Dialogs.ShowCustomAsync<BattleSettleUpPopViewModel, bool>(settle);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Exception(LogChannel.UI, ex);
                        }
                    }

                    await Session.WaitShopReadyAsync();

                    while (IsOpen && Session.Phase == GamePhase.Shop)
                    {
                        var registration = _ui.Registry.GetByViewModelType(typeof(BattleShopPopViewModel));
                        var popup = (BattleShopPopViewModel)_ui.Registry.CreateViewModel(registration);
                        await _ui.Dialogs.ShowCustomAsync<BattleShopPopViewModel, bool>(popup);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _shopPopupOpen = false;
                SetShowBackBtn(!_resultPopupOpen);
            }

            TryPresentResultPopup();
            TryPresentGamePopupInfo();
        }

        private async Task<BattleResultAction> ShowBattleResultAsync(bool forfeitNoRevive = false)
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(BattleResultPopupViewModel));
            var popup = (BattleResultPopupViewModel)_ui.Registry.CreateViewModel(registration);
            return await _ui.Dialogs.ShowCustomAsync<BattleResultPopupViewModel, BattleResultAction>(
                popup,
                forfeitNoRevive);
        }

        private async Task<bool> ShowCommonTopAsync()
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(CommonTopViewModel));
            var popup = (CommonTopViewModel)_ui.Registry.CreateViewModel(registration);
            return await _ui.Dialogs.ShowCustomAsync<CommonTopViewModel, bool>(popup);
        }

        private async void OnBack()
        {
            if (_ui == null || _resultPopupOpen || _shopPopupOpen)
            {
                return;
            }

            _resultPopupOpen = true;
            SetShowBackBtn(false);
            try
            {
                var confirmed = await ShowCommonTopAsync();
                if (!confirmed || !IsOpen)
                {
                    return;
                }

                var action = await ShowBattleResultAsync(forfeitNoRevive: true);
                if (action == BattleResultAction.Again)
                {
                    return;
                }

                await LeaveToHome();
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _resultPopupOpen = false;
                SetShowBackBtn(!_shopPopupOpen);
            }
        }

        private async Task LeaveToHome()
        {
            await _ui.Close(this);
            var home = (HomeViewModel)_ui.Registry.CreateViewModel(
                _ui.Registry.GetByViewModelType(typeof(HomeViewModel)));
            await _ui.Open(home);
            await _navigation.EnsureShown();
            TryStartFirstTalentGuide();
        }

        private void TryStartFirstTalentGuide()
        {
            if (_guide == null || _guide.IsRunning)
            {
                return;
            }

            if (!AppServices.IsReady)
            {
                return;
            }

            var progress = AppServices.Resolve<IGuideProgressService>();
            if (progress == null || progress.IsGroupCompleted(GuideGroupIds.FirstTalentDraw))
            {
                return;
            }

            // 不强制 FirstBattle 已完成：关 GameUI 时 Abort 不会记完成，否则回大厅永远起不来。
            _guide.StartGroup(GuideGroupIds.FirstTalentDraw);
        }

        private void RefreshEnemies()
        {
            var activeCount = 0;
            for (var i = 0; i < Session.Enemies.Length; i++)
            {
                if (Session.Enemies[i].ActiveInStage)
                {
                    activeCount++;
                }
            }

            var shown = new bool[ShowEnemy.Length];
            var placed = 0;
            for (var i = 0; i < Session.Enemies.Length; i++)
            {
                var enemy = Session.Enemies[i];
                if (!enemy.ActiveInStage)
                {
                    continue;
                }

                var slot = VisualSlot(placed, activeCount);
                placed++;
                if (slot < 0 || slot >= ShowEnemy.Length)
                {
                    continue;
                }

                shown[slot] = enemy.Alive;
                EnemyChips[slot].Value = $"勇气 {enemy.Courage}";
                EnemyBet[slot].Value = BetLabel(enemy);
                EnemyState[slot].Value = SeatLine(enemy);
            }

            for (var i = 0; i < ShowEnemy.Length; i++)
            {
                ShowEnemy[i].Value = shown[i];
                if (!shown[i])
                {
                    EnemyChips[i].Value = string.Empty;
                    EnemyBet[i].Value = string.Empty;
                    EnemyState[i].Value = string.Empty;
                }
            }
        }

        private static string SeatLine(SeatState seat)
        {
            if (seat == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(seat.PeekedType))
            {
                return seat.Folded ? $"弃牌 {seat.PeekedType}" : $"透视 {seat.PeekedType}";
            }

            if (!string.IsNullOrEmpty(seat.Banner))
            {
                return seat.Banner;
            }

            if (seat.Folded)
            {
                return "弃牌";
            }

            return seat.Status ?? string.Empty;
        }

        private static string AmountLabel(string name, int amount)
        {
            return amount > 0 ? $"{name}{amount}" : name;
        }

        private static string BetLabel(SeatState seat)
        {
            if (seat == null)
            {
                return "下注(0)";
            }

            return $"下注({seat.StreetPaid})";
        }

        private static int VisualSlot(int enemyIndex, int activeCount)
        {
            return GameSession.TableVisualSlot(enemyIndex, activeCount);
        }

        private void RefreshCardInfo()
        {
            IsHandSettling = ShouldShowCardInfo(Session);
            var preview = ShouldShowPlayerHandPreview(Session);
            if ((!IsHandSettling && !preview) || Session.Player == null)
            {
                ShowCardInfo.Value = false;
                if (!IsHandSettling && !preview)
                {
                    CardTypeNum.Value = string.Empty;
                }

                return;
            }

            var score = Session.EvaluateSeat(Session.Player);
            ApplyCardType(score.Type, CardTypeIcon, CardTypeLabel, CardTypeNum);
            ShowCardInfo.Value = true;
        }

        public static bool ShouldShowCardInfo(GameSession session)
        {
            return session != null &&
                   (session.SequentialCompare ||
                    session.Phase == GamePhase.Showdown ||
                    session.Phase == GamePhase.WaitingAttack ||
                    session.Phase == GamePhase.RoundSettle);
        }

        /// <summary>开牌前选满 3 张时预览玩家牌型；亮牌结算仍走 <see cref="ShouldShowCardInfo"/>。</summary>
        public static bool ShouldShowPlayerHandPreview(GameSession session)
        {
            if (session?.Player == null || ShouldShowCardInfo(session))
            {
                return false;
            }

            if (session.Phase != GamePhase.WaitingOpen && session.Phase != GamePhase.WaitingRub)
            {
                return false;
            }

            return !session.Player.Folded &&
                   session.Player.CountSelectedCards() == GameBalance.OpenHandSize;
        }

        public void ApplyCardType(
            HandType type,
            ObservableProperty<Sprite> icon,
            ObservableProperty<Sprite> label,
            ObservableProperty<string> num)
        {
            if (icon != null)
            {
                icon.Value = GetCardTypeSprite(IconSpriteName(type));
            }

            if (label != null)
            {
                label.Value = GetCardTypeSprite(LabelSpriteName(type));
            }

            if (num != null)
            {
                num.Value = FormatMultiplier(GameSession.HandTypeMagnification(type));
            }
        }

        public Sprite GetCardTypeIcon(HandType type) => GetCardTypeSprite(IconSpriteName(type));

        public Sprite GetCardTypeLabel(HandType type) => GetCardTypeSprite(LabelSpriteName(type));

        public static string FormatHandMultiplier(HandType type)
        {
            return FormatMultiplier(GameSession.HandTypeMagnification(type));
        }

        private Sprite GetCardTypeSprite(string spriteName)
        {
            if (Atlas == null || string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.CardTypeAtlas, spriteName, out var sprite);
            return sprite;
        }

        private static string IconSpriteName(HandType type)
        {
            switch (type)
            {
                case HandType.Pair: return "PairIcon";
                case HandType.Straight: return "StraightIcon";
                case HandType.Flush: return "SameSuitIcon";
                case HandType.StraightFlush: return "FlushIcon";
                case HandType.ThreeOfAKind: return "LeopardIcon";
                default: return "HighCardIcon";
            }
        }

        private static string LabelSpriteName(HandType type)
        {
            switch (type)
            {
                case HandType.Pair: return "Pair";
                case HandType.Straight: return "Straight";
                case HandType.Flush: return "SameSuit";
                case HandType.StraightFlush: return "Flush";
                case HandType.ThreeOfAKind: return "Leopard";
                default: return "HighCard";
            }
        }

        public static string FormatMultiplier(float value)
        {
            var rounded = (float)Math.Round(value, 2);
            if (Math.Abs(rounded - (float)Math.Round(rounded)) < 0.001f)
            {
                return $"x{(int)Math.Round(rounded)}";
            }

            return $"x{rounded:0.##}";
        }

        public static string FormatBonus(float value)
        {
            var rounded = (float)Math.Round(value, 2);
            if (Math.Abs(rounded - (float)Math.Round(rounded)) < 0.001f)
            {
                return $"+{(int)Math.Round(rounded)}";
            }

            return $"+{rounded:0.##}";
        }

        private static string FormatCharges(string name, int current, int max)
        {
            return $"{name}({Math.Max(0, current)}/{Math.Max(0, max)})";
        }

        private static string FormatEntryNames(List<App.Config.BossEntryConfig> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                parts[i] = entries[i] != null ? entries[i].Name : string.Empty;
            }

            return string.Join("、", parts);
        }

        private static string FormatEntryDescs(List<App.Config.BossEntryConfig> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                var desc = string.IsNullOrEmpty(entry.Desc) ? entry.Name : entry.Desc;
                if (!string.IsNullOrEmpty(desc))
                {
                    parts.Add(desc);
                }
            }

            return string.Join("\n", parts);
        }

        private static int SkillChargeMax(int baseline, float extra)
        {
            return Math.Max(0, baseline + (int)Math.Round(extra));
        }

        private async void TryPresentGamePopupInfo()
        {
            if (!IsOpen ||
                _infoPopupOpen ||
                _shopPopupOpen ||
                _resultPopupOpen ||
                HoldDealVisual ||
                !ShowTableButtons.Value ||
                _ui == null)
            {
                return;
            }

            if (Session.Phase == GamePhase.Shop ||
                Session.Phase == GamePhase.RunComplete ||
                Session.Phase == GamePhase.StageFail)
            {
                return;
            }

            var entries = BossMechanics.ResolveAll(Session.Run);
            if (entries.Count == 0)
            {
                return;
            }

            var key = BuildInfoKey(Session.Run);
            if (string.IsNullOrEmpty(key) || key == _shownInfoKey)
            {
                return;
            }

            _shownInfoKey = key;
            _infoPopupOpen = true;
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(GamePopupInfoViewModel));
                var popup = (GamePopupInfoViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(popup);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _infoPopupOpen = false;
            }
        }

        private static string BuildInfoKey(RunState run)
        {
            if (run?.LevelEntryIds == null || run.LevelEntryIds.Count == 0)
            {
                return string.Empty;
            }

            return run.Stage + ":" + string.Join(",", run.LevelEntryIds);
        }

        private async void OpenRemainList()
        {
            if (_ui == null || _remainListOpen)
            {
                return;
            }

            _remainListOpen = true;
            ShowYiwuBtn.Value = false;
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(RemainListPopViewModel));
                var popup = (RemainListPopViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(popup);
                await popup.Closed;
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _remainListOpen = false;
                ShowYiwuBtn.Value = true;
            }
        }
    }
}
