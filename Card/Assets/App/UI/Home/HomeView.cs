using App.Resources;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Home page view. Nodes are resolved via UIReference / UIBind.
    /// </summary>
    [AutoScreen(AppScreenIds.Home, UILayer.Page, ResResourcePaths.Home)]
    public sealed class HomeView : ViewBase<HomeViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindText(UI.Get<TMP_Text>("Title"), ViewModel.Title);
            Binding.BindText(UI.Get<TMP_Text>("Status"), ViewModel.Status);
            BindDifficultyList();
        }

        private void BindDifficultyList()
        {
            var listGo = UI.GetGameObject("DifficultyList");
            EnsureListLayout(listGo);

            var template = UI.Get<Button>("DifficultyBtn");
            template.gameObject.SetActive(false);

            var items = ViewModel.Difficulties;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var go = Instantiate(template.gameObject, listGo.transform, false);
                go.name = $"DifficultyBtn_{item.Difficulty}";
                go.SetActive(true);

                var bind = go.GetComponent<UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                Binding.BindCommand(go.GetComponent<Button>(), item.SelectCommand);

                var label = go.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    Binding.BindText(label, item.Label);
                }
            }
        }

        private static void EnsureListLayout(GameObject listGo)
        {
            var layout = listGo.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = listGo.AddComponent<VerticalLayoutGroup>();
            }

            layout.spacing = 12f;
            layout.padding = new RectOffset(0, 0, 8, 8);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = listGo.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = listGo.AddComponent<ContentSizeFitter>();
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
}
