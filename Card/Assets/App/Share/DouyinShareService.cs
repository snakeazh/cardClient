#if DOUYINMINIGAME && !UNITY_EDITOR
using System;
using Framework.Log;
using TTSDK;

namespace App.Share
{
    /// <summary>
    /// 抖音小游戏主动分享：<c>TT.ShareAppMessage(ShareParam)</c> 拉起抖音分享面板。
    ///
    /// TTSDK 的 C# 分享封装（`ShareParam`：Title/ImageUrl/Query/Extra）不对外暴露
    /// 分享结果回调，因此与微信侧同一口径：分享面板把游戏切到后台后回到前台即视为完成。
    ///
    /// 适配点：若项目接入的 TTSDK 版本在 `ShareParam` 上提供 Success/Cancel 回调
    /// （对应 JS 侧 `tt.shareAppMessage` 的 success/cancel），把 <see cref="BeginShare"/>
    /// 换成「传回调 + 回调里 Complete」即可，其余状态机逻辑不用动。
    /// </summary>
    public sealed class DouyinShareService : ShareServiceBase
    {
        private bool _leftForeground;
        private bool _lifecycleHooked;
        private DateTime _shareStartedUtc;

        /// <summary>分享面板最长等待时间。</summary>
        protected override float TimeoutSeconds => 180f;

        public override bool IsAvailable => true;

        protected override void BeginShare(ShareContent content)
        {
            _leftForeground = false;
            _shareStartedUtc = DateTime.UtcNow;
            HookLifecycle();

            TT.ShareAppMessage(new ShareParam
            {
                Title = string.IsNullOrEmpty(content.Title) ? string.Empty : content.Title,
                ImageUrl = string.IsNullOrEmpty(content.ImageUrl) ? string.Empty : content.ImageUrl,
                Query = string.IsNullOrEmpty(content.Query) ? string.Empty : content.Query,
                Extra = "sharereward"
            });
        }

        protected override void OnShareFinished()
        {
            // 生命周期监听常驻（TTSDK 的 OnShow/OnHide 是全局事件），用 IsSharing 过滤即可。
            _leftForeground = false;
        }

        /// <summary>只挂一次全局生命周期：TTSDK 的 OnShow/OnHide 没有安全的一次性注销口径。</summary>
        private void HookLifecycle()
        {
            if (_lifecycleHooked)
            {
                return;
            }

            _lifecycleHooked = true;
            var lifecycle = TT.GetAppLifeCycle();
            if (lifecycle == null)
            {
                AppLog.Warn(LogChannel.UI, "[Share] TTSDK 生命周期不可用，抖音分享完成判定只能等超时");
                return;
            }

            lifecycle.OnHide += HandleHide;
            lifecycle.OnShow += HandleShow;
        }

        private void HandleHide()
        {
            if (IsSharing)
            {
                _leftForeground = true;
            }
        }

        private void HandleShow(object _)
        {
            if (!IsSharing)
            {
                return;
            }

            var awaySeconds = (DateTime.UtcNow - _shareStartedUtc).TotalSeconds;
            if (_leftForeground || awaySeconds >= 3f)
            {
                AppLog.Info(LogChannel.UI, $"[Share] 抖音分享返回，按分享完成处理（away={awaySeconds:0.0}s）");
                Complete(new ShareResult(ShareStatus.Completed));
            }
        }
    }
}
#endif
