using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 体力不足弹窗。节点通过 UIReference / UIBind 解析：Info / AdInfo / AdBtn / CloseBtn。
    /// </summary>
    [AutoScreen(AppScreenIds.EnergyPopup, UILayer.Popup, ResResourcePaths.EnergyPopup)]
    public sealed class EnergyPopupView : ViewBase<EnergyPopupViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindText(UI.Get<TMP_Text>("Info"), ViewModel.Info);
            Binding.BindText(UI.Get<TMP_Text>("AdInfo"), ViewModel.AdInfo);

            var adButton = UI.Get<Button>("AdBtn");
            Binding.BindCommand(adButton, ViewModel.AdCommand);
            Binding.BindActive(adButton.gameObject, ViewModel.ShowAdBtn);
            Binding.BindCommand(UI.Get<Button>("CloseBtn"), ViewModel.CloseCommand);
        }
    }
}
