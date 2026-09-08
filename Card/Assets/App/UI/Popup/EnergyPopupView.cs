using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 体力不足弹窗（已改走 CommonTop，本页保留未接入）。
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
