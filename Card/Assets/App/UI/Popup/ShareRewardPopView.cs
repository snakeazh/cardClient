using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 分享奖励弹窗。注册键（均为 Button）：Mask（点击关闭）/ Receive（ShareBtn，点击分享）。
    /// Title/Tip 为预制体静态文案。
    /// </summary>
    [AutoScreen(AppScreenIds.ShareRewardPop, UILayer.Popup, ResResourcePaths.ShareRewardPop)]
    public sealed class ShareRewardPopView : ViewBase<ShareRewardPopViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindCommand(UI.Get<Button>("Mask"), ViewModel.CloseCommand);
            Binding.BindCommand(UI.Get<Button>("Receive"), ViewModel.ShareCommand);
        }
    }
}
