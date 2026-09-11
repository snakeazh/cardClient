using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
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
    /// 天赋页面。Content 下的 Item 模板按天赋聚合结果克隆成网格；
    /// 点条目打开天赋详情；抽卡结果直接弹详情，翻卡演出在详情内（TalentDetail 的 Item 卡）。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentPopup, UILayer.Page, ResResourcePaths.TalentPopup)]
    public sealed class TalentPopupView : ViewBase<TalentPopupViewModel>
    {
        private const string TemplateName = "Item";
        private const string UltimateBannerName = "TitleBg";
        private const string UltimateGridName = "UltimateGrid";
        private const string RareBannerName = "RareBanner";
        private const string RareGridName = "RareGrid";
        private const string NormalBannerName = "NormalBanner";
        private const string NormalGridName = "NormalGrid";

        /// <summary>终极区收史诗/传说品质，稀有区收 Rare，其余进普通区；调整分组只改这里。</summary>
        private static readonly QualityType[] UltimateTypes = { QualityType.Epic, QualityType.Legend };

        private readonly List<ItemCard> _cards = new List<ItemCard>();
        private readonly Dictionary<ItemCard, TalentItem> _entries =
            new Dictionary<ItemCard, TalentItem>();
        private GameObject _template;

        protected override void OnBind()
        {
            BindBuyCost();
            BindRulesOpen();
            FillList();
        }

        protected override Task OnViewClose()
        {
            ClearCards();
            return Task.CompletedTask;
        }

        private void FillList()
        {
            var scroll = UI.Get<ScrollRect>("TalentSCView");
            if (scroll == null || scroll.content == null)
            {
                return;
            }

            var content = scroll.content;
            EnsureTemplate(content);
            ClearCards();
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

            FillSection(content.Find(UltimateBannerName), content.Find(UltimateGridName), ultimate);
            FillSection(content.Find(RareBannerName), content.Find(RareGridName), rare);
            FillSection(content.Find(NormalBannerName), content.Find(NormalGridName), normal);
        }

        private static bool IsUltimate(TalentItem item)
        {
            return Array.IndexOf(UltimateTypes, item.Type) >= 0;
        }

        /// <summary>对应品质没有天赋时连横幅一起隐藏；有则把卡牌克隆进该网格。</summary>
        private void FillSection(Transform banner, Transform grid, List<TalentItem> items)
        {
            if (grid == null)
            {
                return;
            }

            var visible = items.Count > 0;
            if (banner != null)
            {
                banner.gameObject.SetActive(visible);
            }

            grid.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var go = Instantiate(_template, grid, false);
                go.name = "Talent_" + item.Snapshot.TalentId;
                go.SetActive(true);
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
                // 品质染色 + card 节点品质边框（Altas/ItemBg），未解锁 Type 按 1 级行兜底
                card.ApplyQuality(item.Type);
                // 不清 card_icon：无配置 Icon 时保留预制体默认图
                card.SetName(item.Name);
                card.SetUnlocked(item.Snapshot.IsOwned);
                // 图标随解锁态染色：未解锁黑色剪影，解锁白色原色
                card.SetIconColor(item.Snapshot.IsOwned ? Color.white : Color.black);
                // 等级角标仅解锁态显示；未解锁卡面已有 Mask + ？？？ 占位
                card.SetLevel(item.Snapshot.IsOwned ? $"Lv.{item.Snapshot.Level}" : null);
                if (!string.IsNullOrEmpty(item.IconKey))
                {
                    ApplyCardIcon(card, item.IconKey);
                }
                card.Clicked += OnCardClicked;
                _entries[card] = item;
                _cards.Add(card);
            }
        }

        /// <summary>图标在 Altas/Talent 图集（sprite 名=TalentConfig.Icon）；图集未就绪或缺图保留预制体默认图。</summary>
        private void ApplyCardIcon(ItemCard card, string key)
        {
            if (card == null || ViewModel.Atlas == null)
            {
                return;
            }

            if (ViewModel.Atlas.TryGetSprite(ResResourcePaths.TalentAtlas, key, out var sprite) && sprite != null)
            {
                card.SetIcon(sprite);
            }
        }

        private void BindRulesOpen()
        {
            // DetailBtn 在预制体挂了 Button + UIBind；缺引用时退回按路径查找并补 Button。
            if (!UI.TryGet<Button>("DetailBtn", out var rulesBtn))
            {
                var node = transform.Find("Bg/TalentSCView/Viewport/Content/TitleBg/DetailBtn");
                if (node == null)
                {
                    return;
                }

                rulesBtn = node.GetComponent<Button>();
                if (rulesBtn == null)
                {
                    rulesBtn = node.gameObject.AddComponent<Button>();
                    rulesBtn.transition = Selectable.Transition.None;
                }
            }

            Binding.BindCommand(rulesBtn, ViewModel.OpenRulesCommand);
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

        private void ClearCards()
        {
            for (var i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null)
                {
                    Destroy(_cards[i].gameObject);
                }
            }

            _cards.Clear();
            _entries.Clear();
        }
    }
}
