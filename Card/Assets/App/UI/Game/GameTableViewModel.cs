using App.Game;
using Framework.Assets;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    /// <summary>把 <see cref="GameSession"/> 的状态绑到桌面 HUD：下注、看牌、商店、攻击。</summary>
    public sealed class GameTableViewModel : ViewModelBase
    {
        public GameTableViewModel(GameSession session, IResourceService resources)
        {
            Session = session;
            Resources = resources;
            Session.Changed += Refresh;
            BlindBetCommand = new RelayCommand(
                () => Session.BlindBet(),
                () => Session.Phase == GamePhase.Betting || Session.Phase == GamePhase.WaitingLookChoice);
            RaiseCommand = new RelayCommand(() => Session.RaiseBet(), () => Session.Phase == GamePhase.Betting);
            RaiseHighCommand = new RelayCommand(() => Session.RaiseBetHigh(), () => Session.Phase == GamePhase.Betting);
            LookCommand = new RelayCommand(() => Session.LookCards(), () => Session.PlayerMayLookCards);
            FoldCommand = new RelayCommand(() => Session.Fold(), () => Session.Phase == GamePhase.Betting);
            OpenCommand = new RelayCommand(
                () => Session.RequestShowdown(),
                () => Session.PlayerMayCompare);
            AllInCommand = new RelayCommand(() => Session.AllIn(), () => Session.PlayerMayAllIn);
            PeekGoodCommand = new RelayCommand(() => Session.UsePeekGood(), () => Session.PlayerMayUsePeekGood);
            ChaKanGoodCommand = new RelayCommand(() => Session.UseChaKanGood(), () => Session.PlayerMayUseChaKanGood);
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
        public ObservableProperty<bool> ShowActionBar { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowLook { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowRub { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowCancel { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowBlind { get; } = new ObservableProperty<bool>();
        public ObservableProperty<string> BlindLabel { get; } = new ObservableProperty<string>("闷注");
        public ObservableProperty<string> RaiseLabel { get; } = new ObservableProperty<string>("加注");
        public ObservableProperty<string> RaiseHighLabel { get; } = new ObservableProperty<string>("加注×3");
        public ObservableProperty<bool> ShowAllIn { get; } = new ObservableProperty<bool>();
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
            PlayerChips.Value = $"HP {Session.Player.Hp}";
            PlayerBet.Value = BetLabel(Session.Player);
            PlayerState.Value = SeatLine(Session.Player);
            RoundInfo.Value = $"已下注:{Session.Pot}";
            BetAmount.Value = string.Empty;
            var choosing = Session.Phase == GamePhase.WaitingLookChoice && !Session.Player.Folded;
            var betting = Session.Phase == GamePhase.Betting && !Session.Player.Folded;
            var callCost = Session.PlayerCallCost;
            BlindLabel.Value = choosing
                ? "闷注"
                : $"跟注{(callCost > 0 ? callCost : Session.CurrentCallUnits)}";
            RaiseLabel.Value = "加注";
            RaiseHighLabel.Value = "加注×3";
            ShowLook.Value = choosing && Session.PlayerMayLookCards;
            ShowBlind.Value = choosing || betting;
            ShowActions.Value = betting;
            ShowRub.Value = Session.Phase == GamePhase.WaitingRub;
            ShowCancel.Value = Session.PlayerMayCancelLookOrRub;
            ShowAllIn.Value = betting;
            ShowCompare.Value = betting && Session.PlayerMayCompare;
            ShowActionBar.Value = choosing || betting || ShowRub.Value;
            ShowContinue.Value = Session.Phase == GamePhase.RoundSettle ||
                                 (Session.Phase == GamePhase.WaitingAttack && !Session.AttackPlaying);
            ShowShop.Value = Session.Phase == GamePhase.Shop;
            ShowFail.Value = Session.Phase == GamePhase.StageFail;
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

        protected override void OnDispose()
        {
            Session.Changed -= Refresh;
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
                EnemyChips[slot].Value = $"HP {enemy.Hp}";
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
