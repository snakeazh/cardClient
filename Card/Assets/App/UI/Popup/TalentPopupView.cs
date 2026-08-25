using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// 天赋弹窗。Content 下的 Item 模板按天赋聚合结果克隆成网格；
    /// 点条目打开天赋详情，点遮罩关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentPopup, UILayer.Popup, ResResourcePaths.TalentPopup)]
    public sealed class TalentPopupView : ViewBase<TalentPopupViewModel>
    {
        private const string TemplateName = "Item";

        private readonly List<ItemCard> _cards = new List<ItemCard>();
        private readonly Dictionary<ItemCard, TalentItem> _entries =
            new Dictionary<ItemCard, TalentItem>();
        private GameObject _template;

        protected override void OnBind()
        {
            BindOverlayClose();
            BindBuyCost();
            FillList();
        }

        protected override Task OnViewClose()
        {
            ClearCards();
            return Task.CompletedTask;
        }

        private void BindOverlayClose()
        {
            var overlay = GetComponent<Button>();
            if (overlay == null)
            {
                overlay = gameObject.AddComponent<Button>();
                overlay.transition = Selectable.Transition.None;
            }

            Binding.BindCommand(overlay, ViewModel.CloseCommand);
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

            var items = ViewModel.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var go = Instantiate(_template, content, false);
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
                card.Bind(item.Name, null, item.Snapshot.IsOwned);
                card.Clicked += OnCardClicked;
                _entries[card] = item;
                _cards.Add(card);
            }
        }

        private void BindBuyCost()
        {
            var buyBtn = UI.GetGameObject("BuyBtn");
            var num = buyBtn != null ? buyBtn.transform.Find("Num") : null;
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                Binding.BindText(text, ViewModel.BuyCostText);
            }
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
