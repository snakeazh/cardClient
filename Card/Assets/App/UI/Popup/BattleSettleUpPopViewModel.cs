using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using App.Game;
using App.Level;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>逐回合统计行：回合数 / 击杀数 / 伤害。</summary>
    public sealed class SettleRoundRow
    {
        public int Round { get; set; }

        public int Kills { get; set; }

        public int Damage { get; set; }
    }

    /// <summary>
    /// 关卡结算：只统计本关。总伤害是本关 Stage 积分，怪物总数取关卡配置，
    /// 基础奖励 / 提现是本关掉落金币，双倍提现走 <see cref="GameSession.WatchAdDoubleGold"/>。
    /// </summary>
    public sealed class BattleSettleUpPopViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private readonly List<SettleRoundRow> _roundRows = new List<SettleRoundRow>();

        public BattleSettleUpPopViewModel(GameSession session, IDialogService dialogs)
        {
            Session = session;
            _dialogs = dialogs;
            CurScoreNum = new ObservableProperty<string>("0");
            TotalScoreNum = new ObservableProperty<string>("0");
            CoinNum = new ObservableProperty<string>("0");
            WithdrawNum = new ObservableProperty<string>("0");
            GoldText = new ObservableProperty<string>("0");
            FormulaText = new ObservableProperty<string>(string.Empty);
            RoundRevision = new ObservableProperty<int>();
            ContinueCommand = new RelayCommand(Continue);
            DoubleCommand = new RelayCommand(DoubleWithdraw);
        }

        public GameSession Session { get; }

        /// <summary>怪物总数（关卡配置的出场怪物数）。</summary>
        public ObservableProperty<string> CurScoreNum { get; }

        /// <summary>总伤害（本关 Stage 积分）。</summary>
        public ObservableProperty<string> TotalScoreNum { get; }

        /// <summary>基础奖励（本关掉落金币）。</summary>
        public ObservableProperty<string> CoinNum { get; }

        public ObservableProperty<string> WithdrawNum { get; }

        public ObservableProperty<string> GoldText { get; }

        /// <summary>换算说明：每 N 点伤害 = 1 金币。</summary>
        public ObservableProperty<string> FormulaText { get; }

        /// <summary>逐回合统计（回合数 / 击杀数 / 伤害）。</summary>
        public IReadOnlyList<SettleRoundRow> RoundRows => _roundRows;

        /// <summary>行数据版本号，Refresh 后自增，驱动 View 重建行列表。</summary>
        public ObservableProperty<int> RoundRevision { get; }

        public IRelayCommand ContinueCommand { get; }

        /// <summary>看广告双倍提现（GameSession 内含每日限次与已双倍保护）。</summary>
        public IRelayCommand DoubleCommand { get; }

        protected override Task OnOpen(object args)
        {
            Refresh();
            return Task.CompletedTask;
        }

        private void Refresh()
        {
            var stageScore = Session.Score.Stage;
            var stageGold = Session.ShopGoldGranted;
            CurScoreNum.Value = ResolveMonsterCount().ToString();
            TotalScoreNum.Value = stageScore.ToString();
            CoinNum.Value = stageGold.ToString();
            WithdrawNum.Value = stageGold.ToString();
            GoldText.Value = Session.Run.Gold.ToString();
            FormulaText.Value = "每" + GameConst.Instance.ExchangePointsForGoldCoins + "点伤害=1";

            _roundRows.Clear();
            var scores = Session.StageRoundScores;
            var kills = Session.StageRoundKills;
            for (var i = 0; i < scores.Count; i++)
            {
                _roundRows.Add(new SettleRoundRow
                {
                    Round = i + 1,
                    Kills = i < kills.Count ? kills[i] : 0,
                    Damage = scores[i]
                });
            }

            RoundRevision.Value++;
        }

        private static int ResolveMonsterCount()
        {
            var level = AppServices.IsReady ? AppServices.Resolve<ILevelService>() : null;
            return level?.Current?.Monsters.Count ?? 0;
        }

        private void DoubleWithdraw()
        {
            Session.WatchAdDoubleGold();
            Refresh();
        }

        private void Continue()
        {
            _ = _dialogs.CloseWithResult(true);
        }
    }
}
