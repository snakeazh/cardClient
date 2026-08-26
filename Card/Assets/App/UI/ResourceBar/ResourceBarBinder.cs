using System.Collections.Generic;
using Framework.UI.Binding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    internal static class ResourceBarBinder
    {
        public static void Bind(BindingContext binding, Transform topArea, IReadOnlyList<ResourceSlot> slots)
        {
            if (binding == null || topArea == null || slots == null || slots.Count == 0)
            {
                return;
            }

            var items = CollectItems(topArea);
            var template = items.Count > 0 ? items[0] : null;
            if (template == null)
            {
                return;
            }

            while (items.Count < slots.Count)
            {
                var clone = Object.Instantiate(template.gameObject, topArea);
                clone.name = "ResourceItem";
                var bind = clone.GetComponent<UIBind>();
                if (bind != null)
                {
                    Object.Destroy(bind);
                }

                items.Add(clone.transform);
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null)
                {
                    continue;
                }

                if (i >= slots.Count)
                {
                    item.gameObject.SetActive(false);
                    continue;
                }

                BindItem(binding, item, slots[i]);
            }
        }

        private static void BindItem(BindingContext binding, Transform item, ResourceSlot slot)
        {
            item.gameObject.SetActive(true);
            binding.BindActive(item.gameObject, slot.Visible);

            var num = FindChild(item, "Num");
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                binding.BindText(text, slot.Amount);
            }

            var icon = FindChild(item, "Icon");
            var image = icon != null ? icon.GetComponent<Image>() : null;
            if (image == null)
            {
                return;
            }

            binding.Add(slot.Icon.Subscribe(sprite =>
            {
                if (sprite != null)
                {
                    image.sprite = sprite;
                }
            }));
        }

        private static List<Transform> CollectItems(Transform topArea)
        {
            var items = new List<Transform>(topArea.childCount);
            for (var i = 0; i < topArea.childCount; i++)
            {
                var child = topArea.GetChild(i);
                if (child.name.StartsWith("ResourceItem"))
                {
                    items.Add(child);
                }
            }

            return items;
        }

        private static Transform FindChild(Transform root, string name)
        {
            var child = root.Find(name);
            if (child != null)
            {
                return child;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
