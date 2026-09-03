using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Item;
using App.Resources;
using Framework.Log;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 遗物列表弹窗。ItemSCView/Content 自带一张 Item 卡模板，按本局已持有遗物克隆：
    /// 显示名字、图标与品质色。点击在该卡上方弹出 ItemTip；可消耗遗物可在 tip 上使用。
    /// CloseBtn 关闭弹窗。
    /// </summary>
    [AutoScreen(AppScreenIds.RemainListPop, UILayer.Popup, ResResourcePaths.RemainListPop)]
    public sealed class RemainListPopView : ViewBase<RemainListPopViewModel>
    {
        private readonly List<ItemCard> _items = new List<ItemCard>();
        private readonly Dictionary<ItemCard, RelicConfig> _bound = new Dictionary<ItemCard, RelicConfig>();
        private readonly Vector3[] _corners = new Vector3[4];
        private Transform _content;
        private GameObject _template;
        private GameObject _tip;
        private TMP_Text _tipText;
        private GameObject _tipUse;
        private Button _tipUseBtn;
        private GameObject _tipCatcher;
        private ItemCard _tipAnchor;
        private int _shownRelicId;
        private Canvas _canvas;

        protected override void OnBind()
        {
            var closeBtn = GetNode<Button>("CloseBtn");
            if (closeBtn != null)
            {
                Binding.BindCommand(closeBtn, ViewModel.CloseCommand);
            }

            BindResourceBar();
            EnsureTemplate();
            Binding.Add(ViewModel.ListVersion.Subscribe(_ => RefreshItems(), emitCurrent: true));
        }

        protected override Task OnViewClose()
        {
            HideTip();
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null)
                {
                    _items[i].Clicked -= OnCardClicked;
                    Destroy(_items[i].gameObject);
                }
            }

            _items.Clear();
            _bound.Clear();
            if (_tip != null)
            {
                Destroy(_tip);
                _tip = null;
                _tipText = null;
                _tipUse = null;
                _tipUseBtn = null;
            }

            if (_tipCatcher != null)
            {
                Destroy(_tipCatcher);
                _tipCatcher = null;
            }

            return Task.CompletedTask;
        }

        private void BindResourceBar()
        {
            if (!UI.TryGet<Transform>("ResourceItem", out var resourceItem))
            {
                return;
            }

            var num = resourceItem.Find("Num") ?? FindDeep(resourceItem, "Num");
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                Binding.BindRollingText(text, ViewModel.GoldText);
            }
        }

        /// <summary>UIBind 条目存的组件类型不保证（如 CloseBtn 存的是 CanvasRenderer），统一走 GameObject 再取组件。</summary>
        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void EnsureTemplate()
        {
            if (!UI.TryGet<ScrollRect>("ItemSCView", out var scroll) || scroll.content == null)
            {
                return;
            }

            _content = scroll.content;
            if (_content.childCount > 0)
            {
                _template = _content.GetChild(0).gameObject;
            }

            if (_template == null)
            {
                AppLog.Warn(LogChannel.UI, "RemainListPop Content 下缺少 Item 模板", this);
                return;
            }

            if (_template.GetComponent<ItemCard>() == null)
            {
                AppLog.Warn(LogChannel.UI, "RemainListPop 模板上缺少 ItemCard 组件", _template);
                return;
            }

            _template.SetActive(false);
        }

        private void RefreshItems()
        {
            if (_template == null || _content == null || ViewModel == null)
            {
                return;
            }

            var owned = ViewModel.Session.Run.RelicConfigIds;
            var needed = 0;
            for (var i = 0; i < owned.Count; i++)
            {
                if (RelicConfig.Get(owned[i]) != null)
                {
                    needed++;
                }
            }

            EnsureCardCount(needed);
            _bound.Clear();
            var slot = 0;
            for (var i = 0; i < owned.Count; i++)
            {
                var relic = RelicConfig.Get(owned[i]);
                if (relic == null)
                {
                    continue;
                }

                var card = _items[slot];
                card.gameObject.SetActive(true);
                card.Bind(relic.Name, ViewModel.GetRelicIcon(relic), unlocked: true);
                card.ApplyQuality(relic.Type);
                _bound[card] = relic;
                slot++;
            }

            for (var i = slot; i < _items.Count; i++)
            {
                _items[i].gameObject.SetActive(false);
            }

            if (_shownRelicId > 0)
            {
                ItemCard match = null;
                foreach (var pair in _bound)
                {
                    if (pair.Value != null && pair.Value.Id == _shownRelicId && pair.Key != null &&
                        pair.Key.gameObject.activeSelf)
                    {
                        match = pair.Key;
                        break;
                    }
                }

                if (match == null)
                {
                    HideTip();
                }
                else if (_tip != null && _tip.activeSelf)
                {
                    _tipAnchor = match;
                    PositionTipAbove(match);
                }
            }
        }

        private void EnsureCardCount(int needed)
        {
            while (_items.Count < needed)
            {
                var go = Instantiate(_template, _content, false);
                go.name = "Item_" + _items.Count;
                go.SetActive(true);
                var card = go.GetComponent<ItemCard>();
                card.SetShadowVisible(false);
                card.SetAnimationEnabled(false);
                card.Clicked += OnCardClicked;
                _items.Add(card);
            }
        }

        private void OnCardClicked(ItemCard card)
        {
            if (card == null || !_bound.TryGetValue(card, out var relic) || relic == null)
            {
                return;
            }

            if (_tip != null && _tip.activeSelf && _tipAnchor == card && _shownRelicId == relic.Id)
            {
                HideTip();
                return;
            }

            _ = ShowTip(card, relic);
        }

        private async Task ShowTip(ItemCard card, RelicConfig relic)
        {
            await EnsureTip();
            if (_tip == null || relic == null)
            {
                return;
            }

            _shownRelicId = relic.Id;
            _tipAnchor = card;
            if (_tipText != null)
            {
                _tipText.text = string.IsNullOrEmpty(relic.Desc) ? relic.Name : relic.Desc;
            }

            SetTipUseVisible(RelicMechanics.IsConsumable(relic));
            EnsureTipCatcher();
            if (_tipCatcher != null)
            {
                _tipCatcher.SetActive(true);
                _tipCatcher.transform.SetAsLastSibling();
            }

            _tip.SetActive(true);
            Canvas.ForceUpdateCanvases();
            var tipRt = _tip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }

            _tip.transform.SetAsLastSibling();
            PositionTipAbove(card);
        }

        private async Task EnsureTip()
        {
            if (_tip != null || ViewModel == null || ViewModel.Resources == null)
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

                _tip = Instantiate(prefab, transform, false);
                _tip.name = "ItemTip";
                BindTipNodes(_tip);
                var group = _tip.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = _tip.AddComponent<CanvasGroup>();
                }

                group.blocksRaycasts = true;
                group.interactable = true;
                _tip.SetActive(false);
                _canvas = GetComponentInParent<Canvas>();
            }
            catch (Exception)
            {
            }
        }

        private void BindTipNodes(GameObject tip)
        {
            _tipText = null;
            _tipUse = null;
            _tipUseBtn = null;
            var ui = tip != null ? tip.GetComponent<UIReference>() : null;
            if (ui != null && ui.TryGet<Component>("tipContext", out var context) && context != null)
            {
                _tipText = context.GetComponent<TMP_Text>() ?? context.GetComponentInChildren<TMP_Text>(true);
            }

            if (_tipText == null && tip != null)
            {
                _tipText = tip.GetComponentInChildren<TMP_Text>(true);
            }

            if (ui != null && ui.TryGet<Component>("use", out var useNode) && useNode != null)
            {
                _tipUse = useNode.gameObject;
                _tipUseBtn = useNode.GetComponent<Button>() ?? useNode.GetComponentInChildren<Button>(true);
            }

            if (_tipUseBtn != null)
            {
                _tipUseBtn.onClick.RemoveAllListeners();
                _tipUseBtn.onClick.AddListener(OnTipUseClicked);
            }
        }

        private void SetTipUseVisible(bool visible)
        {
            if (_tipUse != null)
            {
                _tipUse.SetActive(visible);
            }
        }

        private void OnTipUseClicked()
        {
            if (_shownRelicId <= 0 || ViewModel?.Session == null)
            {
                return;
            }

            ViewModel.Session.UseRelic(_shownRelicId);
        }

        private void HideTip()
        {
            _shownRelicId = 0;
            _tipAnchor = null;
            if (_tip != null)
            {
                _tip.SetActive(false);
            }

            if (_tipCatcher != null)
            {
                _tipCatcher.SetActive(false);
            }
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
            button.onClick.AddListener(HideTip);
            go.SetActive(false);
            _tipCatcher = go;
        }

        private void PositionTipAbove(ItemCard item)
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
            var top = (_corners[1] + _corners[2]) * 0.5f;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, top);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local))
            {
                return;
            }

            var tipWidth = tipRt.rect.width;
            var tipHeight = tipRt.rect.height;
            tipRt.anchoredPosition = new Vector2(
                local.x + (0.5f - tipRt.pivot.x) * tipWidth,
                local.y + 8f + tipRt.pivot.y * tipHeight);
            ItemTipPlacement.ClampToParent(tipRt, parent);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
