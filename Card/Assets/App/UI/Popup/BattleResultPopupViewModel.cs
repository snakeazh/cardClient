using System.Collections.Generic;
using System.Threading.Tasks;
using App.Game;
using App.Net;
using App.Score;
using App.UI;
using App.Wallet;
using CardShare.Contracts;
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

    /// <summary>逐关金币行：关卡号 / 该关积分换算出的金币（与总金币同源同口径）。</summary>
    public sealed class ResultStageRow
    {
        public int Stage { get; set; }

        public int Gold { get; set; }
    }

    /// <summary>
    /// 闯关结算：成功 / 失败两套节点。coinNum 为局外货币（显示在逐关明细最底部），
    /// 积分按 10:1 兑换资金；StageRows 逐关拆分同一口径（每关积分各自换算，逐关向下取整，
    /// 行合计可能比总数少几枚）。
    /// 失败时 AgainBtn 为广告复活；局内主动退出以 forfeitNoRevive 打开则隐藏复活。
    /// 放弃或通关才兑入局外货币。
    /// </summary>
    public sealed class BattleResultPopupViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private readonly IWalletService _wallet;
        private readonly List<ResultStageRow> _stageRows = new List<ResultStageRow>();
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
            StageRevision = new ObservableProperty<int>();
            BackCommand = new RelayCommand(Back);
            AgainCommand = new RelayCommand(Again, CanRevive);
        }

        public GameSession Session { get; }

        public ObservableProperty<bool> ShowSuccess { get; }

        public ObservableProperty<bool> ShowFail { get; }

        public ObservableProperty<bool> ShowAgain { get; }

        public ObservableProperty<string> CoinNum { get; }

        /// <summary>逐关金币明细（自上而下），最底部的总数即 <see cref="CoinNum"/>。</summary>
        public IReadOnlyList<ResultStageRow> StageRows => _stageRows;

        /// <summary>明细行版本号，OnOpen 重建后自增，驱动 View 克隆行列表。</summary>
        public ObservableProperty<int> StageRevision { get; }

        public IRelayCommand BackCommand { get; }

        public IRelayCommand AgainCommand { get; }

        protected override async Task OnOpen(object args)
        {
            _forfeitNoRevive = args is bool forfeit && forfeit;
            var success = !_forfeitNoRevive && Session.Phase == GamePhase.RunComplete;
            ShowSuccess.Value = success;
            ShowFail.Value = !success;
            ShowAgain.Value = CanRevive();
            var funds = ScoreBalance.PointsToGold(Session.Score.Total);
            CoinNum.Value = funds.ToString();
            RefreshStageRows();
            if (success)
            {
                await SettleAsync(cleared: true);
            }

            AgainCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 已结束关卡来自 Session.StageScores；最后一行是当前关（通关最后一关不走 StartStage、
        /// 失败或中途放弃时积分仍在 Score.Stage）。弹窗只在局结束时打开，当前关必然打过，
        /// 0 分（如直杀通关）也占一行，否则行区会出现空洞。
        /// </summary>
        private void RefreshStageRows()
        {
            _stageRows.Clear();
            var records = Session.StageScores;
            for (var i = 0; i < records.Count; i++)
            {
                _stageRows.Add(new ResultStageRow
                {
                    Stage = records[i].Stage,
                    Gold = ScoreBalance.PointsToGold(records[i].Score)
                });
            }

            _stageRows.Add(new ResultStageRow
            {
                Stage = Session.Run.Stage,
                Gold = ScoreBalance.PointsToGold(Session.Score.Stage)
            });

            StageRevision.Value++;
        }

        private bool CanRevive()
        {
            return !_forfeitNoRevive &&
                   Session.Phase == GamePhase.StageFail &&
                   Session.Run.AdsReviveThisStage < 1;
        }

        private async Task SettleAsync(bool cleared)
        {
            if (_granted || !Session.HasServerRun)
            {
                return;
            }

            try
            {
                var resp = await GameApi.Client.SettlePveAsync(Session.BuildSettleRequest(cleared));
                GameApi.ApplyProfile(resp.Profile);
                CoinNum.Value = resp.GoldGranted.ToString();
                _granted = true;
            }
            catch (GameApiException ex)
            {
                Toast.Error(GameApi.Describe(ex));
            }
        }

        private async void Back()
        {
            if (!_granted)
            {
                await SettleAsync(cleared: ShowSuccess.Value);
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
