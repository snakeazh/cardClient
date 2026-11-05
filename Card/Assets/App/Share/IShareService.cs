using System.Threading.Tasks;

namespace App.Share
{
    /// <summary>一次分享拉起的结束状态。</summary>
    public enum ShareStatus
    {
        /// <summary>平台确认分享完成，可以发奖。</summary>
        Completed = 0,

        /// <summary>用户取消，或分享面板未真正完成，不发放奖励。</summary>
        Cancelled = 1,

        /// <summary>拉起分享或 SDK 回调异常，不发放奖励。</summary>
        Failed = 2,

        /// <summary>上一次分享尚未结束，本次调用被忽略。</summary>
        Busy = 3,

        /// <summary>当前环境没有分享能力（非小游戏平台）。</summary>
        Unavailable = 4
    }

    /// <summary>分享结果。仅 <see cref="IsCompleted"/> 为 true 时允许发奖。</summary>
    public readonly struct ShareResult
    {
        public ShareResult(ShareStatus status, int errorCode = 0, string errorMessage = null)
        {
            Status = status;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public ShareStatus Status { get; }

        /// <summary>SDK 错误码，仅 Failed 时有意义。</summary>
        public int ErrorCode { get; }

        /// <summary>SDK 错误描述，仅 Failed 时有意义。</summary>
        public string ErrorMessage { get; }

        public bool IsCompleted => Status == ShareStatus.Completed;
    }

    /// <summary>分享卡片内容。图片留空时由平台使用游戏画面截图。</summary>
    public readonly struct ShareContent
    {
        public ShareContent(string title, string imageUrl = null, string query = null)
        {
            Title = title ?? string.Empty;
            ImageUrl = imageUrl ?? string.Empty;
            Query = query ?? string.Empty;
        }

        /// <summary>分享标题。</summary>
        public string Title { get; }

        /// <summary>分享图片地址（网络图 / 本地图 / 代码包内相对路径），留空用默认截图。</summary>
        public string ImageUrl { get; }

        /// <summary>透传给进入方启动参数的 query，形如 key1=val1&amp;key2=val2。</summary>
        public string Query { get; }
    }

    /// <summary>
    /// 小游戏分享链路：只负责「主动拉起一次分享并返回用户是否分享完成」，不含任何业务发奖逻辑。
    /// 调用方在 <see cref="ShareResult.IsCompleted"/> 为 true 后再执行自己的校验与发奖。
    /// 平台差异（微信/抖音）全部在实现内部处理，业务方不要引用具体实现。
    /// </summary>
    public interface IShareService
    {
        /// <summary>是否有分享面板正在等待结果，用于调用方防连点。</summary>
        bool IsSharing { get; }

        /// <summary>当前环境是否具备分享能力（编辑器模拟环境恒 true）。</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 拉起一次分享。结果回调到达前 Task 不完成；同时只允许一次分享，重入返回 Busy。
        /// 面板一直不返回时由实现内部超时兜底，不会让调用方任务悬挂。
        /// </summary>
        Task<ShareResult> ShareAsync(ShareContent content);
    }
}
