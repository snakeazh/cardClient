using App.Item;
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
    /// 商店商品详情。注册在 TopMost 层：叠加在 Popup 层的商店之上。
    /// Item 卡显示遗物名与图标，Detail 显示描述；购买或出售二选一，购买时另显示 VideoBuyBtn 看广告免费拿；
    /// 失败时 Tip 改为提示文案；点 Mask 关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.ShopDetail, UILayer.TopMost, ResResourcePaths.ShopDetail)]
    public sealed class ShopDetailView : ViewBase<ShopDetailViewModel>
    {
        private ItemCard _card;

        protected override void OnBind()
        {
            _card = UI.GetGameObject("Item").GetComponent<ItemCard>();
            if (_card != null)
            {
                _card.SetShadowVisible(false);
                _card.SetAnimationEnabled(false);
                _card.SetUnlocked(true);
                Binding.Add(ViewModel.NameText.Subscribe(_card.SetName));
                Binding.Add(ViewModel.IconSprite.Subscribe(ApplyIcon, emitCurrent: true));
                Binding.Add(ViewModel.Quality.Subscribe(_card.ApplyQuality, emitCurrent: true));
            }

            Binding.BindText(UI.GetGameObject("Detail").GetComponent<TMP_Text>(), ViewModel.DescText);
            Binding.BindText(UI.GetGameObject("Tip").GetComponent<TMP_Text>(), ViewModel.TipText);
            BindActionButton("BuyBtn", "BuyNum", ViewModel.ShowBuy, ViewModel.BuyCommand, ViewModel.PriceText);
            BindActionButton("SellBtn", "SellNum", ViewModel.ShowSell, ViewModel.SellCommand, ViewModel.PriceText);
            BindVideoBuyButton();
            BindMaskClose();
        }

        private void ApplyIcon(Sprite sprite)
        {
            if (_card == null)
            {
                return;
            }

            _card.SetIcon(sprite);
        }

        private void BindActionButton(
            string buttonKey,
            string priceKey,
            ObservableProperty<bool> visible,
            IRelayCommand command,
            ObservableProperty<string> price)
        {
            var go = UI.GetGameObject(buttonKey);
            Binding.BindActive(go, visible);
            Binding.BindCommand(go.GetComponent<Button>(), command);
            Binding.BindText(UI.GetGameObject(priceKey).GetComponent<TMP_Text>(), price);
        }

        private void BindVideoBuyButton()
        {
            var go = UI.GetGameObject("VideoBuyBtn");
            Binding.BindActive(go, ViewModel.ShowBuy);
            Binding.BindCommand(go.GetComponent<Button>(), ViewModel.VideoBuyCommand);
        }

        private void BindMaskClose()
        {
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
