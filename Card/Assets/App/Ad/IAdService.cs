using System.Threading.Tasks;

namespace App.Ad
{
    /// <summary>激励视频一次播放的结束状态。</summary>
    public enum AdWatchStatus
    {
        /// <summary>完整看完，可以发放奖励。</summary>
        Completed = 0,

        /// <summary>用户中途关闭，不发放奖励。</summary>
        ClosedEarly = 1,

        /// <summary>广告加载或播放失败，不发放奖励。</summary>
        Failed = 2,

        /// <summary>上一次播放尚未结束，本次调用被忽略。</summary>
        Busy = 3,

        /// <summary>当前环境无可用广告（未配置广告位 ID 或创建失败）。</summary>
        Unavailable = 4
    }

    /// <summary>激励视频播放结果。仅 <see cref="Rewarded"/> 为 true 时允许发奖。</summary>
    public readonly struct AdWatchResult
    {
        public AdWatchResult(AdWatchStatus status, int errorCode = 0, string errorMessage = null)
        {
            Status = status;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public AdWatchStatus Status { get; }

        /// <summary>SDK 错误码，仅 Failed 时有意义。</summary>
        public int ErrorCode { get; }

        /// <summary>SDK 错误描述，仅 Failed 时有意义。</summary>
        public string ErrorMessage { get; }

        public bool Rewarded => Status == AdWatchStatus.Completed;
    }

    /// <summary>
    /// 广告链路接口：只负责「播放一次激励视频并返回是否应发奖」，不含任何业务发奖逻辑。
    /// 调用方在 Rewarded 为 true 后再执行原有校验与发奖；其余状态按 Status 自行提示。
    /// </summary>
    public interface IAdService
    {
        /// <summary>是否有广告正在播放，用于调用方防连点。</summary>
        bool IsPlaying { get; }

        /// <summary>当前环境广告是否可用（本地模拟环境恒 true）。</summary>
        bool IsAvailable { get; }

        /// <summary>播放一次激励视频，回调结果到达前 Task 不完成；同时只允许一次播放，重入返回 Busy。</summary>
        Task<AdWatchResult> ShowRewardedVideoAsync();
    }
}
