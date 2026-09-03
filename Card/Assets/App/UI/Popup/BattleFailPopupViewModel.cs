using System.Threading.Tasks;
using App.Game;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    public enum BattleFailResult
    {
        Retry = 0,
        Abandon = 1
    }

    /// <summary>
    /// 关卡失败弹窗：再试试（广告复活）或放弃挑战返回主页。当前未接入弹出。
    /// </summary>
    public sealed class BattleFailPopupViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;

        public BattleFailPopupViewModel(GameSession session, IDialogService dialogs)
        {
            Session = session;
            _dialogs = dialogs;
            Info = new ObservableProperty<string>();
            ShowAgain = new ObservableProperty<bool>();
            AgainCommand = new RelayCommand(Retry, CanRetry);
            AbandonCommand = new RelayCommand(Abandon);
            CloseCommand = new RelayCommand(Abandon);
        }

        public GameSession Session { get; }

        public ObservableProperty<string> Info { get; }

        public ObservableProperty<bool> ShowAgain { get; }

        public IRelayCommand AgainCommand { get; }

        public IRelayCommand AbandonCommand { get; }

        public IRelayCommand CloseCommand { get; }

        protected override Task OnOpen(object args)
        {
            Refresh();
            return Task.CompletedTask;
        }

        private void Refresh()
        {
            var canRetry = CanRetry();
            ShowAgain.Value = canRetry;
            Info.Value = canRetry
                ? "你本局还有一次复活机会 可用哦！"
                : "复活次数已用完，可放弃挑战返回主页";
            AgainCommand.RaiseCanExecuteChanged();
        }

        private bool CanRetry()
        {
            return Session.Phase == GamePhase.StageFail && Session.Run.AdsReviveThisStage < 1;
        }

        private void Retry()
        {
            if (!CanRetry())
            {
                Refresh();
                return;
            }

            Session.WatchAdRevive();
            _ = _dialogs.CloseWithResult(BattleFailResult.Retry);
        }

        private void Abandon()
        {
            _ = _dialogs.CloseWithResult(BattleFailResult.Abandon);
        }
    }
}
