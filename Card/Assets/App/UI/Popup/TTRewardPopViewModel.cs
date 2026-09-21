using System;
using System.Threading.Tasks;
using App.TTReward;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 抖音侧边栏奖励弹窗：展示限量金币礼盒，点领取发放并关闭；未从侧边栏进入时提示引导。
    /// 领取状态每日重置（ITTRewardService）。
    /// </summary>
    public sealed class TTRewardPopViewModel : ViewModelBase
    {
        private readonly ITTRewardService _reward;
        private readonly IUIManager _ui;
        private readonly ToastService _toast;

        public TTRewardPopViewModel(
            ITTRewardService reward,
            IUIManager ui,
            ToastService toast)
        {
            _reward = reward ?? throw new ArgumentNullException(nameof(reward));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _toast = toast;
            RewardText = new ObservableProperty<string>(_reward.GoldPerClaim.ToString());
            ReceiveText = new ObservableProperty<string>(GetReceiveText());
            ReceiveCommand = new RelayCommand(Receive, () => _reward.ClaimsLeftToday > 0);
            CloseCommand = new RelayCommand(Close);
        }

        /// <summary>奖励数量文案（BG/RewardArea/Num）。</summary>
        public ObservableProperty<string> RewardText { get; }

        /// <summary>领取按钮文案（BG/Receive/Text）：完成态「立即领奖」，未完成态「进入侧边栏」。</summary>
        public ObservableProperty<string> ReceiveText { get; }

        public IRelayCommand ReceiveCommand { get; }

        public IRelayCommand CloseCommand { get; }

        protected override Task OnOpen(object args)
        {
            _reward.Changed -= OnRewardChanged;
            _reward.Changed += OnRewardChanged;
            RewardText.Value = _reward.GoldPerClaim.ToString();
            ReceiveText.Value = GetReceiveText();
            return Task.CompletedTask;
        }

        protected override Task OnClose()
        {
            _reward.Changed -= OnRewardChanged;
            return Task.CompletedTask;
        }

        private void OnRewardChanged()
        {
            ReceiveText.Value = GetReceiveText();
            ReceiveCommand.RaiseCanExecuteChanged();
        }

        private string GetReceiveText()
        {
            return _reward.IsFromSidebar ? "立即领奖" : "进入侧边栏";
        }

        private void Receive()
        {
            if (!_reward.IsFromSidebar)
            {
                // 未完成态：关闭弹窗并跳转抖音首页侧边栏，从侧边栏回到游戏后再领取。
                _reward.NavigateToSidebar();
                Close();
                return;
            }

            if (_reward.TryClaim())
            {
                _toast?.ShowSuccess($"金币 +{_reward.GoldPerClaim}");
                Close();
            }
        }

        private void Close()
        {
            _ = _ui.Close(this);
        }
    }
}
