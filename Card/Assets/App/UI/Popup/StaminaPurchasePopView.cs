using App.Resources;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 广告商店弹窗。节点通过 UIReference / UIBind 解析：Mask / BuyStamina / BuyCoin（均为 Button）。
    /// 卡内文案节点（Tip 限购、Icon 下 X数量、MaskArea 下 Num 按钮）按固定路径查找。
    /// </summary>
    [AutoScreen(AppScreenIds.StaminaPurchasePop, UILayer.Popup, ResResourcePaths.StaminaPurchasePop)]
    public sealed class StaminaPurchasePopView : ViewBase<StaminaPurchasePopViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindCommand(UI.Get<Button>("Mask"), ViewModel.CloseCommand);

            BindCard(
                "BuyStamina",
                ViewModel.BuyStaminaCommand,
                ViewModel.StaminaAmountText,
                ViewModel.StaminaLimitText,
                ViewModel.StaminaBtnText);
            BindCard(
                "BuyCoin",
                ViewModel.BuyCoinCommand,
                ViewModel.CoinAmountText,
                ViewModel.CoinLimitText,
                ViewModel.CoinBtnText);
        }

        private void BindCard(
            string key,
            IRelayCommand command,
            ObservableProperty<string> amount,
            ObservableProperty<string> limit,
            ObservableProperty<string> btnText)
        {
            var card = UI.GetGameObject(key).transform;
            var button = card.GetComponent<Button>();
            if (button != null)
            {
                Binding.BindCommand(button, command);
            }

            var amountText = FindTmp(card, "Icon/Text (TMP)");
            if (amountText != null)
            {
                Binding.BindText(amountText, amount);
            }

            var limitText = FindTmp(card, "Tip");
            if (limitText != null)
            {
                Binding.BindText(limitText, limit);
            }

            var btnTextTmp = FindTmp(card, "MaskArea/Num");
            if (btnTextTmp != null)
            {
                Binding.BindText(btnTextTmp, btnText);
            }
        }

        private static TMP_Text FindTmp(Transform root, string path)
        {
            var child = root.Find(path);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }
    }
}
