using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Item;
using App.Resources;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 图鉴弹窗。CollectToggle / RelicToggle / MonsterToggle 切换三份 ScrollRect。
    /// 收藏/遗物两页用 Item(ItemCard)，怪物页用 PlayerItem（卡面带攻击/血量块）。
    /// </summary>
    [AutoScreen(AppScreenIds.IllustratedBookPop, UILayer.Page, ResResourcePaths.IllustratedBookPop)]
    public sealed class IllustratedBookPopView : ViewBase<IllustratedBookPopViewModel>
    {
        // 怪物页分区节点名：Boss 进 UltimateGrid（横幅 TitleBg），Normal 进 RareGrid；
        // NormalBanner/NormalGrid 已在预制体隐藏，代码不再触碰
        private const string UltimateBannerName = "TitleBg";
        private const string UltimateGridName = "UltimateGrid";
        private const string RareBannerName = "RareBanner";
        private const string RareGridName = "RareGrid";

        private readonly List<ItemCard> _collectCards = new List<ItemCard>();
        private readonly List<ItemCard> _relicCards = new List<ItemCard>();
        private readonly List<PlayerItem> _monsterCards = new List<PlayerItem>();
        private readonly Dictionary<Component, IllustratedBookEntry> _entries =
            new Dictionary<Component, IllustratedBookEntry>();
        private readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();
        private readonly Vector3[] _corners = new Vector3[4];
        private GameObject _itemPrefab;
        private GameObject _monsterPrefab;
        private GameObject _tip;
        private TMP_Text _tipTitle;
        private TMP_Text _tipText;
        private GameObject _tipCatcher;
        private Component _tipAnchor;
        private Canvas _canvas;

        protected override async Task OnViewOpen()
        {
            ViewModel.RefreshEntries();
            await EnsureItemPrefab();
            await EnsureMonsterPrefab();
            await LoadIcons();
            await EnsureTip();
        }

        protected override void OnBind()
        {
            _canvas = GetComponentInParent<Canvas>();
            // 新预制体已删 CloseBtn 节点（关闭走底栏导航页签）；旧预制体仍带按钮时保持绑定。
            if (UI.TryGet<Button>("CloseBtn", out var closeBtn))
            {
                Binding.BindCommand(closeBtn, ViewModel.CloseCommand);
            }

            Binding.BindText(UI.Get<TMP_Text>("CurItemNum"), ViewModel.CurItemNum);
            Binding.BindActive(UI.GetGameObject("CollectSCView"), ViewModel.ShowCollect);
            Binding.BindActive(UI.GetGameObject("RelicSCView"), ViewModel.ShowRelic);
            Binding.BindActive(UI.GetGameObject("MonsterSCView"), ViewModel.ShowMonster);
            // 怪物页隐藏界面顶部标题横幅（分区横幅自带标题，UIReference 键由编辑器注册）
            Binding.BindActive(UI.GetGameObject("TitleBg"), ViewModel.ShowTitleBar);
            BindTab(UI.Get<Toggle>("CollectToggle"), ViewModel.CollectOn, IllustratedBookTab.Collect);
            BindTab(UI.Get<Toggle>("RelicToggle"), ViewModel.RelicOn, IllustratedBookTab.Relic);
            BindTab(UI.Get<Toggle>("MonsterToggle"), ViewModel.MonsterOn, IllustratedBookTab.Monster);
            FillList(UI.Get<ScrollRect>("CollectSCView"), ViewModel.CollectEntries, _collectCards);
            FillList(UI.Get<ScrollRect>("RelicSCView"), ViewModel.RelicEntries, _relicCards);
            FillMonsterList(UI.Get<ScrollRect>("MonsterSCView"), ViewModel.MonsterEntries);
            Binding.Add(ViewModel.ShowTip.Subscribe(_ => ApplyTip(), emitCurrent: true));
            Binding.Add(ViewModel.TipTitle.Subscribe(OnTipTitle, emitCurrent: true));
            Binding.Add(ViewModel.TipText.Subscribe(OnTipText, emitCurrent: true));
        }

        protected override Task OnViewClose()
        {
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

        private void BindTab(Toggle toggle, Framework.UI.Core.ObservableProperty<bool> source, IllustratedBookTab tab)
        {
            Binding.BindToggle(toggle, source);
            Binding.Add(source.Subscribe(isOn =>
            {
                if (isOn)
                {
                    ViewModel.SelectTab(tab);
                    RefreshSelected();
                    ApplyTip();
                }
            }, emitCurrent: false));
        }

        private void FillList(
            ScrollRect scroll,
            IReadOnlyList<IllustratedBookEntry> entries,
            List<ItemCard> cards)
        {
            cards.Clear();
            if (scroll == null || scroll.content == null || _itemPrefab == null)
            {
                return;
            }

            var content = scroll.content;
            ClearContent(content);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var go = Instantiate(_itemPrefab, content, false);
                go.name = entry.Tab + "_" + entry.Id;
                go.SetActive(true);
                go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
                var bind = go.GetComponent<UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                var card = go.GetComponent<ItemCard>();
                if (card == null)
                {
                    continue;
                }

                card.SetShadowVisible(false);
                card.SetAnimationEnabled(false);
                card.Bind(entry.Unlocked ? entry.Name : null, GetIcon(entry), entry.Unlocked);
                card.Clicked += OnCardClicked;
                _entries[card] = entry;
                cards.Add(card);
            }

            FitContentHeight(scroll, content, cards.Count);
        }

        /// <summary>
        /// 怪物页按 MonsterType 分两区（同天赋页格式：横幅 + Grid 交替，Content 由
        /// VLayout+ContentSizeFitter 自适应高度）：Boss 进 UltimateGrid，Normal 进 RareGrid；
        /// 对应类型没有怪物时连横幅一起隐藏。
        /// </summary>
        private void FillMonsterList(ScrollRect scroll, IReadOnlyList<IllustratedBookEntry> entries)
        {
            _monsterCards.Clear();
            var content = scroll != null ? scroll.content : null;
            if (content == null || _monsterPrefab == null)
            {
                return;
            }

            var bosses = new List<IllustratedBookEntry>();
            var normals = new List<IllustratedBookEntry>();
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].MonsterType == MonsterType.Boss)
                {
                    bosses.Add(entries[i]);
                }
                else
                {
                    normals.Add(entries[i]);
                }
            }

            FillMonsterSection(content.Find(UltimateBannerName), content.Find(UltimateGridName), bosses);
            FillMonsterSection(content.Find(RareBannerName), content.Find(RareGridName), normals);
        }

        /// <summary>对应类型没有怪物时连横幅一起隐藏；格子为 PlayerItem 敌人形态（enemycard 底图走
        /// MonsterConfig.BaseMap/HealthBar，攻血块隐藏，点击打开详情），卡面 0.9 缩放沿旧版。</summary>
        private void FillMonsterSection(Transform banner, Transform grid, List<IllustratedBookEntry> entries)
        {
            if (grid == null)
            {
                return;
            }

            var visible = entries.Count > 0;
            if (banner != null)
            {
                banner.gameObject.SetActive(visible);
            }

            grid.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            for (var i = grid.childCount - 1; i >= 0; i--)
            {
                var child = grid.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var go = Instantiate(_monsterPrefab, grid, false);
                go.name = entry.Tab + "_" + entry.Id;
                go.SetActive(true);
                go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
                var bind = go.GetComponent<UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                var card = go.GetComponent<PlayerItem>();
                if (card == null)
                {
                    continue;
                }

                card.ApplyEnemyTheme(entry.Id);
                var cardMask = FindDeep(card.transform, "cardMask");
                if (cardMask != null)
                {
                    cardMask.gameObject.SetActive(false);
                }

                card.SetName(entry.Unlocked ? entry.Name : "？？？");
                card.SetPortrait(GetIcon(entry), locked: !entry.Unlocked);
                card.SetAttack(0);
                card.SetHp(0);

                HookMonsterClick(card);
                _entries[card] = entry;
                _monsterCards.Add(card);
            }
        }

        /// <summary>PlayerItem 预制体无 Button，运行时补透明射线 Image + Button（同 GameUIView.BindSeatClick）。</summary>
        private void HookMonsterClick(PlayerItem card)
        {
            var target = card.gameObject;
            var image = target.GetComponent<Image>();
            if (image == null)
            {
                image = target.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.01f);
            }

            var button = target.GetComponent<Button>();
            if (button == null)
            {
                button = target.AddComponent<Button>();
            }

            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnMonsterClicked(card));
        }

        private static Transform FindDeep(Transform root, string nodeName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == nodeName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), nodeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void ClearContent(RectTransform content)
        {
            for (var i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        private static void FitContentHeight(ScrollRect scroll, RectTransform content, int itemCount)
        {
            var grid = content.GetComponent<GridLayoutGroup>();
            var size = content.sizeDelta;
            if (grid == null)
            {
                size.y = 0f;
                content.sizeDelta = size;
                return;
            }

            var columns = ResolveColumnCount(grid, ResolveContentWidth(scroll, content));
            var rows = itemCount <= 0 ? 0 : Mathf.CeilToInt(itemCount / (float)columns);
            var height = (float)grid.padding.top + grid.padding.bottom;
            if (rows > 0)
            {
                height += rows * grid.cellSize.y + (rows - 1) * grid.spacing.y;
            }

            size.y = height;
            content.sizeDelta = size;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private static float ResolveContentWidth(ScrollRect scroll, RectTransform content)
        {
            var width = content.rect.width;
            if (width > 1f)
            {
                return width;
            }

            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            width = viewport.rect.width;
            if (width > 1f)
            {
                return width;
            }

            var host = (RectTransform)scroll.transform;
            width = host.rect.width;
            return width > 1f ? width : Mathf.Max(1f, host.sizeDelta.x);
        }

        private static int ResolveColumnCount(GridLayoutGroup grid, float width)
        {
            if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
            {
                return Mathf.Max(1, grid.constraintCount);
            }

            var stride = grid.cellSize.x + grid.spacing.x;
            if (stride <= 0f)
            {
                return 1;
            }

            var inner = width - grid.padding.horizontal + grid.spacing.x + 0.001f;
            return Mathf.Max(1, Mathf.FloorToInt(inner / stride));
        }

        private void OnCardClicked(ItemCard card)
        {
            if (_entries.TryGetValue(card, out var entry))
            {
                ViewModel.OpenDetail(entry, GetIcon);
            }
        }

        private void OnMonsterClicked(PlayerItem card)
        {
            if (_entries.TryGetValue(card, out var entry))
            {
                ViewModel.OpenDetail(entry, GetIcon);
            }
        }

        private void RefreshSelected()
        {
            RefreshSelected(_collectCards);
            RefreshSelected(_relicCards);
            RefreshSelected(_monsterCards);
        }

        private void RefreshSelected(List<ItemCard> cards)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null || !_entries.TryGetValue(card, out var entry))
                {
                    continue;
                }

                card.SetSelected(ViewModel.IsSelected(entry));
            }
        }

        private void RefreshSelected(List<PlayerItem> cards)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null || !_entries.TryGetValue(card, out var entry))
                {
                    continue;
                }

                card.SetSelectLift(ViewModel.IsSelected(entry), HeroItem.SelectAnim, HeroItem.DefaultAnim);
            }
        }

        private async Task EnsureItemPrefab()
        {
            if (_itemPrefab != null || ViewModel.Resources == null)
            {
                return;
            }

            try
            {
                _itemPrefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.Item);
            }
            catch (Exception)
            {
            }
        }

        private async Task EnsureMonsterPrefab()
        {
            if (_monsterPrefab != null || ViewModel.Resources == null)
            {
                return;
            }

            try
            {
                _monsterPrefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.PlayerItem);
            }
            catch (Exception)
            {
            }
        }

        private async Task LoadIcons()
        {
            await LoadEntryIcons(ViewModel.CollectEntries);
            await LoadEntryIcons(ViewModel.RelicEntries);
            await LoadEntryIcons(ViewModel.MonsterEntries);
        }

        private async Task LoadEntryIcons(IReadOnlyList<IllustratedBookEntry> entries)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Tab == IllustratedBookTab.Monster)
                {
                    continue;
                }

                var key = IconKey(entries[i]);
                if (string.IsNullOrEmpty(key) || _icons.ContainsKey(key))
                {
                    continue;
                }

                var sprite = TryAtlasSprite(entries[i]);
                if (sprite == null)
                {
                    sprite = await LoadResourceSprite(ResourceIconPath(entries[i]) ?? key);
                }

                if (sprite != null)
                {
                    _icons[key] = sprite;
                }
            }
        }

        private Sprite GetIcon(IllustratedBookEntry entry)
        {
            if (entry != null && entry.Tab == IllustratedBookTab.Monster)
            {
                return PortraitLoader.GetEnemy(entry.Icon);
            }

            var key = IconKey(entry);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_icons.TryGetValue(key, out var sprite))
            {
                return sprite;
            }

            sprite = TryAtlasSprite(entry);
            if (sprite != null)
            {
                _icons[key] = sprite;
            }

            return sprite;
        }

        private Sprite TryAtlasSprite(IllustratedBookEntry entry)
        {
            if (entry == null || entry.Tab != IllustratedBookTab.Relic || string.IsNullOrEmpty(entry.Icon))
            {
                return null;
            }

            var atlas = ViewModel != null ? ViewModel.Atlas : null;
            if (atlas == null)
            {
                return null;
            }

            atlas.TryGetSprite(ResResourcePaths.RelicAtlas, entry.Icon.Trim(), out var sprite);
            return sprite;
        }

        private async Task<Sprite> LoadResourceSprite(string key)
        {
            if (string.IsNullOrEmpty(key) || ViewModel.Resources == null)
            {
                return null;
            }

            try
            {
                return await ViewModel.Resources.LoadAsync<Sprite>(key);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ResourceIconPath(IllustratedBookEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            switch (entry.Tab)
            {
                case IllustratedBookTab.Collect:
                    return ResResourcePaths.RoleIcon(entry.Icon);
                case IllustratedBookTab.Relic:
                    return ResResourcePaths.RelicIcon(entry.Icon);
                case IllustratedBookTab.Monster:
                    return ResResourcePaths.EnemyPortrait(entry.Icon, ResResourcePaths.PortraitAttack);
                default:
                    return null;
            }
        }

        private static string IconKey(IllustratedBookEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            switch (entry.Tab)
            {
                case IllustratedBookTab.Collect:
                    return ResResourcePaths.RoleIcon(entry.Icon);
                case IllustratedBookTab.Relic:
                    return string.IsNullOrWhiteSpace(entry.Icon) ? null : "relic:" + entry.Icon.Trim();
                case IllustratedBookTab.Monster:
                    return ResResourcePaths.EnemyPortrait(entry.Icon, ResResourcePaths.PortraitAttack);
                default:
                    return null;
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

                _tip = Instantiate(prefab, transform, false);
                _tip.name = "ItemTip";
                var ui = _tip.GetComponent<UIReference>();
                if (ui != null && ui.TryGet<Component>("title", out var title) && title != null)
                {
                    _tipTitle = title.GetComponent<TMP_Text>() ?? title.GetComponentInChildren<TMP_Text>(true);
                }

                if (ui != null && ui.TryGet<Component>("tipContext", out var context) && context != null)
                {
                    _tipText = context.GetComponent<TMP_Text>() ?? context.GetComponentInChildren<TMP_Text>(true);
                }

                if (_tipText == null)
                {
                    var texts = _tip.GetComponentsInChildren<TMP_Text>(true);
                    for (var i = 0; i < texts.Length; i++)
                    {
                        if (texts[i] == _tipTitle)
                        {
                            continue;
                        }

                        _tipText = texts[i];
                        break;
                    }
                }

                if (ui != null && ui.TryGet<Component>("use", out var useNode) && useNode != null)
                {
                    useNode.gameObject.SetActive(false);
                }

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

        private void OnTipTitle(string text)
        {
            ApplyTipTitle(text);
        }

        private void ApplyTipTitle(string text)
        {
            if (_tipTitle == null)
            {
                return;
            }

            var value = text ?? string.Empty;
            _tipTitle.text = value;
            _tipTitle.gameObject.SetActive(!string.IsNullOrEmpty(value));
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
            if (!show)
            {
                _tipAnchor = null;
            }

            EnsureTipCatcher();
            if (_tipCatcher != null)
            {
                _tipCatcher.SetActive(show);
                if (show)
                {
                    _tipCatcher.transform.SetAsLastSibling();
                }
            }

            _tip.SetActive(show);
            if (!show)
            {
                return;
            }

            ApplyTipTitle(ViewModel.TipTitle.Value);

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
            button.onClick.AddListener(() =>
            {
                ViewModel.HideTip();
                RefreshSelected();
                ApplyTip();
            });
            go.SetActive(false);
            _tipCatcher = go;
        }

        private void PositionTipBelow(Component item)
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
            tipRt.anchoredPosition = new Vector2(local.x, local.y + 28.35828f - (1f - tipRt.pivot.y) * tipHeight);
            ItemTipPlacement.ClampToParent(tipRt, parent);
        }
    }
}
