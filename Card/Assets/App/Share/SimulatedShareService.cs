namespace App.Share
{
    /// <summary>
    /// 无分享能力的平台（PC / 安卓母包等）：<see cref="IsAvailable"/> 恒 false，
    /// 调用方会拿到 Unavailable 并隐藏入口，不会走到拉起分享。
    /// </summary>
    public sealed class SimulatedShareService : ShareServiceBase
    {
        public override bool IsAvailable => false;
    }
}
