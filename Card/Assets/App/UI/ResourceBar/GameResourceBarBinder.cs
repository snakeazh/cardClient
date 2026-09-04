using System.Collections.Generic;
using Framework.UI.Binding;
using Framework.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 局内 GameResourceBar：金币格复用 ResourceBarBinder，AddBtn 关闭。
    /// backBtn 绑 BackCommand，显隐跟 ShowBackBtn（商城/购买/结算隐藏）。
    /// </summary>
    internal static class GameResourceBarBinder
    {
        public static void Bind(
            BindingContext binding,
            Transform host,
            ResourceSlot gold,
            bool showBack,
            IRelayCommand backCommand = null,
            ObservableProperty<bool> backVisible = null)
        {
            if (binding == null || host == null || gold == null)
            {
                return;
            }

            var bar = Resolve(host);
            if (bar == null)
            {
                return;
            }

            bar.gameObject.SetActive(true);
            var top = bar.Find("TopArea") ?? FindDeep(bar, "TopArea") ?? bar;
            ResourceBarBinder.Bind(binding, top, new List<ResourceSlot> { gold }, HideAddBtn);
            BindBack(binding, bar, showBack, backCommand, backVisible);
        }

        public static void Bind(
            BindingContext binding,
            Transform host,
            ObservableProperty<string> goldText,
            bool showBack,
            IRelayCommand backCommand = null,
            ObservableProperty<bool> backVisible = null)
        {
            if (goldText == null)
            {
                return;
            }

            var slot = new ResourceSlot(ResourceKind.Gold);
            binding.Add(goldText.Subscribe(value => slot.Amount.Value = value ?? "0"));
            Bind(binding, host, slot, showBack, backCommand, backVisible);
        }

        public static Transform Resolve(Transform host)
        {
            if (host == null)
            {
                return null;
            }

            if (host.name == "GameResourceBar" || host.name == "ResourceBar")
            {
                return host;
            }

            return host.Find("GameResourceBar")
                   ?? FindDeep(host, "GameResourceBar")
                   ?? host.Find("ResourceBar")
                   ?? FindDeep(host, "ResourceBar");
        }

        private static void BindBack(
            BindingContext binding,
            Transform bar,
            bool showBack,
            IRelayCommand backCommand,
            ObservableProperty<bool> backVisible)
        {
            var back = bar.Find("backBtn") ?? FindDeep(bar, "backBtn");
            if (back == null)
            {
                return;
            }

            if (!showBack)
            {
                back.gameObject.SetActive(false);
                return;
            }

            back.gameObject.SetActive(true);
            var button = back.GetComponent<Button>();
            if (button != null && backCommand != null)
            {
                binding.BindCommand(button, backCommand);
            }

            if (backVisible != null)
            {
                binding.BindActive(back.gameObject, backVisible);
            }
        }

        private static void HideAddBtn(Transform item, ResourceSlot _)
        {
            var addBtn = item.Find("AddBtn") ?? FindDeep(item, "AddBtn");
            if (addBtn != null)
            {
                addBtn.gameObject.SetActive(false);
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
