using System;
using System.Collections.Generic;
using App.Config;
using App.Game;
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
    /// 通关商店弹窗。sellItem / mineItem 是隐藏模板，克隆到 sellHor 与 MineHor。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleShopPop, UILayer.Popup, ResResourcePaths.BattleShopPop)]
    public sealed class BattleShopPopView : ViewBase<BattleShopPopViewModel>
    {
        private readonly List<ShopItem> _sellItems = new List<ShopItem>();
        private readonly List<ShopItem> _mineItems = new List<ShopItem>();
        private GameObject _sellTemplate;
        private GameObject _mineTemplate;
        private Transform _mineContent;

        protected override void OnBind()
        {
            Binding.BindText(GetNode<TMP_Text>("RefreshGooldNum"), ViewModel.RefreshGoldNum);
            Binding.BindText(GetNode<TMP_Text>("refreshNum"), ViewModel.RefreshNum);
            Binding.BindCommand(GetNode<Button>("RefreshBtn"), ViewModel.RefreshCommand);
            Binding.BindCommand(GetNode<Button>("NextStageBtn"), ViewModel.NextStageCommand);

            EnsureSellItems();
            EnsureMineItems();
            Binding.Add(ViewModel.ShopRevision.Subscribe(_ => RefreshItems(), emitCurrent: true));
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

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void EnsureSellItems()
        {
            _sellTemplate = UI.GetGameObject("sellItem");
            _sellTemplate.SetActive(false);
            var root = UI.GetGameObject("sellHor").transform;
            _sellItems.Clear();
            for (var i = 0; i < GameBalance.ShopOfferCount; i++)
            {
                _sellItems.Add(CloneItem(_sellTemplate, root, $"sellItem_{i}", OnSellClicked));
            }
        }

        private void EnsureMineItems()
        {
            _mineTemplate = UI.GetGameObject("mineItem");
            _mineTemplate.SetActive(false);
            var mineHor = UI.GetGameObject("MineHor").transform;
            _mineContent = FindMineContent(mineHor);
        }

        private static Transform FindMineContent(Transform mineHor)
        {
            var scroll = mineHor.GetComponent<ScrollRect>();
            if (scroll != null && scroll.content != null)
            {
                return scroll.content;
            }

            var named = FindDeep(mineHor, "Content");
            return named != null ? named : mineHor;
        }

        private ShopItem CloneItem(
            GameObject template,
            Transform parent,
            string name,
            Action<ShopItem> onClick,
            bool wrapForGrid = false)
        {
            var host = parent;
            if (wrapForGrid)
            {
                var slot = new GameObject(name, typeof(RectTransform));
                slot.layer = parent.gameObject.layer;
                var rt = slot.GetComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.localScale = Vector3.one;
                host = rt;
            }

            var go = UnityEngine.Object.Instantiate(template, host, false);
            go.name = wrapForGrid ? template.name : name;
            go.SetActive(false);
            var binds = go.GetComponentsInChildren<UIBind>(true);
            for (var i = 0; i < binds.Length; i++)
            {
                UnityEngine.Object.Destroy(binds[i]);
            }

            var item = go.GetComponentInChildren<ShopItem>(true);
            item.BindClick(onClick);
            return item;
        }

        private void OnSellClicked(ShopItem item)
        {
            if (item == null || item.RelicConfigId <= 0)
            {
                return;
            }

            _ = ViewModel.OpenDetail(item.RelicConfigId, buying: true);
        }

        private void OnMineClicked(ShopItem item)
        {
            if (item == null || item.RelicConfigId <= 0)
            {
                return;
            }

            _ = ViewModel.OpenDetail(item.RelicConfigId, buying: false);
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
                    SetSlotActive(item, false);
                    continue;
                }

                var relic = RelicConfig.Get(offers[i]);
                if (relic == null)
                {
                    SetSlotActive(item, false);
                    continue;
                }

                SetSlotActive(item, true);
                item.Bind(relic, ViewModel.GetRelicIcon(relic), buyPrice: ViewModel.Session.EffectiveBuyPrice(relic.Id));
            }
        }

        private void RefreshMineItems()
        {
            if (_mineTemplate == null || _mineContent == null)
            {
                return;
            }

            var owned = ViewModel.Session.Run.RelicConfigIds;
            var shown = owned.Count;
            while (_mineItems.Count < shown)
            {
                _mineItems.Add(CloneItem(_mineTemplate, _mineContent, $"mineItem_{_mineItems.Count}", OnMineClicked, wrapForGrid: true));
            }

            for (var i = 0; i < _mineItems.Count; i++)
            {
                var item = _mineItems[i];
                if (i >= shown)
                {
                    SetSlotActive(item, false, _mineContent);
                    continue;
                }

                var relic = RelicConfig.Get(owned[i]);
                if (relic == null)
                {
                    SetSlotActive(item, false, _mineContent);
                    continue;
                }

                SetSlotActive(item, true, _mineContent);
                item.Bind(relic, ViewModel.GetRelicIcon(relic), forSale: false);
            }
        }

        private static void SetSlotActive(ShopItem item, bool active, Transform layoutRoot = null)
        {
            if (item == null)
            {
                return;
            }

            var t = item.transform;
            while (t != null && t != layoutRoot)
            {
                t.gameObject.SetActive(active);
                var parent = t.parent;
                if (parent == null || parent == layoutRoot)
                {
                    break;
                }

                t = parent;
                if (layoutRoot == null)
                {
                    t.gameObject.SetActive(active);
                    break;
                }
            }
        }
    }
}
