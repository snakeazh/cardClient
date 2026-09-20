using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Item;
using App.Resources;
using App.UI.List;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.Log;
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
        // 怪物页按 MonsterType 三分（Banner+Grid 成对，MonsterSCView/Content 下 VLG 竖排）：
        // Boss→UltimateBg/UltimateGrid、精英→EliteBanner/EliteGrid、普通→NormalBanner/NormalGrid；
        // Normal 两个名字与遗物页分区同名，但 Fill 在各自 Content 下 Find，互不影响
        private const string UltimateBannerName = "UltimateBg";
        private const string UltimateGridName = "UltimateGrid";
        private const string EliteBannerName = "EliteBanner";
        private const string EliteGridName = "EliteGrid";

        // 遗物页品质分区节点名（RelicSCView/Content 下 Banner+Grid 成对）：
        // 传说→LegendGrid、史诗→EpicGrid、稀有→RareGrid、普通→NormalGrid
        private const string LegendBannerName = "LegendBanner";
        private const string LegendGridName = "LegendGrid";
        private const string EpicBannerName = "EpicBanner";
        private const string EpicGridName = "EpicGrid";
        private const string RareBannerName = "RareBanner";
        private const string RareGridName = "RareGrid";
        private const string NormalBannerName = "NormalBanner";
        private const string NormalGridName = "NormalGrid";

        private readonly Dictionary<Component, IllustratedBookEntry> _entries =
            new Dictionary<Component, IllustratedBookEntry>();
        private readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();
        private readonly Vector3[] _corners = new Vector3[4];
        // 已构建（或构建中）的页签：打开只建当前页，其余页首次切换时再建
        private readonly HashSet<IllustratedBookTab> _builtTabs = new HashSet<IllustratedBookTab>();
        private readonly List<Component> _activeSlots = new List<Component>();
        // 本视图创建的全部卡槽（含行池里隐藏中的）：关闭时回收到 UiCardPool 供下次打开复用
        private readonly List<Component> _ownedSlots = new List<Component>();
        private CardRowRecycler<IllustratedBookEntry> _collectRecycler;
        private CardRowRecycler<IllustratedBookEntry> _relicRecycler;
        private CardRowRecycler<IllustratedBookEntry> _monsterRecycler;
        private bool _tipLoading;
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
            // 重开时数据可能已变：页签标记全部重置（SetSections 重建数据，行池与横幅克隆重绑）
            _builtTabs.Clear();
            // 只预载当前页（默认遗物）要用的 Item 预制体；PlayerItem/ItemTip/收藏图标延迟到首次需要
            await EnsureItemPrefab();
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
            // 打开只构建当前页（默认遗物），避免一帧克隆三页全部卡片造成卡顿
            StartBuildTab(ViewModel.Tab);
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

            // 视图销毁前把卡槽脱离 Content 回收进池，下次打开直接复用，不再整批 Instantiate
            ReleaseOwnedSlots();
            return Task.CompletedTask;
        }

        private void ReleaseOwnedSlots()
        {
            for (var i = 0; i < _ownedSlots.Count; i++)
            {
                var slot = _ownedSlots[i];
                if (slot is ItemCard itemCard)
                {
                    UiCardPool.ReleaseItemCard(itemCard);
                }
                else if (slot is PlayerItem playerItem)
                {
                    UiCardPool.ReleasePlayerItem(playerItem);
                }
            }

            _ownedSlots.Clear();
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
                    // 首次切到该页才克隆卡片（StartBuildTab 幂等）
                    StartBuildTab(tab);
                }
            }, emitCurrent: false));
        }

        /// <summary>收藏页构建：单区无横幅，Content 自身的 GridLayoutGroup 即布局模板。</summary>
        private void BuildCollectPage()
        {
            var scroll = UI.Get<ScrollRect>("CollectSCView");
            var content = scroll != null ? scroll.content : null;
            if (content == null || _itemPrefab == null)
            {
                return;
            }

            var grid = content.GetComponent<GridLayoutGroup>();
            if (_collectRecycler == null)
            {
                CleanContent(content);
                _collectRecycler = new CardRowRecycler<IllustratedBookEntry>();
                _collectRecycler.Initialize(scroll, grid, null, CreateCardSlot, BindCardSlot);
            }

            var section = new CardRowRecycler<IllustratedBookEntry>.Section
            {
                GridTemplate = grid,
                Items = new List<IllustratedBookEntry>(ViewModel.CollectEntries)
            };
            _collectRecycler.SetSections(new[] { section });
        }

        /// <summary>向 UiCardPool 租用 ItemCard 槽位（装饰幂等：0.9 缩放、关阴影动画、清旧订阅再订阅点击），
        /// 数据绑定走 BindCardSlot；视图关闭时槽位回收进池复用。</summary>
        private Component CreateCardSlot(Transform parent)
        {
            var card = UiCardPool.RentItemCard(_itemPrefab, parent);
            if (card == null)
            {
                return null;
            }

            var go = card.gameObject;
            go.name = "CardSlot";
            go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            var bind = go.GetComponent<UIBind>();
            if (bind != null)
            {
                Destroy(bind);
            }

            card.SetShadowVisible(false);
            card.SetAnimationEnabled(false);
            // 池化复用：先清掉上一任视图的订阅再挂自己的
            card.ClearClicked();
            card.Clicked += OnCardClicked;
            _ownedSlots.Add(card);
            return card;
        }

        /// <summary>卡槽数据绑定（行复用时反复调用）：品质边框（遗物页=RelicConfig.Type，缺图回退普通品质）、
        /// 未解锁黑剪影（同天赋列表口径）、刷新选中态。</summary>
        private void BindCardSlot(Component slot, IllustratedBookEntry entry)
        {
            var card = (ItemCard)slot;
            card.ApplyQuality(entry.Quality);
            card.Bind(entry.Unlocked ? entry.Name : null, GetIcon(entry), entry.Unlocked);
            card.SetIconColor(entry.Unlocked ? Color.white : Color.black);
            _entries[card] = entry;
            card.SetSelected(ViewModel.IsSelected(entry));
        }

        /// <summary>
        /// 遗物页按品质分四区（传说/史诗/稀有/普通，横幅 + 卡片行交替）；
        /// 对应品质没有遗物时连横幅一起隐藏。行虚拟化：视口内才生成，行与卡槽池化复用。
        /// </summary>
        private void BuildRelicPage()
        {
            var scroll = UI.Get<ScrollRect>("RelicSCView");
            var content = scroll != null ? scroll.content : null;
            if (content == null || _itemPrefab == null)
            {
                return;
            }

            var legends = new List<IllustratedBookEntry>();
            var epics = new List<IllustratedBookEntry>();
            var rares = new List<IllustratedBookEntry>();
            var normals = new List<IllustratedBookEntry>();
            var entries = ViewModel.RelicEntries;
            for (var i = 0; i < entries.Count; i++)
            {
                switch (entries[i].Quality)
                {
                    case QualityType.Legend:
                        legends.Add(entries[i]);
                        break;
                    case QualityType.Epic:
                        epics.Add(entries[i]);
                        break;
                    case QualityType.Rare:
                        rares.Add(entries[i]);
                        break;
                    default:
                        normals.Add(entries[i]);
                        break;
                }
            }

            if (_relicRecycler == null)
            {
                CleanContent(content, LegendBannerName, LegendGridName, EpicBannerName, EpicGridName,
                    RareBannerName, RareGridName, NormalBannerName, NormalGridName);
                _relicRecycler = new CardRowRecycler<IllustratedBookEntry>();
                _relicRecycler.Initialize(
                    scroll, FindGrid(content, LegendGridName, EpicGridName, RareGridName, NormalGridName),
                    content.GetComponent<VerticalLayoutGroup>(), CreateCardSlot, BindCardSlot);
            }

            var sections = new List<CardRowRecycler<IllustratedBookEntry>.Section>(4);
            AddSection(sections, (RectTransform)content.Find(LegendBannerName), FindGrid(content, LegendGridName), legends);
            AddSection(sections, (RectTransform)content.Find(EpicBannerName), FindGrid(content, EpicGridName), epics);
            AddSection(sections, (RectTransform)content.Find(RareBannerName), FindGrid(content, RareGridName), rares);
            AddSection(sections, (RectTransform)content.Find(NormalBannerName), FindGrid(content, NormalGridName), normals);
            _relicRecycler.SetSections(sections);
        }

        private static GridLayoutGroup FindGrid(Transform content, params string[] names)
        {
            for (var i = 0; i < names.Length; i++)
            {
                var node = content.Find(names[i]);
                if (node != null)
                {
                    return node.GetComponent<GridLayoutGroup>();
                }
            }

            return null;
        }

        private static void AddSection(
            List<CardRowRecycler<IllustratedBookEntry>.Section> sections,
            RectTransform banner,
            GridLayoutGroup grid,
            List<IllustratedBookEntry> items)
        {
            sections.Add(new CardRowRecycler<IllustratedBookEntry>.Section
            {
                BannerTemplate = banner,
                GridTemplate = grid,
                Items = items
            });
        }

        /// <summary>清掉 Content 下不在分区名单里的节点（美术预放的示例卡等）。只在建页签 recycler 前清一次，
        /// 之后 Content 下的横幅克隆与虚拟化行节点都由 recycler 管理。</summary>
        private static void CleanContent(RectTransform content, params string[] keepNames)
        {
            for (var i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                var keep = false;
                for (var k = 0; k < keepNames.Length; k++)
                {
                    if (child.name == keepNames[k])
                    {
                        keep = true;
                        break;
                    }
                }

                if (!keep)
                {
                    child.SetParent(null, false);
                    Destroy(child.gameObject);
                }
            }
        }

        /// <summary>
        /// 怪物页按 MonsterType 分三区（横幅 + 卡片行交替）：Boss / 精英 / 普通；
        /// 对应类型没有怪物时连横幅一起隐藏。行虚拟化：视口内才生成，行与卡槽池化复用。
        /// </summary>
        private void BuildMonsterPage()
        {
            var scroll = UI.Get<ScrollRect>("MonsterSCView");
            var content = scroll != null ? scroll.content : null;
            if (content == null || _monsterPrefab == null)
            {
                return;
            }

            var bosses = new List<IllustratedBookEntry>();
            var elites = new List<IllustratedBookEntry>();
            var normals = new List<IllustratedBookEntry>();
            var entries = ViewModel.MonsterEntries;
            for (var i = 0; i < entries.Count; i++)
            {
                switch (entries[i].MonsterType)
                {
                    case MonsterType.Boss:
                        bosses.Add(entries[i]);
                        break;
                    case MonsterType.Elite:
                        elites.Add(entries[i]);
                        break;
                    default:
                        normals.Add(entries[i]);
                        break;
                }
            }

            if (_monsterRecycler == null)
            {
                CleanContent(content, UltimateBannerName, UltimateGridName,
                    EliteBannerName, EliteGridName, NormalBannerName, NormalGridName);
                _monsterRecycler = new CardRowRecycler<IllustratedBookEntry>();
                _monsterRecycler.Initialize(
                    scroll, FindGrid(content, UltimateGridName, EliteGridName, NormalGridName),
                    content.GetComponent<VerticalLayoutGroup>(), CreateMonsterSlot, BindMonsterSlot);
            }

            var sections = new List<CardRowRecycler<IllustratedBookEntry>.Section>(3);
            AddSection(sections, (RectTransform)content.Find(UltimateBannerName), FindGrid(content, UltimateGridName), bosses);
            AddSection(sections, (RectTransform)content.Find(EliteBannerName), FindGrid(content, EliteGridName), elites);
            AddSection(sections, (RectTransform)content.Find(NormalBannerName), FindGrid(content, NormalGridName), normals);
            _monsterRecycler.SetSections(sections);
        }

        /// <summary>向 UiCardPool 租用 PlayerItem 槽位（装饰幂等：0.9 缩放、藏 cardMask 与敌人攻/血块、
        /// 重挂点击），数据绑定走 BindMonsterSlot；视图关闭时槽位回收进池复用。</summary>
        private Component CreateMonsterSlot(Transform parent)
        {
            var card = UiCardPool.RentPlayerItem(_monsterPrefab, parent);
            if (card == null)
            {
                return null;
            }

            var go = card.gameObject;
            go.name = "MonsterSlot";
            go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            var bind = go.GetComponent<UIBind>();
            if (bind != null)
            {
                Destroy(bind);
            }

            // 图鉴条目不显示敌人攻/血块（BindMonsterSlot 不再调 SetAttack/SetHp，
            // 但两节点 prefab 默认激活，须在此关掉）
            HideEnemyStatBlocks(go.transform);
            var cardMask = FindDeep(card.transform, "cardMask");
            if (cardMask != null)
            {
                cardMask.gameObject.SetActive(false);
            }

            HookMonsterClick(card);
            _ownedSlots.Add(card);
            return card;
        }

        /// <summary>怪物槽数据绑定（行复用时反复调用）：敌人形态底图（enemycard 走
        /// MonsterConfig.BaseMap/HealthBar）、名字/头像（未解锁 ？？？+剪影口径），
        /// 敌人攻/血块不显示（CreateMonsterSlot 已隐藏节点，这里不能再调 SetAttack/SetHp
        /// ——那会在数值 >0 时把节点重新点亮），刷新选中态。</summary>
        private void BindMonsterSlot(Component slot, IllustratedBookEntry entry)
        {
            var card = (PlayerItem)slot;
            card.ApplyEnemyTheme(entry.Id);
            card.SetName(entry.Unlocked ? entry.Name : "？？？");
            card.SetPortrait(GetIcon(entry), locked: !entry.Unlocked);
            _entries[card] = entry;
            card.SetSelectLift(ViewModel.IsSelected(entry), HeroItem.SelectAnim, HeroItem.DefaultAnim);
        }

        /// <summary>隐藏敌人形态攻/血块容器（enemycardattack/enemycardheart，位于 enemycard 下，
        /// 须整树深查找）。PlayerItem.SetAttack/SetHp 会按显隐控制这两个节点，图鉴全程不用它们。</summary>
        private static void HideEnemyStatBlocks(Transform root)
        {
            var attack = FindDeep(root, "enemycardattack");
            if (attack != null)
            {
                attack.gameObject.SetActive(false);
            }

            var heart = FindDeep(root, "enemycardheart");
            if (heart != null)
            {
                heart.gameObject.SetActive(false);
            }
        }

        /// <summary>PlayerItem 预制体无 Button，运行时补透明射线 Image + Button（同 GameUIView.BindSeatClick）。
        /// 槽位池化复用：Button 可能带着上一任视图的闭包订阅，重挂前先清空。</summary>
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
            button.onClick.RemoveAllListeners();
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
            RefreshSelected(_collectRecycler);
            RefreshSelected(_relicRecycler);
            RefreshSelected(_monsterRecycler);
        }

        /// <summary>只刷当前视口内生成的卡槽（虚拟化：视口外的行在池中，无可见状态可刷）。</summary>
        private void RefreshSelected(CardRowRecycler<IllustratedBookEntry> recycler)
        {
            if (recycler == null)
            {
                return;
            }

            recycler.CollectActiveSlots(_activeSlots);
            for (var i = 0; i < _activeSlots.Count; i++)
            {
                var slot = _activeSlots[i];
                if (slot == null || !_entries.TryGetValue(slot, out var entry))
                {
                    continue;
                }

                if (slot is ItemCard itemCard)
                {
                    itemCard.SetSelected(ViewModel.IsSelected(entry));
                }
                else if (slot is PlayerItem playerItem)
                {
                    playerItem.SetSelectLift(
                        ViewModel.IsSelected(entry), HeroItem.SelectAnim, HeroItem.DefaultAnim);
                }
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

        /// <summary>
        /// 按需构建页签（幂等）：只加载该页需要的预制体与图标，再以虚拟化方式填充
        /// （视口内的卡片行才生成，行与卡槽池化复用）；打开瞬间只建当前页，其余页首次切换时才构建。
        /// </summary>
        private async void StartBuildTab(IllustratedBookTab tab)
        {
            if (!_builtTabs.Add(tab))
            {
                return;
            }

            try
            {
                switch (tab)
                {
                    case IllustratedBookTab.Collect:
                        await EnsureItemPrefab();
                        await LoadEntryIcons(ViewModel.CollectEntries);
                        BuildCollectPage();
                        break;
                    case IllustratedBookTab.Monster:
                        await EnsureMonsterPrefab();
                        BuildMonsterPage();
                        break;
                    default:
                        await EnsureItemPrefab();
                        await LoadEntryIcons(ViewModel.RelicEntries);
                        BuildRelicPage();
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
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

        /// <summary>首次显示浮层时异步加载 ItemTip 预制体（防重入），完成后若仍在显示态则补一次 ApplyTip。</summary>
        private async void EnsureTipLazy()
        {
            if (_tipLoading || _tip != null)
            {
                return;
            }

            _tipLoading = true;
            try
            {
                await EnsureTip();
            }
            catch (Exception)
            {
            }
            finally
            {
                _tipLoading = false;
            }

            if (_tip != null && ViewModel != null && ViewModel.ShowTip.Value)
            {
                ApplyTip();
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
                // ItemTip 预制体延迟到首次显示浮层时加载，完成后补一次应用
                if (ViewModel.ShowTip.Value)
                {
                    EnsureTipLazy();
                }

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
