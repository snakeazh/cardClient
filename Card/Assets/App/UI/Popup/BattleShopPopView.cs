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
    /// 通关商店弹窗。sellHor 展示货架，MineHor 展示已购装备，点击弹出 ShopDetail 购买或出售。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleShopPop, UILayer.Popup, ResResourcePaths.BattleShopPop)]
    public sealed class BattleShopPopView : ViewBase<BattleShopPopViewModel>
    {
        private readonly List<EquipShopIcon> _sellItems = new List<EquipShopIcon>();
        private readonly List<EquipShopIcon> _mineItems = new List<EquipShopIcon>();
        private EquipShopIcon _sellTemplate;
        private EquipShopIcon _mineTemplate;

        protected override void OnBind()
        {
            Binding.BindText(GetNode<TMP_Text>("RefreshGooldNum"), ViewModel.RefreshGoldNum);
            Binding.BindCommand(GetNode<Button>("RefreshBtn"), ViewModel.RefreshCommand);
            Binding.BindCommand(GetNode<Button>("NextStageBtn"), ViewModel.NextStageCommand);
            Binding.BindCommand(GetNode<Button>("CloseBtn"), ViewModel.CloseCommand);
            BindResourceBar();

            EnsureSellItems();
            EnsureMineItems();
            Binding.Add(ViewModel.ShopRevision.Subscribe(_ => RefreshItems(), emitCurrent: true));
        }

        private void BindResourceBar()
        {
            var bar = transform.Find("ResourceBar");
            if (bar == null)
            {
                bar = FindDeep(transform, "ResourceBar");
            }

            if (bar == null)
            {
                return;
            }

            bar.gameObject.SetActive(true);
            var top = bar.Find("TopArea") ?? FindDeep(bar, "TopArea") ?? bar;
            Transform goldItem = null;
            for (var i = 0; i < top.childCount; i++)
            {
                var child = top.GetChild(i);
                if (!child.name.StartsWith("ResourceItem"))
                {
                    continue;
                }

                if (goldItem == null)
                {
                    goldItem = child;
                    child.gameObject.SetActive(true);
                    continue;
                }

                child.gameObject.SetActive(false);
            }

            if (goldItem == null)
            {
                return;
            }

            var num = goldItem.Find("Num") ?? FindDeep(goldItem, "Num");
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                Binding.BindRollingText(text, ViewModel.GoldText);
            }
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
            var root = UI.GetGameObject("sellHor").transform;
            _sellTemplate = root.GetComponentInChildren<EquipShopIcon>(true);
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
            _mineTemplate = root.GetComponentInChildren<EquipShopIcon>(true);
            if (_mineTemplate == null)
            {
                return;
            }

            _mineTemplate.gameObject.SetActive(false);
        }

        private EquipShopIcon CloneItem(
            EquipShopIcon template,
            Transform parent,
            string name,
            Action<EquipShopIcon> onClick)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(false);
            var bind = go.GetComponent<UIBind>();
            if (bind != null)
            {
                UnityEngine.Object.Destroy(bind);
            }

            var item = go.GetComponent<EquipShopIcon>();
            item.BindClick(onClick);
            return item;
        }

        private void OnSellClicked(EquipShopIcon item)
        {
            if (item == null || item.RelicConfigId <= 0)
            {
                return;
            }

            _ = ViewModel.OpenDetail(item.RelicConfigId, buying: true);
        }

        private void OnMineClicked(EquipShopIcon item)
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
                    item.gameObject.SetActive(false);
                    continue;
                }

                var relic = RelicConfig.Get(offers[i]);
                if (relic == null)
                {
                    item.gameObject.SetActive(false);
                    continue;
                }

                item.gameObject.SetActive(true);
                item.Bind(relic, ViewModel.GetRelicIcon(relic));
            }
        }

        private void RefreshMineItems()
        {
            if (_mineTemplate == null)
            {
                return;
            }

            var owned = ViewModel.Session.Run.RelicConfigIds;
            var shown = owned.Count;
            var root = _mineTemplate.transform.parent;
            while (_mineItems.Count < shown)
            {
                _mineItems.Add(CloneItem(_mineTemplate, root, $"mineItem_{_mineItems.Count}", OnMineClicked));
            }

            for (var i = 0; i < _mineItems.Count; i++)
            {
                var item = _mineItems[i];
                if (i >= shown)
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

                item.gameObject.SetActive(true);
                item.Bind(relic, ViewModel.GetRelicIcon(relic));
            }
        }
    }
}
