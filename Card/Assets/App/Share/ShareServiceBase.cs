using System;
using System.Threading.Tasks;
using Framework.Log;

namespace App.Share
{
    /// <summary>
    /// 分享状态机：防重入、可用性检查、异常兜底、超时收口。
    /// 平台实现只需负责拉起分享面板，并在 SDK 回调里调用 <see cref="Complete"/>。
    /// </summary>
    public abstract class ShareServiceBase : IShareService
    {
        private TaskCompletionSource<ShareResult> _pending;

        public bool IsSharing { get; private set; }

        public abstract bool IsAvailable { get; }

        /// <summary>等待平台回调的最长秒数，超时按取消处理（&lt;= 0 表示不设超时）。</summary>
        protected virtual float TimeoutSeconds => 60f;

        public Task<ShareResult> ShareAsync(ShareContent content)
        {
            if (IsSharing)
            {
                return Task.FromResult(new ShareResult(ShareStatus.Busy));
            }

            if (!IsAvailable)
            {
                return Task.FromResult(new ShareResult(ShareStatus.Unavailable));
            }

            IsSharing = true;
            _pending = new TaskCompletionSource<ShareResult>();
            var task = _pending.Task;
            try
            {
                BeginShare(content);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, GetType().Name + " share failed: " + ex.Message);
                Complete(new ShareResult(ShareStatus.Failed, 0, ex.Message));
            }

            ArmTimeout(_pending, TimeoutSeconds);
            return task;
        }

        /// <summary>拉起一次分享面板，结果通过 <see cref="Complete"/> 回传。无分享能力的实现可留空。</summary>
        protected virtual void BeginShare(ShareContent content)
        {
        }

        /// <summary>本次分享出结果后的清理钩子（如注销平台事件监听）。</summary>
        protected virtual void OnShareFinished()
        {
        }

        /// <summary>SDK 回调收口；非分享期收到的回调直接忽略，不会误判下一次分享。</summary>
        protected void Complete(ShareResult result)
        {
            Complete(_pending, result);
        }

        private void Complete(TaskCompletionSource<ShareResult> pending, ShareResult result)
        {
            if (pending == null || pending.Task.IsCompleted)
            {
                return;
            }

            IsSharing = false;
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }

            try
            {
                OnShareFinished();
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, GetType().Name + " share cleanup failed: " + ex.Message);
            }

            pending.TrySetResult(result);
        }

        /// <summary>超时兜底：面板一直不回来时按取消收口，避免调用方一直等。</summary>
        private async void ArmTimeout(TaskCompletionSource<ShareResult> pending, float seconds)
        {
            if (pending == null || seconds <= 0f)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (pending.Task.IsCompleted)
            {
                return;
            }

            AppLog.Warn(LogChannel.UI, GetType().Name + " share timeout");
            Complete(pending, new ShareResult(ShareStatus.Cancelled, 0, "share timeout"));
        }
    }
}
