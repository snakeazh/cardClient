using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 抖音侧边栏奖励弹窗。注册键（均为 Button）：Mask（点击关闭）/ Receive（领取）。
    /// 奖励数量节点按固定路径 BG/RewardArea/Num 查找；Title/Tip 为预制体静态文案。
    /// </summary>
    [AutoScreen(AppScreenIds.TTRewardPop, UILayer.Popup, ResResourcePaths.TTRewardPop)]
    public sealed class TTRewardPopView : ViewBase<TTRewardPopViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindCommand(UI.Get<UnityEngine.UI.Button>("Mask"), ViewModel.CloseCommand);
            Binding.BindCommand(UI.Get<UnityEngine.UI.Button>("Receive"), ViewModel.ReceiveCommand);

            var num = transform.Find("BG/RewardArea/Num");
            var numText = num != null ? num.GetComponent<TMP_Text>() : null;
            if (numText != null)
            {
                Binding.BindText(numText, ViewModel.RewardText);
            }
        }
    }
}
