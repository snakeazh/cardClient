using System.Threading.Tasks;
using App.Item;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋详情弹窗。注册在 TopMost 层：叠加在 Popup 层的天赋列表之上，弹出时不隐藏列表。
    /// Item 卡显示天赋名，Tip/Detail 显示等级与描述，LeftBtn/RightBtn 翻等级，点 Mask 关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentDetail, UILayer.TopMost, ResResourcePaths.TalentDetail)]
    public sealed class TalentDetailView : ViewBase<TalentDetailViewModel>
    {
        protected override void OnBind()
        {
            var talent = ViewModel.Talent;
            var card = UI.GetGameObject("Item").GetComponent<ItemCard>();
            if (card != null)
            {
                card.SetShadowVisible(false);
                card.SetAnimationEnabled(false);
                card.Bind(talent != null ? talent.Name : null, null, true);
            }

            Binding.BindText(UI.GetGameObject("Tip").GetComponent<TMP_Text>(), ViewModel.TipText);
            Binding.BindText(UI.GetGameObject("Detail").GetComponent<TMP_Text>(), ViewModel.DescText);
            Binding.BindCommand(UI.GetGameObject("LeftBtn").GetComponent<Button>(), ViewModel.PrevCommand);
            Binding.BindCommand(UI.GetGameObject("RightBtn").GetComponent<Button>(), ViewModel.NextCommand);
            BindMaskClose();
        }

        private void BindMaskClose()
        {
            // 预制体优先经 UIReference 挂 Mask 的 Button；缺引用时退回按名查找并运行时补 Button。
            if (!UI.TryGet<Button>("Mask", out var overlay))
            {
                var mask = transform.Find("Mask");
                if (mask == null)
                {
                    return;
                }

                overlay = mask.GetComponent<Button>();
                if (overlay == null)
                {
                    overlay = mask.gameObject.AddComponent<Button>();
                    overlay.transition = Selectable.Transition.None;
                }
            }

            Binding.BindCommand(overlay, ViewModel.CloseCommand);
        }
    }
}
