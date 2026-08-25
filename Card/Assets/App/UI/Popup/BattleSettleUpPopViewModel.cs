using System.Threading.Tasks;
using App.Game;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 关卡结算：只统计本关。当前关积分 / 总积分都是本关 Stage，金币与提现是本关掉落。
    /// </summary>
    public sealed class BattleSettleUpPopViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;

        public BattleSettleUpPopViewModel(GameSession session, IDialogService dialogs)
        {
            Session = session;
            _dialogs = dialogs;
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
            var stageScore = Session.Score.Stage;
            var stageGold = Session.ShopGoldGranted;
            CurScoreNum.Value = stageScore.ToString();
            TotalScoreNum.Value = stageScore.ToString();
            CoinNum.Value = stageGold.ToString();
            WithdrawNum.Value = stageGold.ToString();
            GoldText.Value = Session.Run.Gold.ToString();
        }

        private void Continue()
        {
            _ = _dialogs.CloseWithResult(true);
        }
    }
}
