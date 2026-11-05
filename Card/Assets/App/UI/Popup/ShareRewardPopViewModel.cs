using System;
using System.Threading.Tasks;
using App.Share;
using App.ShareReward;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 分享奖励弹窗：点 ShareBtn（预制体 key `Receive`）主动拉起小游戏分享面板，
    /// 平台确认分享完成后发放「搓牌使用次数永久 +1」并关闭；取消/失败只提示不关闭，方便重试。
    /// 领奖后首页 ShareRewardItem 由 <see cref="IShareRewardService.Changed"/> 自动隐藏。
    /// </summary>
    public sealed class ShareRewardPopViewModel : ViewModelBase
    {
        private readonly IShareService _share;
        private readonly IShareRewardService _reward;
        private readonly IUIManager _ui;
        private readonly ToastService _toast;
        private bool _sharing;

        public ShareRewardPopViewModel(
            IShareService share,
            IShareRewardService reward,
            IUIManager ui,
            ToastService toast)
        {
            _share = share ?? throw new ArgumentNullException(nameof(share));
            _reward = reward ?? throw new ArgumentNullException(nameof(reward));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _toast = toast;
            ShareCommand = new RelayCommand(Share, () => !_sharing);
            CloseCommand = new RelayCommand(ClosePop);
        }

        /// <summary>拉起分享面板并等待平台结果；分享期间不可重复点击。</summary>
        public IRelayCommand ShareCommand { get; }

        /// <summary>点遮罩空白处关闭（预制体 key `Mask`）。</summary>
        public IRelayCommand CloseCommand { get; }

        protected override Task OnOpen(object args)
        {
            return Task.CompletedTask;
        }

        private async void Share()
        {
            if (_sharing)
            {
                return;
            }

            if (_reward.HasClaimed)
            {
                _toast?.Show("分享奖励已领取");
                ClosePop();
                return;
            }

            _sharing = true;
            ShareCommand.RaiseCanExecuteChanged();
            try
            {
                var content = new ShareContent(
                    ShareRewardBalance.ShareTitle,
                    ShareRewardBalance.ShareImageUrl,
                    ShareRewardBalance.ShareQuery);
                var result = await _share.ShareAsync(content);
                HandleResult(result);
            }
            catch (Exception ex)
            {
                // async void 吞异常会让按钮“点了没反应”：记录并提示重试。
                AppLog.Exception(LogChannel.UI, ex);
                _toast?.ShowError("分享失败，请稍后重试");
            }
            finally
            {
                _sharing = false;
                ShareCommand.RaiseCanExecuteChanged();
            }
        }

        private void HandleResult(ShareResult result)
        {
            switch (result.Status)
            {
                case ShareStatus.Completed:
                    if (_reward.TryClaim())
                    {
                        _toast?.ShowSuccess($"分享成功，搓牌次数永久 +{ShareRewardBalance.RubBonusCharges}");
                    }

                    ClosePop();
                    break;

                case ShareStatus.Cancelled:
                    _toast?.ShowWarning("分享未完成，请重新分享");
                    break;

                case ShareStatus.Unavailable:
                    _toast?.ShowWarning("当前平台暂不支持分享");
                    ClosePop();
                    break;

                case ShareStatus.Busy:
                    // 平台分享进行中，忽略本次点击。
                    break;

                default:
                    AppLog.Warn(LogChannel.UI, $"[Share] 分享失败：{result.ErrorCode} {result.ErrorMessage}");
                    _toast?.ShowError("分享失败，请稍后重试");
                    break;
            }
        }

        private async void ClosePop()
        {
            if (!IsOpen)
            {
                return;
            }

            try
            {
                await _ui.Close(this);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }
    }
}
