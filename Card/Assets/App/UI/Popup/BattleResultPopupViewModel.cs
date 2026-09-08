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
    /// 失败时 AgainBtn 为广告复活；局内主动退出以 forfeitNoRevive 打开则隐藏复活。
    /// 放弃或通关才兑入局外货币。
    /// </summary>
    public sealed class BattleResultPopupViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private readonly IWalletService _wallet;
        private bool _granted;
        private bool _forfeitNoRevive;

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
            ShowAgain = new ObservableProperty<bool>(CanRevive());
            CoinNum = new ObservableProperty<string>("0");
            BackCommand = new RelayCommand(Back);
            AgainCommand = new RelayCommand(Again, CanRevive);
        }

        public GameSession Session { get; }

        public ObservableProperty<bool> ShowSuccess { get; }

        public ObservableProperty<bool> ShowFail { get; }

        public ObservableProperty<bool> ShowAgain { get; }

        public ObservableProperty<string> CoinNum { get; }

        public IRelayCommand BackCommand { get; }

        public IRelayCommand AgainCommand { get; }

        protected override Task OnOpen(object args)
        {
            _forfeitNoRevive = args is bool forfeit && forfeit;
            var success = !_forfeitNoRevive && Session.Phase == GamePhase.RunComplete;
            ShowSuccess.Value = success;
            ShowFail.Value = !success;
            ShowAgain.Value = CanRevive();
            var funds = ScoreBalance.PointsToGold(Session.Score.Total);
            CoinNum.Value = funds.ToString();
            if (success)
            {
                GrantOutGameGold(funds);
            }

            AgainCommand.RaiseCanExecuteChanged();
            return Task.CompletedTask;
        }

        private bool CanRevive()
        {
            return !_forfeitNoRevive &&
                   Session.Phase == GamePhase.StageFail &&
                   Session.Run.AdsReviveThisStage < 1;
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
            if (!_granted)
            {
                GrantOutGameGold(ScoreBalance.PointsToGold(Session.Score.Total));
            }

            _ = _dialogs.CloseWithResult(BattleResultAction.Back);
        }

        private void Again()
        {
            if (!CanRevive())
            {
                ShowAgain.Value = false;
                AgainCommand.RaiseCanExecuteChanged();
                return;
            }

            Session.WatchAdRevive();
            _ = _dialogs.CloseWithResult(BattleResultAction.Again);
        }
    }
}
