using System.Threading.Tasks;
using App.Item;
using App.Resources;
using DG.Tweening;
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
    /// 失败时 Tip 改为提示文案；点 Mask 关闭。出售时金币从 SellBtn 飞入 GameResourceBar。
    /// </summary>
    [AutoScreen(AppScreenIds.ShopDetail, UILayer.TopMost, ResResourcePaths.ShopDetail)]
    public sealed class ShopDetailView : ViewBase<ShopDetailViewModel>
    {
        private ItemCard _card;
        private Button _sellBtn;
        private GameObject _coinPrefab;
        private Sequence _coinSeq;
        private int _playToken;

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
            BindSellButton();
            BindVideoBuyButton();
            BindMaskClose();
            _ = EnsureCoinPrefab();
        }

        protected override Task OnViewClose()
        {
            KillCoinFx();
            if (_sellBtn != null)
            {
                _sellBtn.onClick.RemoveListener(OnSellClicked);
            }

            return Task.CompletedTask;
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

        private void BindSellButton()
        {
            var go = UI.GetGameObject("SellBtn");
            Binding.BindActive(go, ViewModel.ShowSell);
            _sellBtn = go.GetComponent<Button>();
            if (_sellBtn != null)
            {
                _sellBtn.onClick.AddListener(OnSellClicked);
                Binding.BindInteractable(_sellBtn, ViewModel.ButtonsEnabled);
            }

            Binding.BindText(UI.GetGameObject("SellNum").GetComponent<TMP_Text>(), ViewModel.PriceText);
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

        private async Task EnsureCoinPrefab()
        {
            if (_coinPrefab != null || ViewModel?.Resources == null)
            {
                return;
            }

            if (ViewModel.Resources.TryGetCached<GameObject>(ResResourcePaths.CoinItem, out var cached) && cached != null)
            {
                _coinPrefab = cached;
                return;
            }

            _coinPrefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.CoinItem);
        }

        private async void OnSellClicked()
        {
            if (ViewModel == null || !ViewModel.ButtonsEnabled.Value)
            {
                return;
            }

            ViewModel.SetBusy(true);
            await EnsureCoinPrefab();
            if (ViewModel == null)
            {
                return;
            }

            var bar = GameResourceView.FindOpen()?.ViewModel;
            if (!ViewModel.TryBeginSell(bar, out var gold))
            {
                ViewModel.SetBusy(false);
                return;
            }

            if (gold <= 0 || !TryPlayCoinFly(_sellBtn, () => ViewModel.CompleteSell(bar, gold)))
            {
                ViewModel.CompleteSell(bar, gold);
            }
        }

        private bool TryPlayCoinFly(Button source, System.Action onArrived)
        {
            var from = source != null ? source.transform as RectTransform : null;
            var to = CoinFlyFx.FindGoldIcon();
            var parent = ResolveFxParent();
            if (from == null || to == null || parent == null || _coinPrefab == null)
            {
                return false;
            }

            var token = ++_playToken;
            KillCoinFx(false);
            _coinSeq = CoinFlyFx.Play(
                _coinPrefab,
                parent,
                from.position,
                to.position,
                () =>
                {
                    if (token != _playToken || ViewModel == null)
                    {
                        return;
                    }

                    _coinSeq = null;
                    ViewModel.SetBusy(false);
                    onArrived?.Invoke();
                });
            return _coinSeq != null;
        }

        private RectTransform ResolveFxParent()
        {
            var root = ViewModel?.Ui?.Root;
            if (root != null)
            {
                // ShopDetail 在 TopMost，金币必须挂同层才能盖过详情遮罩。
                return root.GetLayer(UILayer.TopMost);
            }

            return transform as RectTransform;
        }

        private void KillCoinFx(bool bumpToken = true)
        {
            if (bumpToken)
            {
                _playToken++;
            }

            if (_coinSeq != null && _coinSeq.IsActive())
            {
                _coinSeq.Kill();
            }

            _coinSeq = null;
        }
    }
}
