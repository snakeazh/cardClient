using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI.List
{
    /// <summary>
    /// Helper to build a vertical ScrollRect + RecycleListView at runtime.
    /// </summary>
    public static class RecycleListFactory
    {
        public static RecycleListView Create(
            Transform parent,
            GameObject itemPrefab,
            Vector2 size,
            float itemHeight = 80f,
            float spacing = 0f)
        {
            var root = new GameObject("RecycleList", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.sizeDelta = size;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(root.transform, false);
            StretchFull(viewportRect);
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.SetParent(viewport.transform, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var list = root.AddComponent<RecycleListView>();
            list.Initialize(scroll, itemPrefab, itemHeight, spacing);
            return list;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
