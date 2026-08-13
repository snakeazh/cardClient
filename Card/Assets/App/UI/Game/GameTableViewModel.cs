using App.Game;
using Framework.Assets;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    public sealed class GameTableViewModel : ViewModelBase
    {
        public GameTableViewModel(GameSession session, IResourceService resources)
        {
            Session = session;
            Resources = resources;
            Session.Changed += Refresh;
            BlindBetCommand = new RelayCommand(() => Session.BlindBet(), () => Session.Phase == GamePhase.Betting);
            RaiseCommand = new RelayCommand(() => Session.RaiseBet(), () => Session.Phase == GamePhase.Betting);
            LookCommand = new RelayCommand(() => Session.LookCards(), () => Session.PlayerMayLookCards);
            FoldCommand = new RelayCommand(() => Session.Fold(), () => Session.Phase == GamePhase.Betting);
            OpenCommand = new RelayCommand(() => Session.OpenCompare(), () => Session.Phase == GamePhase.Betting);
            MinusBetCommand = new RelayCommand(() => Session.AdjustBetUnits(-GameBalance.MinBet), () => Session.Phase == GamePhase.Betting);
            PlusBetCommand = new RelayCommand(() => Session.AdjustBetUnits(GameBalance.MinBet), () => Session.Phase == GamePhase.Betting);
            ContinueCommand = new RelayCommand(() => Session.Continue(), () => Session.Phase == GamePhase.RoundSettle);
            LeaveShopCommand = new RelayCommand(() => Session.LeaveShop(), () => Session.Phase == GamePhase.Shop);
            LoanCommand = new RelayCommand(() => Session.WatchAdLoan(), () => Session.Phase == GamePhase.StageFail);
            ReviveCommand = new RelayCommand(() => Session.WatchAdRevive(), () => Session.Phase == GamePhase.StageFail);
            RestartCommand = new RelayCommand(() => Session.RestartStage(), () => Session.Phase == GamePhase.StageFail);
            ExtraRubAdCommand = new RelayCommand(() => Session.WatchAdExtraRub());
            DoubleGoldAdCommand = new RelayCommand(() => Session.WatchAdDoubleGold(), () => Session.Phase == GamePhase.Shop);
            Refresh();
        }

        public GameSession Session { get; }
        public IResourceService Resources { get; }

        public ObservableProperty<string> Title { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> Hint { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> GoldText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PotText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerChips { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerBet { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerState { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> BetAmount { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> LogText { get; } = new ObservableProperty<string>();
        public ObservableProperty<bool> ShowActions { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowContinue { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowShop { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowFail { get; } = new ObservableProperty<bool>();

        public IRelayCommand BlindBetCommand { get; }
        public IRelayCommand RaiseCommand { get; }
        public IRelayCommand LookCommand { get; }
        public IRelayCommand FoldCommand { get; }
        public IRelayCommand OpenCommand { get; }
        public IRelayCommand MinusBetCommand { get; }
        public IRelayCommand PlusBetCommand { get; }
        public IRelayCommand ContinueCommand { get; }
        public IRelayCommand LeaveShopCommand { get; }
        public IRelayCommand LoanCommand { get; }
        public IRelayCommand ReviveCommand { get; }
        public IRelayCommand RestartCommand { get; }
        public IRelayCommand ExtraRubAdCommand { get; }
        public IRelayCommand DoubleGoldAdCommand { get; }

        public void Refresh()
        {
            var run = Session.Run;
            var boss = GameBalance.IsBossStage(run.Stage);
            Title.Value = boss
                ? $"第{run.Stage}关 BOSS {GameBalance.AffixName(run.Affix)}"
                : $"第{run.Stage}关";
            Hint.Value = Session.Hint ?? string.Empty;
            GoldText.Value = $"金币 {run.Gold}";
            PotText.Value = $"奖池 {Session.Pot}";
            PlayerChips.Value = $"筹码({Session.Player.Chips})";
            PlayerBet.Value = Session.Player.Looked
                ? $"看牌下注（{Session.BetUnits * 2}）"
                : $"下注（{Session.BetUnits}）";
            PlayerState.Value = string.IsNullOrEmpty(Session.Player.Status) ? PhaseLabel(Session.Phase) : Session.Player.Status;
            BetAmount.Value = Session.BetUnits.ToString();
            ShowActions.Value = Session.Phase == GamePhase.Betting;
            ShowContinue.Value = Session.Phase == GamePhase.RoundSettle;
            ShowShop.Value = Session.Phase == GamePhase.Shop;
            ShowFail.Value = Session.Phase == GamePhase.StageFail;

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
            LookCommand.RaiseCanExecuteChanged();
            FoldCommand.RaiseCanExecuteChanged();
            OpenCommand.RaiseCanExecuteChanged();
            ContinueCommand.RaiseCanExecuteChanged();
            LeaveShopCommand.RaiseCanExecuteChanged();
            LoanCommand.RaiseCanExecuteChanged();
            ReviveCommand.RaiseCanExecuteChanged();
            RestartCommand.RaiseCanExecuteChanged();
            DoubleGoldAdCommand.RaiseCanExecuteChanged();
        }

        protected override void OnDispose()
        {
            Session.Changed -= Refresh;
        }

        private static string PhaseLabel(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.WaitingRub: return "请搓牌";
                case GamePhase.Betting: return "下注中";
                case GamePhase.Showdown: return "比牌";
                case GamePhase.RoundSettle: return "结算";
                case GamePhase.Shop: return "商店";
                case GamePhase.StageFail: return "失败";
                default: return string.Empty;
            }
        }
    }
}
