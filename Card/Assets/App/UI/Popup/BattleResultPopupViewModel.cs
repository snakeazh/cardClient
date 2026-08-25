using System.Threading.Tasks;
using App.Game;
using App.Score;
using App.Wallet;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    public enum BattleResultAction
    {
        Back = 0,
        Again = 1
    }

    /// <summary>
    /// 闯关结算：成功 / 失败两套节点。coinNum 为局外货币，积分按 10:1 兑换资金。
    /// </summary>
    public sealed class BattleResultPopupViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private readonly IWalletService _wallet;
        private bool _granted;

        public BattleResultPopupViewModel(
            GameSession session,
            IDialogService dialogs,
            IWalletService wallet)
        {
            Session = session;
            _dialogs = dialogs;
            _wallet = wallet;
            var success = session.Phase == GamePhase.RunComplete;
            ShowSuccess = new ObservableProperty<bool>(success);
            ShowFail = new ObservableProperty<bool>(!success);
            ShowAgain = new ObservableProperty<bool>(!success);
            CoinNum = new ObservableProperty<string>("0");
            BackCommand = new RelayCommand(Back);
            AgainCommand = new RelayCommand(Again, () => ShowAgain.Value);
            CloseCommand = new RelayCommand(Back);
        }

        public GameSession Session { get; }

        public ObservableProperty<bool> ShowSuccess { get; }

        public ObservableProperty<bool> ShowFail { get; }

        public ObservableProperty<bool> ShowAgain { get; }

        public ObservableProperty<string> CoinNum { get; }

        public IRelayCommand BackCommand { get; }

        public IRelayCommand AgainCommand { get; }

        public IRelayCommand CloseCommand { get; }

        protected override Task OnOpen(object args)
        {
            var success = Session.Phase == GamePhase.RunComplete;
            ShowSuccess.Value = success;
            ShowFail.Value = !success;
            ShowAgain.Value = !success;
            var funds = ScoreBalance.PointsToGold(Session.Score.Total);
            CoinNum.Value = funds.ToString();
            GrantOutGameGold(funds);
            AgainCommand.RaiseCanExecuteChanged();
            return Task.CompletedTask;
        }

        private void GrantOutGameGold(int funds)
        {
            if (_granted || funds <= 0 || _wallet == null)
            {
                return;
            }

            _wallet.Add(funds);
            _granted = true;
        }

        private void Back()
        {
            _ = _dialogs.CloseWithResult(BattleResultAction.Back);
        }

        private void Again()
        {
            if (!ShowAgain.Value)
            {
                return;
            }

            _ = _dialogs.CloseWithResult(BattleResultAction.Again);
        }
    }
}
