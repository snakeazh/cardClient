using Framework.UI.Binding;
using Framework.UI.Dialog;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI.Editor
{
    /// <summary>
    /// Builds the ConfirmDialog prefab with UIReference / UIBind wiring.
    /// </summary>
    public static class ConfirmDialogPrefabBuilder
    {
        private const string PrefabAssetPath = "Assets/Res/UI/ConfirmDialog.prefab";

        [MenuItem("Framework/UI/Generate Confirm Dialog Prefab")]
        public static void Generate()
        {
            EnsureDirectory("Assets/Res/UI");

            var root = new GameObject("ConfirmDialog", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rootRect = root.GetComponent<RectTransform>();
            StretchFull(rootRect);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(root.transform, false);
            panelRect.sizeDelta = new Vector2(560, 280);
            panel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.96f);

            root.AddComponent<ConfirmDialogView>();
            var ui = root.AddComponent<UIReference>();

            CreateText(panel.transform, "Title", new Vector2(0, 90), 28, FontStyles.Bold);
            CreateText(panel.transform, "Message", new Vector2(0, 20), 22, FontStyles.Normal);
            CreateButton(panel.transform, "OkButton", "OkLabel", new Vector2(-90, -90));
            CreateButton(panel.transform, "CancelButton", "CancelLabel", new Vector2(90, -90));
            var yes = CreateButton(panel.transform, "YesButton", "YesLabel", new Vector2(-90, -90));
            var no = CreateButton(panel.transform, "NoButton", "NoLabel", new Vector2(90, -90));
            yes.gameObject.SetActive(false);
            no.gameObject.SetActive(false);

            ui.Collect();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"ConfirmDialog prefab saved to {PrefabAssetPath}");
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

        private static TMP_Text CreateText(Transform parent, string name, Vector2 anchoredPos, float fontSize, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(500, 60);
            rect.anchoredPosition = anchoredPos;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            AddBind(go, name, text);
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string labelKey, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(140, 48);
            rect.anchoredPosition = anchoredPos;
            go.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.85f, 1f);

            var button = go.GetComponent<Button>();
            AddBind(go, name, button);

            var labelGo = new GameObject(labelKey, typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(go.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 20;
            label.color = Color.white;
            AddBind(labelGo, labelKey, label);
            return button;
        }
    }
}
