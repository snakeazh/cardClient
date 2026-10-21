#if DOUYINMINIGAME && !UNITY_EDITOR
using System;
using Framework.Log;
using TTSDK;

namespace App.Ad
{
    /// <summary>抖音激励视频：Stark SDK 真实播放，首次使用时懒创建单例，关闭后预加载下一条。</summary>
    public sealed class DouyinAdService : AdServiceBase
    {
        private TTRewardedVideoAd _ad;
        private bool _createFailed;

        public override bool IsAvailable =>
            !_createFailed && !string.IsNullOrWhiteSpace(AdSettings.DouyinRewardedVideoAdUnitId);

        protected override void ShowRewardedVideo()
        {
            if (!EnsureAdCreated())
            {
                Complete(new AdWatchResult(AdWatchStatus.Unavailable));
                return;
            }

            _ad.Show();
        }

        private bool EnsureAdCreated()
        {
            if (_ad != null)
            {
                return true;
            }

            try
            {
                _ad = TT.CreateRewardedVideoAd(AdSettings.DouyinRewardedVideoAdUnitId, HandleClose, HandleError);
                if (_ad == null)
                {
                    _createFailed = true;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _createFailed = true;
                AppLog.Warn(LogChannel.Net, "Douyin rewarded video create failed: " + ex.Message);
                return false;
            }
        }

        private void HandleClose(bool isComplete, int count)
        {
            try
            {
                _ad?.Load();
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "Douyin rewarded video preload failed: " + ex.Message);
            }

            Complete(new AdWatchResult(isComplete ? AdWatchStatus.Completed : AdWatchStatus.ClosedEarly));
        }

        private void HandleError(int errCode, string errMsg)
        {
            AppLog.Warn(LogChannel.Net, $"Douyin rewarded video error: {errCode} {errMsg}");
            Complete(new AdWatchResult(AdWatchStatus.Failed, errCode, errMsg));
        }
    }
}
#endif
