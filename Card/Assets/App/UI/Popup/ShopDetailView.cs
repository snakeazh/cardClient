using System.Threading.Tasks;
using App.Item;
using App.Resources;
using DG.Tweening;
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
    /// 金币不足时 BuyNum 变红；失败弹 Toast，不改底部 Tip。点 Mask 关闭。
    /// 购买成功后商品卡飞入 BattleShopPop 的 MineHor；出售时金币从 SellBtn 飞入 GameResourceBar。
    /// </summary>
    [AutoScreen(AppScreenIds.ShopDetail, UILayer.TopMost, ResResourcePaths.ShopDetail)]
    public sealed class ShopDetailView : ViewBase<ShopDetailViewModel>
    {
        private ItemCard _card;
        private Button _buyBtn;
        private Button _videoBuyBtn;
        private Button _sellBtn;
        private GameObject _coinPrefab;
        private Sequence _coinSeq;
        private Sequence _itemSeq;
        private CanvasGroup _overlayGroup;
        private int _playToken;
        private int _pendingRevealRelicId;

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
            BindBuyButton();
            BindSellButton();
            BindVideoBuyButton();
            BindMaskClose();
            _ = EnsureCoinPrefab();
        }

        protected override Task OnViewClose()
        {
            KillCoinFx();
            KillItemFx();
            RestoreOverlay();
            RevealPendingMine();
            if (_buyBtn != null)
            {
                _buyBtn.onClick.RemoveListener(OnBuyClicked);
            }

            if (_videoBuyBtn != null)
            {
                _videoBuyBtn.onClick.RemoveListener(OnVideoBuyClicked);
            }

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

        private void BindBuyButton()
        {
            var go = UI.GetGameObject("BuyBtn");
            Binding.BindActive(go, ViewModel.ShowBuy);
            _buyBtn = go.GetComponent<Button>();
            if (_buyBtn != null)
            {
                _buyBtn.onClick.AddListener(OnBuyClicked);
                Binding.BindInteractable(_buyBtn, ViewModel.ButtonsEnabled);
            }

            var priceText = UI.GetGameObject("BuyNum").GetComponent<TMP_Text>();
            Binding.BindText(priceText, ViewModel.PriceText);
            BindBuyPriceColor(priceText);
        }

        private void BindBuyPriceColor(TMP_Text priceText)
        {
            if (priceText == null)
            {
                return;
            }

            var normal = priceText.color;
            Binding.Add(ViewModel.CanAffordBuy.Subscribe(
                affordable => priceText.color = affordable ? normal : ThemeColors.ShopPriceUnaffordable,
                emitCurrent: true));
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
            _videoBuyBtn = go.GetComponent<Button>();
            if (_videoBuyBtn != null)
            {
                _videoBuyBtn.onClick.AddListener(OnVideoBuyClicked);
                Binding.BindInteractable(_videoBuyBtn, ViewModel.ButtonsEnabled);
            }
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

        private void OnBuyClicked()
        {
            BeginBuy(watchAd: false);
        }

        private void OnVideoBuyClicked()
        {
            BeginBuy(watchAd: true);
        }

        private void BeginBuy(bool watchAd)
        {
            if (ViewModel == null || !ViewModel.ButtonsEnabled.Value)
            {
                return;
            }

            ViewModel.SetBusy(true);
            var relicId = ViewModel.RelicId;
            var shop = BattleShopPopView.FindOpen();
            shop?.HoldIncomingMine(relicId);
            if (!ViewModel.TryBeginBuy(watchAd))
            {
                shop?.ClearIncomingMine();
                ViewModel.SetBusy(false);
                return;
            }

            _pendingRevealRelicId = relicId;
            if (!TryPlayItemFly(shop, relicId))
            {
                FinishBuyFly(shop, relicId);
            }
        }

        private bool TryPlayItemFly(BattleShopPopView shop, int relicId)
        {
            var source = _card != null ? _card.transform as RectTransform : null;
            var dest = shop != null ? shop.GetIncomingMineRect(relicId) : null;
            var parent = ResolveFxParent();
            if (source == null || dest == null || parent == null)
            {
                return false;
            }

            var token = ++_playToken;
            KillItemFx(false);
            _itemSeq = ItemFlyFx.Play(
                source,
                parent,
                dest,
                () =>
                {
                    if (token != _playToken || ViewModel == null)
                    {
                        return;
                    }

                    _itemSeq = null;
                    FinishBuyFly(shop, relicId);
                });
            if (_itemSeq == null)
            {
                return false;
            }

            HideDetailOverlay();
            return true;
        }

        private void FinishBuyFly(BattleShopPopView shop, int relicId)
        {
            shop?.RevealIncomingMine(relicId);
            _pendingRevealRelicId = 0;
            if (ViewModel == null)
            {
                return;
            }

            ViewModel.SetBusy(false);
            ViewModel.CompleteBuy();
        }

        private void HideDetailOverlay()
        {
            if (_overlayGroup == null)
            {
                _overlayGroup = GetComponent<CanvasGroup>();
                if (_overlayGroup == null)
                {
                    _overlayGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            _overlayGroup.alpha = 0f;
            _overlayGroup.blocksRaycasts = false;
            _overlayGroup.interactable = false;
        }

        private void RestoreOverlay()
        {
            if (_overlayGroup == null)
            {
                return;
            }

            _overlayGroup.alpha = 1f;
            _overlayGroup.blocksRaycasts = true;
            _overlayGroup.interactable = true;
        }

        private void RevealPendingMine()
        {
            if (_pendingRevealRelicId <= 0)
            {
                return;
            }

            var relicId = _pendingRevealRelicId;
            _pendingRevealRelicId = 0;
            BattleShopPopView.FindOpen()?.RevealIncomingMine(relicId);
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

        private void KillItemFx(bool bumpToken = true)
        {
            if (bumpToken)
            {
                _playToken++;
            }

            if (_itemSeq != null && _itemSeq.IsActive())
            {
                _itemSeq.Kill();
            }

            _itemSeq = null;
        }
    }
}
