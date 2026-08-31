using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
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
    /// 遗物列表弹窗。ItemSCView/Content 自带一个 RemainItem 模板，按 GameBalance.MaxRelics
    /// 克隆固定格数：已持有遗物显示品质底色与图标，空余格子由 RemainItem 显示"空"。
    /// CloseBtn 关闭弹窗。纯查看，点击不接出售（ShopDetail 的非购买态是卖）。
    /// </summary>
    [AutoScreen(AppScreenIds.RemainListPop, UILayer.Popup, ResResourcePaths.RemainListPop)]
    public sealed class RemainListPopView : ViewBase<RemainListPopViewModel>
    {
        private readonly List<RemainItem> _items = new List<RemainItem>(GameBalance.MaxRelics);
        private GameObject _template;

        protected override void OnBind()
        {
            var closeBtn = GetNode<Button>("CloseBtn");
            if (closeBtn != null)
            {
                Binding.BindCommand(closeBtn, ViewModel.CloseCommand);
            }

            BindResourceBar();
            EnsureItems();
            Binding.Add(ViewModel.ListVersion.Subscribe(_ => RefreshItems(), emitCurrent: true));
        }

        protected override Task OnViewClose()
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null)
                {
                    Destroy(_items[i].gameObject);
                }
            }

            _items.Clear();
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

        private void EnsureItems()
        {
            if (!UI.TryGet<ScrollRect>("ItemSCView", out var scroll) || scroll.content == null)
            {
                return;
            }

            var content = scroll.content;
            if (content.childCount > 0)
            {
                _template = content.GetChild(0).gameObject;
            }

            if (_template == null)
            {
                AppLog.Warn(LogChannel.UI, "RemainListPop Content 下缺少 RemainItem 模板", this);
                return;
            }

            if (_template.GetComponent<RemainItem>() == null)
            {
                AppLog.Warn(LogChannel.UI, "RemainListPop 模板上缺少 RemainItem 组件", _template);
                return;
            }

            _template.SetActive(false);
            for (var i = 0; i < GameBalance.MaxRelics; i++)
            {
                var go = Instantiate(_template, content, false);
                go.name = "RemainItem_" + i;
                go.SetActive(true);
                var item = go.GetComponent<RemainItem>();
                _items.Add(item);
            }
        }

        private void RefreshItems()
        {
            var owned = ViewModel.Session.Run.RelicConfigIds;
            for (var i = 0; i < _items.Count; i++)
            {
                RelicConfig relic = null;
                if (i < owned.Count)
                {
                    relic = RelicConfig.Get(owned[i]);
                }

                _items[i].Bind(relic, relic != null ? ViewModel.GetRelicIcon(relic) : null);
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
    }
}
