using Framework.UI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI.Editor
{
    /// <summary>
    /// Builds the UIRoot prefab. Runtime loads it from Resources instead of creating nodes in code.
    /// </summary>
    public static class UIRootPrefabBuilder
    {
        private const string PrefabAssetPath = "Assets/Res/UI/UIRoot.prefab";

        [MenuItem("Framework/UI/Generate UIRoot Prefab")]
        public static void Generate()
        {
            EnsureDirectory("Assets/Res/UI");

            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var rootRect = root.GetComponent<RectTransform>();
            StretchFull(rootRect);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var uiRoot = root.AddComponent<UIRoot>();
            var hud = CreateLayer(root.transform, UILayer.Hud, 100);
            var page = CreateLayer(root.transform, UILayer.Page, 200);
            var popup = CreateLayer(root.transform, UILayer.Popup, 300);
            var topMost = CreateLayer(root.transform, UILayer.TopMost, 400);

            var so = new SerializedObject(uiRoot);
            so.FindProperty("_hudLayer").objectReferenceValue = hud;
            so.FindProperty("_pageLayer").objectReferenceValue = page;
            so.FindProperty("_popupLayer").objectReferenceValue = popup;
            so.FindProperty("_topMostLayer").objectReferenceValue = topMost;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"UIRoot prefab saved to {PrefabAssetPath}");
            Framework.Assets.Editor.ResAssetBundleBuilder.RebuildCurrentVersion();
        }

        private static RectTransform CreateLayer(Transform parent, UILayer layer, int sortingOrder)
        {
            var go = new GameObject(layer.ToString(), typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            StretchFull(rect);

            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            return rect;
        }

        private static void EnsureDirectory(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
