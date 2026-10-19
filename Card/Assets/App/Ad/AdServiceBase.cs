using System;
using System.Threading.Tasks;
using Framework.Log;

namespace App.Ad
{
    /// <summary>
    /// 激励视频播放状态机：防重入、可用性检查、异常兜底与结果收口。
    /// 平台实现只需负责触发播放，并在 SDK 回调中调用 <see cref="Complete"/>。
    /// </summary>
    public abstract class AdServiceBase : IAdService
    {
        private TaskCompletionSource<AdWatchResult> _pending;

        public bool IsPlaying { get; private set; }

        public abstract bool IsAvailable { get; }

        public Task<AdWatchResult> ShowRewardedVideoAsync()
        {
            if (IsPlaying)
            {
                return Task.FromResult(new AdWatchResult(AdWatchStatus.Busy));
            }

            if (!IsAvailable)
            {
                return Task.FromResult(new AdWatchResult(AdWatchStatus.Unavailable));
            }

            IsPlaying = true;
            _pending = new TaskCompletionSource<AdWatchResult>();
            var task = _pending.Task;
            try
            {
                ShowRewardedVideo();
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, GetType().Name + " rewarded video show failed: " + ex.Message);
                Complete(new AdWatchResult(AdWatchStatus.Failed, 0, ex.Message));
            }

            return task;
        }

        /// <summary>触发一次激励视频播放，结果通过 <see cref="Complete"/> 回传。</summary>
        protected abstract void ShowRewardedVideo();

        /// <summary>SDK 回调收口；非播放期收到的回调只复位状态，不产生结果。</summary>
        protected void Complete(AdWatchResult result)
        {
            IsPlaying = false;
            var pending = _pending;
            _pending = null;
            pending?.TrySetResult(result);
        }
    }
}
