using System.Threading.Tasks;

namespace App.Ad
{
    /// <summary>本地模拟广告：直接视为完整看完，用于尚未接入真实广告 SDK 的平台构建（当前含微信包）。</summary>
    public sealed class SimulatedAdService : IAdService
    {
        public bool IsPlaying => false;

        public bool IsAvailable => true;

        public Task<AdWatchResult> ShowRewardedVideoAsync()
        {
            return Task.FromResult(new AdWatchResult(AdWatchStatus.Completed));
        }
    }
}
