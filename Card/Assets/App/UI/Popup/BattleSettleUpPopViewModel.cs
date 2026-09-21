using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;
using App.Level;
using Framework.Assets;
using Framework.UI;
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
    /// 金币先暂扣在资源栏外，点提现/双倍立刻关页，飞币落到金币图标时数字才涨。
    /// </summary>
    public sealed class BattleSettleUpPopViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private readonly List<SettleRoundRow> _roundRows = new List<SettleRoundRow>();
        private bool _coinFxOwnsGold;

        public BattleSettleUpPopViewModel(
            GameSession session,
            IDialogService dialogs,
            IUIManager ui,
            IResourceService resources)
        {
            Session = session;
            _dialogs = dialogs;
            Ui = ui;
            Resources = resources;
            CurScoreNum = new ObservableProperty<string>("0");
            TotalScoreNum = new ObservableProperty<string>("0");
            CoinNum = new ObservableProperty<string>("0");
            WithdrawNum = new ObservableProperty<string>("0");
            GoldText = new ObservableProperty<string>("0");
            FormulaText = new ObservableProperty<string>(string.Empty);
            FormulaSkillText = new ObservableProperty<string>(string.Empty);
            RoundRevision = new ObservableProperty<int>();
            ButtonsEnabled = new ObservableProperty<bool>(true);
            DoubleEnabled = new ObservableProperty<bool>(false);
            DoubleCommand = new RelayCommand(DoubleWithdraw, CanDouble);
        }

        public GameSession Session { get; }

        public IUIManager Ui { get; }

        public IResourceService Resources { get; }

        /// <summary>怪物总数（关卡配置的出场怪物数）。</summary>
        public ObservableProperty<string> CurScoreNum { get; }

        /// <summary>总伤害（本关 Stage 积分）。</summary>
        public ObservableProperty<string> TotalScoreNum { get; }

        /// <summary>基础奖励（本关掉落金币）。</summary>
        public ObservableProperty<string> CoinNum { get; }

        public ObservableProperty<string> WithdrawNum { get; }

        public ObservableProperty<string> GoldText { get; }

        /// <summary>换算说明：每 N 点伤害 = M 金币（局内伤害换金，读 GameConst.DamageTurnToGold）。</summary>
        public ObservableProperty<string> FormulaText { get; }

        /// <summary>换算说明：每剩余 1 次技能 = N 金币（结算时未使用技能折算，读 GameConst.EverySkillProvideGold）。</summary>
        public ObservableProperty<string> FormulaSkillText { get; }

        /// <summary>逐回合统计（回合数 / 击杀数 / 伤害）。</summary>
        public IReadOnlyList<SettleRoundRow> RoundRows => _roundRows;

        /// <summary>行数据版本号，Refresh 后自增，驱动 View 重建行列表。</summary>
        public ObservableProperty<int> RoundRevision { get; }

        public ObservableProperty<bool> ButtonsEnabled { get; }

        public ObservableProperty<bool> DoubleEnabled { get; }

        /// <summary>看广告双倍提现（GameSession 内含每日限次与已双倍保护）。</summary>
        public IRelayCommand DoubleCommand { get; }

        protected override Task OnOpen(object args)
        {
            Refresh();
            return Task.CompletedTask;
        }

        public void SetBusy(bool busy)
        {
            ButtonsEnabled.Value = !busy;
            RefreshDoubleEnabled();
            DoubleCommand.RaiseCanExecuteChanged();
        }

        public void CompleteWithdraw(GameResourceViewModel bar, bool coinFxOwnsGold = false)
        {
            _coinFxOwnsGold = coinFxOwnsGold;
            if (!coinFxOwnsGold)
            {
                bar?.ReleaseHeldGold();
            }

            Close();
        }

        public bool TryBeginDouble(GameResourceViewModel bar, out int extra)
        {
            extra = 0;
            // View 会先 SetBusy 防连点，这里不能再看 ButtonsEnabled，否则双倍会当场失败。
            if (!Session.CanWatchAdDoubleGold())
            {
                return false;
            }

            extra = Session.ShopGoldGranted;
            if (extra > 0)
            {
                bar?.HoldGold(extra, refresh: false);
            }

            Session.WatchAdDoubleGold();
            Refresh();
            return true;
        }

        public void CompleteDouble(GameResourceViewModel bar, int extra, bool coinFxOwnsGold = false)
        {
            _coinFxOwnsGold = coinFxOwnsGold;
            if (!coinFxOwnsGold && extra > 0)
            {
                bar?.ReleaseHeldGold(extra);
            }

            Close();
        }

        protected override Task OnClose()
        {
            if (_coinFxOwnsGold)
            {
                return Task.CompletedTask;
            }

            var view = GameResourceView.FindOpen();
            view?.ViewModel?.ReleaseHeldGold();
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
            FormulaText.Value = FormatDamageGoldFormula();
            FormulaSkillText.Value = FormatSkillGoldFormula();
            RefreshDoubleEnabled();

            _roundRows.Clear();
            var scores = Session.StageRoundScores;
            var kills = Session.StageRoundKills;
            var directDamages = Session.StageDirectKillDamages;
            // 行数取积分/击杀的较大者：直接击杀（无牌局积分）只产生击杀行，不能按积分数截断丢行。
            var rowCount = scores.Count > kills.Count ? scores.Count : kills.Count;
            for (var i = 0; i < rowCount; i++)
            {
                var damage = i < scores.Count ? scores[i] : 0;
                if (i < directDamages.Count)
                {
                    damage += directDamages[i];
                }

                _roundRows.Add(new SettleRoundRow
                {
                    Round = i + 1,
                    Kills = i < kills.Count ? kills[i] : 0,
                    Damage = damage
                });
            }

            RoundRevision.Value++;
        }

        /// <summary>伤害换金说明文案。比例 [a, b] 即每 a 点伤害 = b 金币，与 GameSession.DamageToGold 同源同钳制。</summary>
        private static string FormatDamageGoldFormula()
        {
            var rate = GameConst.IsLoaded ? GameConst.Instance.DamageTurnToGold : null;
            if (rate == null || rate.Length < 2 || rate[0] <= 0)
            {
                return string.Empty;
            }

            return $"每{rate[0]}点伤害={Math.Max(0, rate[1])}";
        }

        /// <summary>技能换金说明文案。与 GameSession.GrantStageGold 的未用技能折算同源同钳制。</summary>
        private static string FormatSkillGoldFormula()
        {
            var value = GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.EverySkillProvideGold) : 0;
            return $"每剩余1技能={value}";
        }

        private static int ResolveMonsterCount()
        {
            var level = AppServices.IsReady ? AppServices.Resolve<ILevelService>() : null;
            return level?.Current?.Monsters.Count ?? 0;
        }

        private bool CanDouble()
        {
            return ButtonsEnabled.Value && Session.CanWatchAdDoubleGold();
        }

        private void RefreshDoubleEnabled()
        {
            DoubleEnabled.Value = CanDouble();
        }

        private void DoubleWithdraw()
        {
            // View 拦截 DoubleBtn 点击并播飞币；命令仅用于 CanExecute。
        }

        private void Close()
        {
            _ = _dialogs.CloseWithResult(true);
        }
    }
}
