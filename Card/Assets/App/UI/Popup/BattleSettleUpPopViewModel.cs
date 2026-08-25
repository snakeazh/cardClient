using System.Threading.Tasks;
using App.Game;
using App.Score;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 关卡结算：当前关积分、总积分、本关金币、每回合积分、可提现金额。
    /// </summary>
    public sealed class BattleSettleUpPopViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private readonly IScoreService _score;

        public BattleSettleUpPopViewModel(GameSession session, IDialogService dialogs, IScoreService score)
        {
            Session = session;
            _dialogs = dialogs;
            _score = score;
            CurScoreNum = new ObservableProperty<string>("0");
            TotalScoreNum = new ObservableProperty<string>("0");
            CoinNum = new ObservableProperty<string>("0");
            WithdrawNum = new ObservableProperty<string>("0");
            GoldText = new ObservableProperty<string>("0");
            ContinueCommand = new RelayCommand(Continue);
        }

        public GameSession Session { get; }

        public ObservableProperty<string> CurScoreNum { get; }

        public ObservableProperty<string> TotalScoreNum { get; }

        public ObservableProperty<string> CoinNum { get; }

        public ObservableProperty<string> WithdrawNum { get; }

        public ObservableProperty<string> GoldText { get; }

        public IRelayCommand ContinueCommand { get; }

        protected override Task OnOpen(object args)
        {
            Refresh();
            return Task.CompletedTask;
        }

        private void Refresh()
        {
            var snap = Session.Score;
            CurScoreNum.Value = snap.Stage.ToString();
            TotalScoreNum.Value = snap.Total.ToString();
            CoinNum.Value = Session.ShopGoldGranted.ToString();
            WithdrawNum.Value = (_score != null ? _score.CollectableGold : 0).ToString();
            GoldText.Value = Session.Run.Gold.ToString();
        }

        private void Continue()
        {
            _ = _dialogs.CloseWithResult(true);
        }
    }
}
