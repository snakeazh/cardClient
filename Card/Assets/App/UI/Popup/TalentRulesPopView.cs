using System.Threading.Tasks;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋规则说明弹窗。注册在 TopMost 层：叠加在天赋列表之上。DetailedInformation
    /// 注入 GameConst.TalentDesc 文本（'|' 已转行），点 Mask 关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentRulesPop, UILayer.TopMost, ResResourcePaths.TalentRulesPop)]
    public sealed class TalentRulesPopView : ViewBase<TalentRulesPopViewModel>
    {
        protected override void OnBind()
        {
            // 预制体优先经 UIReference 取 DetailedInformation 的 TMP；缺引用时退回按名查找。
            if (!UI.TryGet<TMP_Text>("DetailedInformation", out var detail))
            {
                detail = transform.Find("DetailedInformation")?.GetComponent<TMP_Text>();
            }

            if (detail != null)
            {
                Binding.BindText(detail, ViewModel.DescText);
            }

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
