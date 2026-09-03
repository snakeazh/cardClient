using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    [AutoScreen(AppScreenIds.MainResource, UILayer.Resource, ResResourcePaths.MainResource)]
    public sealed class MainResourceView : ViewBase<MainResourceViewModel>
    {
        /// <summary>Common 目录不打图集，图标在 Inspector 手动引用。</summary>
        [SerializeField] private Sprite _goldIcon;
        [SerializeField] private Sprite _energyIcon;

        protected override void OnBind()
        {
            var top = ResolveTopArea();
            if (top == null)
            {
                return;
            }

            ResourceBarBinder.Bind(Binding, top, ViewModel.Slots, BindShopEntry);
            ViewModel.SetIcons(_goldIcon, _energyIcon);
        }

        /// <summary>资源格上的 AddBtn 打开广告商店；局内金币格（RunGold）不显示入口。</summary>
        private void BindShopEntry(Transform item, ResourceSlot slot)
        {
            var addBtn = item.Find("AddBtn");
            if (addBtn == null)
            {
                return;
            }

            if (slot.Kind == ResourceKind.RunGold)
            {
                addBtn.gameObject.SetActive(false);
                return;
            }

            var button = addBtn.GetComponent<Button>();
            if (button != null)
            {
                Binding.BindCommand(button, ViewModel.OpenShopCommand);
            }
        }

        private Transform ResolveTopArea()
        {
            if (UI != null && UI.TryGet<RectTransform>("TopArea", out var top) && top != null)
            {
                return top;
            }

            var bar = transform.Find("ResourceBar") ?? FindDeep(transform, "ResourceBar");
            if (bar == null)
            {
                return FindDeep(transform, "TopArea");
            }

            return bar.Find("TopArea") ?? FindDeep(bar, "TopArea") ?? bar;
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
