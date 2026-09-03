using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 失败弹窗。资源保留，当前阵亡改走 <see cref="BattleResultPopupView"/>，本页暂不弹出。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleFailPopup, UILayer.Popup, ResResourcePaths.BattleFailPopup)]
    public sealed class BattleFailPopupView : ViewBase<BattleFailPopupViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindText(UI.Get<TMP_Text>("TextInfo"), ViewModel.Info);

            var againButton = UI.Get<Button>("AgainBtn");
            Binding.BindCommand(againButton, ViewModel.AgainCommand);
            Binding.BindActive(againButton.gameObject, ViewModel.ShowAgain);
            Binding.BindCommand(UI.Get<Button>("AbandonBtn"), ViewModel.AbandonCommand);
            Binding.BindCommand(UI.Get<Button>("CloseBtn"), ViewModel.CloseCommand);
        }
    }
}
