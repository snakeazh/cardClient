using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    [AutoScreen(AppScreenIds.MainResource, UILayer.Resource, ResResourcePaths.MainResource)]
    public sealed class MainResourceView : ViewBase<MainResourceViewModel>
    {
        protected override void OnBind()
        {
            var top = ResolveTopArea();
            if (top == null)
            {
                return;
            }

            ResourceBarBinder.Bind(Binding, top, ViewModel.Slots);
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
