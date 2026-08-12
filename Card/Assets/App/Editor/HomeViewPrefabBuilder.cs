using App.UI;
using Framework.UI.Binding;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace App.Editor
{
    /// <summary>
    /// Builds the Home prefab with UIReference / UIBind wiring.
    /// </summary>
    public static class HomeViewPrefabBuilder
    {
        private const string PrefabAssetPath = "Assets/Res/UI/Home.prefab";

        [MenuItem("App/UI/Generate Home Prefab")]
        public static void Generate()
        {
            EnsureDirectory("Assets/Res/UI");

            var root = new GameObject("Home", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rootRect = root.GetComponent<RectTransform>();
            StretchFull(rootRect);
            root.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);

            root.AddComponent<HomeView>();
            var ui = root.AddComponent<UIReference>();

            CreateText(root.transform, "Title", new Vector2(0, 120), 40, FontStyles.Bold);
            CreateText(root.transform, "Status", new Vector2(0, 40), 24, FontStyles.Normal);
            CreateButton(root.transform, "DialogButton", new Vector2(0, -40), "Show Dialog");

            ui.Collect();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Home prefab saved to {PrefabAssetPath}");
            Framework.Assets.Editor.ResAssetBundleBuilder.RebuildCurrentVersion();
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
        }

        private static void AddBind(GameObject go, string key, Component target)
        {
            var bind = go.GetComponent<UIBind>();
            if (bind == null)
            {
                bind = go.AddComponent<UIBind>();
            }

            var so = new SerializedObject(bind);
            so.FindProperty("_key").stringValue = key;
            so.FindProperty("_target").objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TMP_Text CreateText(Transform parent, string name, Vector2 pos, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(900, 80);
            rect.anchoredPosition = pos;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            AddBind(go, name, text);
            return text;
        }

        private static Button CreateButton(Transform parent, string name, Vector2 pos, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(220, 56);
            rect.anchoredPosition = pos;
            go.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.85f, 1f);

            var button = go.GetComponent<Button>();
            AddBind(go, name, button);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(go.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 22;
            text.color = Color.white;
            return button;
        }
    }
}
