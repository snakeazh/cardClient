using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>竞技场规则弹窗。</summary>
    [AutoScreen(AppScreenIds.PvpRulePop, UILayer.Popup, ResResourcePaths.PvpRulePop)]
    public sealed class PvpRulePopView : ViewBase<PvpRulePopViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindCommand(UI.Get<Button>("ConfirmBtn"), ViewModel.CloseCommand);
        }
    }
}
