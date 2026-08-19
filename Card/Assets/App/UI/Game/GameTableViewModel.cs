using System;
using System.Threading.Tasks;
using App.Game;
using App.Level;
using App.UI.Popup;
using Framework.Assets;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    /// <summary>把 <see cref="GameSession"/> 的状态绑到桌面 HUD：下注、看牌、商店、攻击。</summary>
    public sealed class GameTableViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private bool _failPopupOpen;
        private bool _shopPopupOpen;

        public GameTableViewModel(
            GameSession session,
            IResourceService resources,
            IUIManager ui,
            ILevelProgressService progress)
        {
            Session = session;
            Resources = resources;
            Progress = progress;
            _ui = ui;
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
                      (Session.Phase == GamePhase.WaitingAttack && !Session.AttackPlaying));
            LeaveShopCommand = new RelayCommand(() => Session.LeaveShop(), () => Session.Phase == GamePhase.Shop);
            LoanCommand = new RelayCommand(() => Session.WatchAdLoan(), () => Session.Phase == GamePhase.StageFail);
            ReviveCommand = new RelayCommand(() => Session.WatchAdRevive(), () => Session.Phase == GamePhase.StageFail);
            RestartCommand = new RelayCommand(() => Session.RestartStage(), () => Session.Phase == GamePhase.StageFail);
            ExtraRubAdCommand = new RelayCommand(() => Session.WatchAdExtraRub());
            DoubleGoldAdCommand = new RelayCommand(() => Session.WatchAdDoubleGold(), () => Session.Phase == GamePhase.Shop);
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

        public ObservableProperty<string> Title { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> Hint { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> GoldText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PotText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerChips { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerBet { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerState { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> RoundInfo { get; } = new ObservableProperty<string>();
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
        public ObservableProperty<string> PeekGoodLabel { get; } = new ObservableProperty<string>("搓牌 3");
        public ObservableProperty<string> ChaKanGoodLabel { get; } = new ObservableProperty<string>("透视 1");
        public ObservableProperty<string> TiHuanGoodLabel { get; } = new ObservableProperty<string>("替换 1");
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
        public IRelayCommand LeaveShopCommand { get; }
        public IRelayCommand LoanCommand { get; }
        public IRelayCommand ReviveCommand { get; }
        public IRelayCommand RestartCommand { get; }
        public IRelayCommand ExtraRubAdCommand { get; }
        public IRelayCommand DoubleGoldAdCommand { get; }
        public IRelayCommand[] AttackCommands { get; }
        private int _seenDealSerial = -1;

        public void NotifyDealReady()
        {
            ShowTableButtons.Value = true;
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
                }
            }

            var run = Session.Run;
            var boss = GameBalance.IsBossStage(run.Stage);
            Title.Value = boss
                ? $"第{run.Stage}关 BOSS {GameBalance.AffixName(run.Affix)}"
                : $"第{run.Stage}关";
            Hint.Value = Session.Hint ?? string.Empty;
            GoldText.Value = $"金币 {run.Gold}";
            PotText.Value = $"奖池 {Session.Pot}";
            PlayerChips.Value = $"勇气 {Session.Player.Courage}";
            PlayerBet.Value = BetLabel(Session.Player);
            PlayerState.Value = SeatLine(Session.Player);
            RoundInfo.Value = $"已下注:{Session.Pot}";
            BetAmount.Value = string.Empty;
            var canAct = !Session.Player.Folded && !Session.AiActing;
            var choosing = Session.Phase == GamePhase.WaitingLookChoice && canAct;
            var betting = Session.Phase == GamePhase.Betting && canAct;
            var rubbing = Session.Phase == GamePhase.WaitingRub && canAct;
            var looked = Session.Player.Looked;
            var streetBet = betting || rubbing;
            var callCost = Session.PlayerCallCost;
            var raiseLowCost = Session.CostToReach(Session.RaiseLowUnits);
            var raiseHighCost = Session.CostToReach(Session.RaiseHighUnits);
            BlindLabel.Value = choosing && !looked
                ? "闷注"
                : AmountLabel("跟注", callCost);
            RaiseLabel.Value = AmountLabel("x2下注", raiseLowCost);
            RaiseHighLabel.Value = AmountLabel("x4下注", raiseHighCost);
            AllInLabel.Value = AmountLabel("全部下注", Session.Player.Hp);
            PeekGoodLabel.Value = $"搓牌 {Session.Run.PeekGoodCharges}";
            ChaKanGoodLabel.Value = $"透视 {Session.Run.ChaKanGoodCharges}";
            TiHuanGoodLabel.Value = $"替换 {Session.Run.TiHuanGoodCharges}";
            ShowLook.Value = Session.PlayerMayLookCards;
            ShowBlind.Value = choosing || streetBet;
            ShowActions.Value = streetBet;
            ShowRub.Value = false;
            ShowCancel.Value = Session.PlayerMayCancelLookOrRub;
            ShowAllIn.Value = streetBet;
            ShowFold.Value = ShowTableButtons.Value &&
                             !Session.Player.Folded &&
                             (Session.Phase == GamePhase.WaitingLookChoice ||
                              Session.Phase == GamePhase.WaitingRub ||
                              Session.Phase == GamePhase.Betting);
            ShowCompare.Value = betting && Session.PlayerMayCompare;
            ShowActionBar.Value = choosing || betting || rubbing || ShowFold.Value;
            ShowContinue.Value = Session.Phase == GamePhase.RoundSettle ||
                                 (Session.Phase == GamePhase.WaitingAttack && !Session.AttackPlaying);
            ShowShop.Value = Session.Phase == GamePhase.Shop;
            ShowFail.Value = Session.Phase == GamePhase.StageFail;
            TryPresentFailPopup();
            TryPresentShopPopup();
            ShowAttack.Value = Session.Phase == GamePhase.WaitingAttack || Session.SelectingOpenTarget;
            if (!Session.AttackPlaying)
            {
                ShowMask.Value = false;
                ShowHpText.Value = false;
            }
            RefreshEnemies();

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

        protected override Task OnOpen(object args)
        {
            Session.Changed -= Refresh;
            Session.Changed += Refresh;
            Refresh();
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Session.Changed -= Refresh;
        }

        private async void TryPresentFailPopup()
        {
            if (!IsOpen || Session.Phase != GamePhase.StageFail || _failPopupOpen || _ui == null)
            {
                return;
            }

            _failPopupOpen = true;
            try
            {
                while (IsOpen && Session.Phase == GamePhase.StageFail)
                {
                    var registration = _ui.Registry.GetByViewModelType(typeof(BattleFailPopupViewModel));
                    var popup = (BattleFailPopupViewModel)_ui.Registry.CreateViewModel(registration);
                    var result = await _ui.Dialogs.ShowCustomAsync<BattleFailPopupViewModel, BattleFailResult>(popup);
                    if (result == BattleFailResult.Abandon)
                    {
                        await LeaveToHome();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
            }
            finally
            {
                _failPopupOpen = false;
            }
        }

        private async void TryPresentShopPopup()
        {
            if (!IsOpen || Session.Phase != GamePhase.Shop || _shopPopupOpen || _ui == null)
            {
                return;
            }

            _shopPopupOpen = true;
            try
            {
                while (IsOpen && Session.Phase == GamePhase.Shop)
                {
                    var registration = _ui.Registry.GetByViewModelType(typeof(BattleShopPopViewModel));
                    var popup = (BattleShopPopViewModel)_ui.Registry.CreateViewModel(registration);
                    await _ui.Dialogs.ShowCustomAsync<BattleShopPopViewModel, bool>(popup);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
            }
            finally
            {
                _shopPopupOpen = false;
            }
        }

        private async Task LeaveToHome()
        {
            await _ui.Close(this);
            var home = (HomeViewModel)_ui.Registry.CreateViewModel(
                _ui.Registry.GetByViewModelType(typeof(HomeViewModel)));
            await _ui.Open(home);
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

            for (var i = 0; i < ShowEnemy.Length; i++)
            {
                ShowEnemy[i].Value = false;
                EnemyChips[i].Value = string.Empty;
                EnemyBet[i].Value = string.Empty;
                EnemyState[i].Value = string.Empty;
            }

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

                ShowEnemy[slot].Value = true;
                EnemyChips[slot].Value = $"勇气 {enemy.Courage}";
                EnemyBet[slot].Value = BetLabel(enemy);
                EnemyState[slot].Value = SeatLine(enemy);
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
            if (activeCount <= 1)
            {
                return 1;
            }

            if (activeCount == 2)
            {
                return enemyIndex == 0 ? 0 : 2;
            }

            return enemyIndex;
        }
    }
}
