using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 闯关结算弹窗。成功 / 失败显示不同立绘与角标；失败时 AgainBtn 广告复活。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleResultPopup, UILayer.Popup, ResResourcePaths.BattleResultPopup)]
    public sealed class BattleResultPopupView : ViewBase<BattleResultPopupViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindActive(UI.GetGameObject("logosuccess"), ViewModel.ShowSuccess);
            Binding.BindActive(UI.GetGameObject("logofail"), ViewModel.ShowFail);
            Binding.BindActive(UI.GetGameObject("logosuccess2"), ViewModel.ShowSuccess);
            Binding.BindActive(UI.GetGameObject("logofail2"), ViewModel.ShowFail);
            Binding.BindText(GetNode<TMP_Text>("coinNum"), ViewModel.CoinNum);

            Binding.BindCommand(GetNode<Button>("BackBtn"), ViewModel.BackCommand);

            var againButton = GetNode<Button>("AgainBtn");
            Binding.BindCommand(againButton, ViewModel.AgainCommand);
            Binding.BindActive(againButton.gameObject, ViewModel.ShowAgain);
        }

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }
    }
}
