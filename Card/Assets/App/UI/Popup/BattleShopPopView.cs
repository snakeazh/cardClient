using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Resources;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 通关商店弹窗。点击看 ItemTip，拖到 buy / Sell 完成购买或出售。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleShopPop, UILayer.Popup, ResResourcePaths.BattleShopPop)]
    public sealed class BattleShopPopView : ViewBase<BattleShopPopViewModel>
    {
        private readonly List<ShopItem> _sellItems = new List<ShopItem>();
        private readonly List<ShopItem> _mineItems = new List<ShopItem>();
        private readonly Dictionary<int, Sprite> _icons = new Dictionary<int, Sprite>();
        private ShopItem _sellTemplate;
        private ShopItem _mineTemplate;
        private RectTransform _sellHor;
        private RectTransform _mineHor;
        private RectTransform _sellDrop;
        private RectTransform _buyDrop;
        private GameObject _tip;
        private TMP_Text _tipText;
        private GameObject _tipCatcher;
        private ShopItem _tipAnchor;
        private ShopItem _tipAnimAnchor;
        private GameObject _ghost;
        private Vector2 _ghostGrabOffset;
        private Canvas _canvas;
        private bool _dragging;
        private readonly Vector3[] _corners = new Vector3[4];

        protected override void OnBind()
        {
            Binding.BindText(GetNode<TMP_Text>("RefreshGooldNum"), ViewModel.RefreshGoldNum);
            Binding.BindText(GetNode<TMP_Text>("buyNum"), ViewModel.BuyNum);
            Binding.BindText(GetNode<TMP_Text>("SellNum"), ViewModel.SellNum);
            Binding.BindCommand(GetNode<Button>("RefreshBtn"), ViewModel.RefreshCommand);
            Binding.BindCommand(GetNode<Button>("NextStageBtn"), ViewModel.NextStageCommand);
            Binding.BindCommand(GetNode<Button>("CloseBtn"), ViewModel.CloseCommand);
            Binding.BindActive(UI.GetGameObject("sellHor"), ViewModel.ShowSellHor);
            Binding.BindActive(UI.GetGameObject("MineHor"), ViewModel.ShowMineHor);
            Binding.BindActive(UI.GetGameObject("buy"), ViewModel.ShowBuy);
            Binding.BindActive(UI.GetGameObject("Sell"), ViewModel.ShowSell);

            _buyDrop = UI.GetGameObject("buy").GetComponent<RectTransform>();
            _sellDrop = UI.GetGameObject("Sell").GetComponent<RectTransform>();
            _sellHor = UI.GetGameObject("sellHor").GetComponent<RectTransform>();
            _mineHor = UI.GetGameObject("MineHor").GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();

            EnsureSellItems();
            EnsureMineItems();
            Binding.Add(ViewModel.ShopRevision.Subscribe(OnShopRevision, emitCurrent: true));
            Binding.Add(ViewModel.ShowTip.Subscribe(_ => ApplyTip(), emitCurrent: true));
            Binding.Add(ViewModel.TipText.Subscribe(OnTipText, emitCurrent: true));
        }

        private void OnShopRevision(int revision)
        {
            _ = ReloadAndRefresh();
        }

        protected override async Task OnViewOpen()
        {
            await LoadOfferIcons();
            await EnsureTip();
            ApplyTip();
        }

        protected override Task OnViewClose()
        {
            _dragging = false;
            DestroyGhost();
            if (_tipCatcher != null)
            {
                _tipCatcher.SetActive(false);
            }
            if (_tip != null)
            {
                _tip.SetActive(false);
            }

            return Task.CompletedTask;
        }

        private async Task ReloadAndRefresh()
        {
            await LoadOfferIcons();
            RefreshItems();
            ApplyTip();
        }

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void EnsureSellItems()
        {
            var root = UI.GetGameObject("sellHor").transform;
            _sellTemplate = root.GetComponentInChildren<ShopItem>(true);
            if (_sellTemplate == null)
            {
                return;
            }

            _sellTemplate.gameObject.SetActive(false);
            _sellItems.Clear();
            for (var i = 0; i < GameBalance.ShopOfferCount; i++)
            {
                _sellItems.Add(CloneItem(_sellTemplate, root, $"sellItem_{i}", OnSellClicked));
            }
        }

        private void EnsureMineItems()
        {
            var root = UI.GetGameObject("MineHor").transform;
            _mineTemplate = root.GetComponentInChildren<ShopItem>(true);
            if (_mineTemplate == null)
            {
                return;
            }

            _mineTemplate.gameObject.SetActive(false);
        }

        private ShopItem CloneItem(ShopItem template, Transform parent, string name, Action<ShopItem> onClick)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(false);
            var bind = go.GetComponent<UIBind>();
            if (bind != null)
            {
                UnityEngine.Object.Destroy(bind);
            }

            var item = go.GetComponent<ShopItem>();
            item.BindClick(onClick);
            item.BindDrag(OnItemBeginDrag, OnItemDrag, OnItemEndDrag);
            return item;
        }

        private void OnSellClicked(ShopItem item)
        {
            if (!TryGetRelicId(item, out var relicId) || _dragging)
            {
                return;
            }

            _tipAnchor = item;
            ViewModel.PreviewShopOffer(relicId);
            ApplyTip();
        }

        private void OnMineClicked(ShopItem item)
        {
            if (!TryGetRelicId(item, out var relicId) || _dragging)
            {
                return;
            }

            _tipAnchor = item;
            ViewModel.PreviewOwned(relicId);
            ApplyTip();
        }

        private void OnItemBeginDrag(ShopItem item, PointerEventData eventData)
        {
            if (!TryGetRelicId(item, out var relicId))
            {
                return;
            }

            _dragging = true;
            var buying = _sellItems.Contains(item);
            ViewModel.BeginDragTrade(relicId, buying);
            ApplyTip();
            SetItemDragging(item, true);
            BeginGhost(item, eventData);
        }

        private void OnItemDrag(ShopItem item, PointerEventData eventData)
        {
            MoveGhost(eventData);
        }

        private void OnItemEndDrag(ShopItem item, PointerEventData eventData)
        {
            var droppedBuy = ContainsScreen(_buyDrop, eventData.position) && ViewModel.ShowBuy.Value;
            var droppedSell = ContainsScreen(_sellDrop, eventData.position) && ViewModel.ShowSell.Value;
            SetItemDragging(item, false);
            DestroyGhost();
            _dragging = false;
            if (droppedBuy)
            {
                ViewModel.ConfirmBuy();
            }
            else if (droppedSell)
            {
                ViewModel.ConfirmSell();
            }

            ViewModel.EndDragTrade();
        }

        private void RefreshItems()
        {
            RefreshSellItems();
            RefreshMineItems();
        }

        private void RefreshSellItems()
        {
            var offers = ViewModel.Session.Run.ShopOfferIds;
            for (var i = 0; i < _sellItems.Count; i++)
            {
                var item = _sellItems[i];
                if (i >= offers.Count)
                {
                    item.gameObject.SetActive(false);
                    continue;
                }

                var relic = RelicConfig.Get(offers[i]);
                if (relic == null)
                {
                    item.gameObject.SetActive(false);
                    continue;
                }

                _icons.TryGetValue(relic.Id, out var icon);
                item.gameObject.SetActive(true);
                item.Bind(relic, icon);
            }
        }

        private void RefreshMineItems()
        {
            if (_mineTemplate == null)
            {
                return;
            }

            var owned = ViewModel.Session.Run.RelicConfigIds;
            var root = _mineTemplate.transform.parent;
            while (_mineItems.Count < owned.Count)
            {
                _mineItems.Add(CloneItem(_mineTemplate, root, $"mineItem_{_mineItems.Count}", OnMineClicked));
            }

            for (var i = 0; i < _mineItems.Count; i++)
            {
                var item = _mineItems[i];
                if (i >= owned.Count)
                {
                    item.gameObject.SetActive(false);
                    continue;
                }

                var relic = RelicConfig.Get(owned[i]);
                if (relic == null)
                {
                    item.gameObject.SetActive(false);
                    continue;
                }

                _icons.TryGetValue(relic.Id, out var icon);
                item.gameObject.SetActive(true);
                item.Bind(relic, icon, forSale: false);
            }
        }

        private async Task LoadOfferIcons()
        {
            var ids = new HashSet<int>();
            var offers = ViewModel.Session.Run.ShopOfferIds;
            for (var i = 0; i < offers.Count; i++)
            {
                ids.Add(offers[i]);
            }

            var owned = ViewModel.Session.Run.RelicConfigIds;
            for (var i = 0; i < owned.Count; i++)
            {
                ids.Add(owned[i]);
            }

            foreach (var id in ids)
            {
                await EnsureIcon(id);
            }
        }

        private async Task EnsureIcon(int relicId)
        {
            if (_icons.ContainsKey(relicId) || ViewModel.Resources == null)
            {
                return;
            }

            var relic = RelicConfig.Get(relicId);
            var key = ResResourcePaths.RelicIcon(relic != null ? relic.Icon : null);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            try
            {
                _icons[relicId] = await ViewModel.Resources.LoadAsync<Sprite>(key);
            }
            catch (Exception)
            {
            }
        }

        private async Task EnsureTip()
        {
            if (_tip != null || ViewModel.Resources == null)
            {
                return;
            }

            try
            {
                var prefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.ItemTip);
                if (prefab == null)
                {
                    return;
                }

                _tip = UnityEngine.Object.Instantiate(prefab, transform, false);
                _tip.name = "ItemTip";
                _tipText = _tip.GetComponentInChildren<TMP_Text>(true);
                var group = _tip.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = _tip.AddComponent<CanvasGroup>();
                }

                group.blocksRaycasts = false;
                group.interactable = false;
                _tip.SetActive(false);
            }
            catch (Exception)
            {
            }
        }

        private void OnTipText(string text)
        {
            if (_tipText != null)
            {
                _tipText.text = text ?? string.Empty;
            }
        }

        private void ApplyTip()
        {
            if (_tip == null)
            {
                return;
            }

            var show = ViewModel.ShowTip.Value;
            UpdateTipAnimations(show);

            EnsureTipCatcher();
            if (_tipCatcher != null)
            {
                _tipCatcher.SetActive(show);
                if (show)
                {
                    _tipCatcher.transform.SetAsLastSibling();
                    if (_sellHor != null)
                    {
                        _sellHor.SetAsLastSibling();
                    }

                    if (_mineHor != null)
                    {
                        _mineHor.SetAsLastSibling();
                    }
                }
            }

            _tip.SetActive(show);
            if (!show)
            {
                return;
            }

            if (_tipText != null)
            {
                _tipText.text = ViewModel.TipText.Value ?? string.Empty;
            }

            Canvas.ForceUpdateCanvases();
            var tipRt = _tip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }

            _tip.transform.SetAsLastSibling();
            PositionTipBelow(_tipAnchor);
        }

        private void UpdateTipAnimations(bool show)
        {
            if (show)
            {
                if (_tipAnchor == null)
                {
                    return;
                }

                if (_tipAnimAnchor == _tipAnchor)
                {
                    return;
                }

                if (_tipAnimAnchor != null)
                {
                    _tipAnimAnchor.PlayChooseEnd();
                }

                _tipAnchor.PlayChooseStart();
                _tipAnimAnchor = _tipAnchor;
                return;
            }

            if (_tipAnimAnchor != null)
            {
                _tipAnimAnchor.PlayChooseEnd();
                _tipAnimAnchor = null;
            }

            _tipAnchor = null;
        }

        private void EnsureTipCatcher()
        {
            if (_tipCatcher != null)
            {
                return;
            }

            var go = new GameObject("ItemTipCatcher", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => ViewModel.HideTip());
            go.SetActive(false);
            _tipCatcher = go;
        }

        private void PositionTipBelow(ShopItem item)
        {
            var tipRt = _tip != null ? _tip.GetComponent<RectTransform>() : null;
            var itemRt = item != null ? item.GetComponent<RectTransform>() : null;
            var parent = transform as RectTransform;
            if (tipRt == null || itemRt == null || parent == null)
            {
                return;
            }

            var cam = _canvas != null ? _canvas.worldCamera : null;
            itemRt.GetWorldCorners(_corners);
            var bottom = (_corners[0] + _corners[3]) * 0.5f;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, bottom);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local))
            {
                return;
            }

            var tipHeight = tipRt.rect.height;
            tipRt.anchoredPosition = new Vector2(local.x, local.y - 8f - (1f - tipRt.pivot.y) * tipHeight);
        }

        private void BeginGhost(ShopItem item, PointerEventData eventData)
        {
            DestroyGhost();
            _ghost = UnityEngine.Object.Instantiate(item.gameObject, transform, false);
            _ghost.name = "ShopItemGhost";
            var bind = _ghost.GetComponent<UIBind>();
            if (bind != null)
            {
                UnityEngine.Object.Destroy(bind);
            }

            var shopItem = _ghost.GetComponent<ShopItem>();
            if (shopItem != null)
            {
                UnityEngine.Object.Destroy(shopItem);
            }

            var button = _ghost.GetComponent<Button>();
            if (button != null)
            {
                UnityEngine.Object.Destroy(button);
            }

            var layout = _ghost.GetComponent<LayoutElement>();
            if (layout != null)
            {
                UnityEngine.Object.Destroy(layout);
            }

            var group = _ghost.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = _ghost.AddComponent<CanvasGroup>();
            }

            group.blocksRaycasts = false;
            group.alpha = 0.95f;

            var srcRt = item.GetComponent<RectTransform>();
            var ghostRt = _ghost.GetComponent<RectTransform>();
            var parent = transform as RectTransform;
            if (srcRt != null && ghostRt != null && parent != null)
            {
                ghostRt.anchorMin = ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
                ghostRt.pivot = srcRt.pivot;
                ghostRt.sizeDelta = srcRt.rect.size;
                ghostRt.localScale = Vector3.one;
                var cam = _canvas != null ? _canvas.worldCamera : null;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, cam, out var pointerLocal);
                var itemScreen = RectTransformUtility.WorldToScreenPoint(cam, srcRt.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, itemScreen, cam, out var itemLocal);
                _ghostGrabOffset = itemLocal - pointerLocal;
            }

            _ghost.transform.SetAsLastSibling();
            MoveGhost(eventData);
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (_ghost == null)
            {
                return;
            }

            var parent = transform as RectTransform;
            var ghostRt = _ghost.transform as RectTransform;
            if (parent == null || ghostRt == null)
            {
                return;
            }

            var cam = _canvas != null ? _canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, cam, out var local))
            {
                return;
            }

            ghostRt.anchoredPosition = local + _ghostGrabOffset;
        }

        private void DestroyGhost()
        {
            if (_ghost == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_ghost);
            _ghost = null;
        }

        private static void SetItemDragging(ShopItem item, bool dragging)
        {
            if (item == null)
            {
                return;
            }

            var group = item.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = item.gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = dragging ? 0.45f : 1f;
        }

        private bool ContainsScreen(RectTransform target, Vector2 screen)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                return false;
            }

            var cam = _canvas != null ? _canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(target, screen, cam);
        }

        private static bool TryGetRelicId(ShopItem item, out int relicId)
        {
            relicId = item != null && item.Data != null ? item.Data.RelicConfigId : 0;
            return relicId > 0;
        }
    }
}
