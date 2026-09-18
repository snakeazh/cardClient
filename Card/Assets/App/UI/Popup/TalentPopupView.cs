using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using App.Guide;
using App.Item;
using App.Resources;
using App.UI.List;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋页面。按品质三区（终极=史诗/传说、稀有、普通，横幅+卡片行交替），
    /// 行虚拟化填充：视口内的卡片行才生成，行与卡槽池化复用（同图鉴 IllustratedBookPop 的 CardRowRecycler）。
    /// 点条目打开天赋详情；抽卡结果直接弹详情，翻卡演出在详情内（TalentDetail 的 Item 卡）。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentPopup, UILayer.Page, ResResourcePaths.TalentPopup)]
    public sealed class TalentPopupView : ViewBase<TalentPopupViewModel>
    {
        private const string TemplateName = "Item";
        private const string DetailBtnName = "DetailBtn";
        private const string UltimateBannerName = "TitleBg";
        private const string UltimateGridName = "UltimateGrid";
        private const string RareBannerName = "RareBanner";
        private const string RareGridName = "RareGrid";
        private const string NormalBannerName = "NormalBanner";
        private const string NormalGridName = "NormalGrid";

        /// <summary>终极区收史诗/传说品质，稀有区收 Rare，其余进普通区；调整分组只改这里。</summary>
        private static readonly QualityType[] UltimateTypes = { QualityType.Epic, QualityType.Legend };

        // 池化复用：卡槽对象反复重绑不同条目，_entries 覆盖式写入（同图鉴口径）
        private readonly Dictionary<ItemCard, TalentItem> _entries =
            new Dictionary<ItemCard, TalentItem>();
        private GameObject _template;
        private CardRowRecycler<TalentItem> _recycler;
        private GuideTargetRegistry _guideTargets;
        private readonly List<string> _guideTargetIds = new List<string>(2);

        protected override Task OnViewOpen()
        {
            GuideSignals.NotifyTalentPopupOpen();
            return Task.CompletedTask;
        }

        protected override void OnBind()
        {
            BindBuyCost();
            FillList();
            RegisterGuideTargets();
        }

        protected override Task OnViewClose()
        {
            UnregisterGuideTargets();
            GuideSignals.NotifyTalentPopupClosed();
            if (AppServices.IsReady)
            {
                var guide = AppServices.Resolve<IGuideService>();
                if (guide != null &&
                    guide.IsRunning &&
                    guide.CurrentGroupId == GuideGroupIds.FirstTalentDraw)
                {
                    guide.Abort();
                }
            }

            return Task.CompletedTask;
        }

        private void RegisterGuideTargets()
        {
            UnregisterGuideTargets();
            if (!AppServices.IsReady)
            {
                return;
            }

            var buyGo = UI.GetGameObject("BuyBtn");
            if (buyGo == null)
            {
                return;
            }

            _guideTargets = AppServices.Resolve<GuideTargetRegistry>();
            _guideTargets.RegisterUi(GuideTargetIds.TalentBuyBtn, (RectTransform)buyGo.transform);
            _guideTargetIds.Add(GuideTargetIds.TalentBuyBtn);
        }

        private void UnregisterGuideTargets()
        {
            if (_guideTargets != null && _guideTargetIds.Count > 0)
            {
                _guideTargets.UnregisterAll(_guideTargetIds);
            }

            _guideTargetIds.Clear();
        }

        /// <summary>按品质三区做行虚拟化填充；首次建 recycler（清 Content 杂项、停用布局组件），
        /// 之后（ListVersion 变化）仅重建分区数据，行池与卡槽复用。</summary>
        private void FillList()
        {
            var scroll = UI.Get<ScrollRect>("TalentSCView");
            var content = scroll != null ? scroll.content : null;
            if (content == null)
            {
                return;
            }

            EnsureTemplate(content);
            if (_template == null)
            {
                return;
            }

            var ultimate = new List<TalentItem>();
            var rare = new List<TalentItem>();
            var normal = new List<TalentItem>();
            var items = ViewModel.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (IsUltimate(items[i]))
                {
                    ultimate.Add(items[i]);
                }
                else if (items[i].Type == QualityType.Rare)
                {
                    rare.Add(items[i]);
                }
                else
                {
                    normal.Add(items[i]);
                }
            }

            if (_recycler == null)
            {
                CleanContent(content, UltimateBannerName, UltimateGridName,
                    RareBannerName, RareGridName, NormalBannerName, NormalGridName, TemplateName);
                _recycler = new CardRowRecycler<TalentItem>();
                _recycler.Initialize(
                    scroll, FindGrid(content, UltimateGridName, RareGridName, NormalGridName),
                    content.GetComponent<VerticalLayoutGroup>(), CreateCardSlot, BindCardSlot);
            }

            var sections = new List<CardRowRecycler<TalentItem>.Section>(3);
            AddSection(sections, content, UltimateBannerName, UltimateGridName, ultimate);
            AddSection(sections, content, RareBannerName, RareGridName, rare);
            AddSection(sections, content, NormalBannerName, NormalGridName, normal);
            _recycler.SetSections(sections);
            // 终极区横幅被 recycler 克隆显示（原件隐藏），规则按钮到克隆件上重绑
            BindRulesOpen(content);
        }

        private static bool IsUltimate(TalentItem item)
        {
            return Array.IndexOf(UltimateTypes, item.Type) >= 0;
        }

        /// <summary>分区 = 横幅 + Grid 模板 + 条目；空区把横幅与 Grid 模板一并隐藏
        /// （recycler 空区不出横幅，但也不会隐藏模板，须在此处理）。</summary>
        private static void AddSection(
            List<CardRowRecycler<TalentItem>.Section> sections,
            RectTransform content, string bannerName, string gridName, List<TalentItem> items)
        {
            var banner = content.Find(bannerName);
            var grid = content.Find(gridName);
            var visible = items.Count > 0;
            if (banner != null)
            {
                banner.gameObject.SetActive(visible);
            }

            if (grid != null)
            {
                grid.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            sections.Add(new CardRowRecycler<TalentItem>.Section
            {
                BannerTemplate = (RectTransform)banner,
                GridTemplate = grid != null ? grid.GetComponent<GridLayoutGroup>() : null,
                Items = items
            });
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

        /// <summary>清掉 Content 下不在分区名单里的节点（美术预放的示例卡等）。只在首次建 recycler 前清一次，
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

        /// <summary>克隆 Item 模板做卡槽（一次性装饰：关阴影动画、订阅点击），数据绑定走 BindCardSlot。</summary>
        private Component CreateCardSlot(Transform parent)
        {
            var go = Instantiate(_template, parent, false);
            go.name = "Talent_" + go.GetInstanceID();
            go.SetActive(true);
            var bind = go.GetComponent<UIBind>();
            if (bind != null)
            {
                Destroy(bind);
            }

            var card = go.GetComponent<ItemCard>();
            if (card == null)
            {
                return null;
            }

            card.SetShadowVisible(false);
            card.SetAnimationEnabled(false);
            card.Clicked += OnCardClicked;
            return card;
        }

        /// <summary>卡槽数据绑定（行复用时反复调用）：品质染色 + card 节点品质边框（Altas/ItemBg），
        /// 解锁态（未解锁黑剪影+？？？、无等级角标、无品质特效），图标取 Altas/Talent 图集。</summary>
        private void BindCardSlot(Component slot, TalentItem item)
        {
            var card = (ItemCard)slot;
            card.ApplyQuality(item.Type);
            // 已解锁卡常驻品质特效（循环粒子）；未解锁是黑剪影+？？？，不亮。
            // ShowQualityFx 先清场再点亮，槽位复用切品质不叠加
            card.ShowQualityFx(item.Snapshot.IsOwned ? item.Type : QualityType.Ordinary);
            card.SetName(item.Name);
            card.SetUnlocked(item.Snapshot.IsOwned);
            // 图标随解锁态染色：未解锁黑色剪影，解锁白色原色
            card.SetIconColor(item.Snapshot.IsOwned ? Color.white : Color.black);
            // 等级角标仅解锁态显示；未解锁卡面已有 Mask + ？？？ 占位
            card.SetLevel(item.Snapshot.IsOwned ? $"Lv.{item.Snapshot.Level}" : null);
            // 图标在 Altas/Talent 图集（sprite 名=TalentConfig.Icon）；缺配置/缺图时隐藏图标节点（背景框仍显示）
            card.SetIcon(ResolveIcon(item));
            _entries[card] = item;
        }

        /// <summary>图标在 Altas/Talent 图集（sprite 名=TalentConfig.Icon）；图集未就绪、缺配置或缺图返回 null。</summary>
        private Sprite ResolveIcon(TalentItem item)
        {
            if (string.IsNullOrEmpty(item.IconKey) || ViewModel.Atlas == null)
            {
                return null;
            }

            return ViewModel.Atlas.TryGetSprite(ResResourcePaths.TalentAtlas, item.IconKey, out var sprite)
                ? sprite
                : null;
        }

        /// <summary>规则按钮（DetailBtn）在终极区横幅内。横幅由 recycler 克隆显示、原件隐藏，
        /// 故每次 SetSections 后在克隆件上重绑；终极区为空时克隆不存在，兜底绑原件（隐藏态，无实际影响）。
        /// 克隆件生命周期由 recycler 管理，不走 Binding.BindCommand——累积的 dispose 回调会在 View
        /// 销毁时对已销毁克隆 RemoveListener 抛 MissingReferenceException。</summary>
        private void BindRulesOpen(RectTransform content)
        {
            Button rulesBtn = null;
            for (var i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (child.name != UltimateBannerName || !child.gameObject.activeSelf)
                {
                    continue;
                }

                var node = child.Find(DetailBtnName);
                rulesBtn = node != null ? node.GetComponent<Button>() : null;
                break;
            }

            if (rulesBtn == null)
            {
                var fallback = content.Find(UltimateBannerName)?.Find(DetailBtnName);
                rulesBtn = fallback != null ? fallback.GetComponent<Button>() : null;
            }

            if (rulesBtn == null)
            {
                return;
            }

            rulesBtn.onClick.RemoveAllListeners();
            rulesBtn.onClick.AddListener(OnRulesClicked);
        }

        private void OnRulesClicked()
        {
            ViewModel.OpenRulesCommand.Execute();
        }

        private void BindBuyCost()
        {
            var buyBtn = UI.GetGameObject("BuyBtn");
            var num = buyBtn != null ? buyBtn.transform.Find("Num") : null;
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                Binding.BindText(text, ViewModel.BuyCostText, value => $"×{value}");
            }

            if (buyBtn != null)
            {
                var button = buyBtn.GetComponent<Button>();
                if (button == null)
                {
                    button = buyBtn.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                }

                Binding.BindCommand(button, ViewModel.BuyCommand);
            }

            // 购买后刷新列表，让新天赋解除锁定显示；初始填充由 OnBind 的 FillList 负责
            Binding.Add(ViewModel.ListVersion.Subscribe(_ => FillList(), false));
        }

        private void EnsureTemplate(RectTransform content)
        {
            if (_template != null)
            {
                return;
            }

            for (var i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (child.name != TemplateName)
                {
                    continue;
                }

                _template = child.gameObject;
                _template.SetActive(false);
                return;
            }
        }

        private void OnCardClicked(ItemCard card)
        {
            if (_entries.TryGetValue(card, out var item))
            {
                _ = ViewModel.OpenDetail(item);
            }
        }
    }
}
