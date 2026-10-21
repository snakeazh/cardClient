#if UNITY_EDITOR
namespace App.Ad
{
    /// <summary>
    /// 编辑器模拟广告：弹出 <see cref="EditorAdOverlay"/> 面板；
    /// 点「领取奖励」返回 Completed，点右上角「关闭」返回 ClosedEarly。
    /// </summary>
    public sealed class EditorSimulatedAdService : AdServiceBase
    {
        public override bool IsAvailable => true;

        protected override void ShowRewardedVideo()
        {
            EditorAdOverlay.Show(
                () => Complete(new AdWatchResult(AdWatchStatus.Completed)),
                () => Complete(new AdWatchResult(AdWatchStatus.ClosedEarly)));
        }
    }
}
#endif
