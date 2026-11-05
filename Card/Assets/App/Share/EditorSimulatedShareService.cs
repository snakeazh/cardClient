#if UNITY_EDITOR
namespace App.Share
{
    /// <summary>
    /// 编辑器模拟分享：弹出 <see cref="EditorShareOverlay"/> 面板；
    /// 点「分享成功」返回 Completed，点「取消分享」返回 Cancelled。
    /// </summary>
    public sealed class EditorSimulatedShareService : ShareServiceBase
    {
        /// <summary>模拟面板由人手动点击，不设超时，避免面板停留太久后结果被判丢。</summary>
        protected override float TimeoutSeconds => 0f;

        public override bool IsAvailable => true;

        protected override void BeginShare(ShareContent content)
        {
            EditorShareOverlay.Show(
                content,
                () => Complete(new ShareResult(ShareStatus.Completed)),
                () => Complete(new ShareResult(ShareStatus.Cancelled)));
        }
    }
}
#endif
