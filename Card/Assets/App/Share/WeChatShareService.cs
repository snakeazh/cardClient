#if (WEIXINMINIGAME || PLATFORM_WEIXINMINIGAME) && !UNITY_EDITOR
using System;
using Framework.Log;
using WeChatWASM;

namespace App.Share
{
    /// <summary>
    /// 微信小游戏主动转发：<c>wx.shareAppMessage</c> 直接拉起「选择聊天」转发面板。
    ///
    /// 微信的主动转发接口没有成功回调（官方文档 `wx.shareAppMessage` 参数只有
    /// title/imageUrl/query/imageUrlId/toCurrentGroup/path，没有 success/fail），
    /// 因此这里按转发面板的进出前后台判断：面板把游戏切到后台（<c>wx.onHide</c>）后
    /// 回到前台（<c>wx.onShow</c>）即视为分享完成；用户取消同样会回到前台，
    /// 平台层面无法区分，这是微信侧的能力限制（详见模块文档「分享完成判定」）。
    /// </summary>
    public sealed class WeChatShareService : ShareServiceBase
    {
        private bool _leftForeground;
        private bool _listening;
        private DateTime _shareStartedUtc;

        /// <summary>转发面板最长等待时间：用户可能停在联系人选择页很久。</summary>
        protected override float TimeoutSeconds => 180f;

        public override bool IsAvailable => true;

        protected override void BeginShare(ShareContent content)
        {
            _leftForeground = false;
            _shareStartedUtc = DateTime.UtcNow;
            ListenLifecycle();

            WX.ShareAppMessage(new ShareAppMessageOption
            {
                title = string.IsNullOrEmpty(content.Title) ? null : content.Title,
                imageUrl = string.IsNullOrEmpty(content.ImageUrl) ? null : content.ImageUrl,
                query = string.IsNullOrEmpty(content.Query) ? null : content.Query
            });
        }

        protected override void OnShareFinished()
        {
            StopListening();
        }

        private void ListenLifecycle()
        {
            if (_listening)
            {
                return;
            }

            _listening = true;
            WX.OnHide(HandleHide);
            WX.OnShow(HandleShow);
        }

        private void StopListening()
        {
            if (!_listening)
            {
                return;
            }

            _listening = false;
            WX.OffHide(HandleHide);
            WX.OffShow(HandleShow);
        }

        private void HandleHide(GeneralCallbackResult result)
        {
            if (IsSharing)
            {
                _leftForeground = true;
            }
        }

        private void HandleShow(OnShowListenerResult result)
        {
            if (!IsSharing)
            {
                StopListening();
                return;
            }

            // 正常路径：转发面板让游戏离开前台，回来后即完成。
            // 兜底：个别基础库版本转发面板不触发 onHide，回到前台时距拉起已超过 3 秒也按完成处理。
            var awaySeconds = (DateTime.UtcNow - _shareStartedUtc).TotalSeconds;
            if (_leftForeground || awaySeconds >= 3f)
            {
                AppLog.Info(LogChannel.UI, $"[Share] 微信转发返回，按分享完成处理（away={awaySeconds:0.0}s）");
                Complete(new ShareResult(ShareStatus.Completed));
            }
        }
    }
}
#endif
