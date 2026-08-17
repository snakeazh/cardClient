using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityObject = UnityEngine.Object;

namespace Framework.UI.Editor
{
    /// <summary>
    /// Converts Unity UI <see cref="Text"/> components to <see cref="TextMeshProUGUI"/>.
    /// </summary>
    public static class TextToTmpConverter
    {
        private static readonly string[] DefaultSkipPathParts =
        {
            "/Plugins/",
            "/TextMesh Pro/Examples & Extras/",
            "/TextMesh Pro/Examples/",
        };

        public sealed class Options
        {
            public TMP_FontAsset Font;
            public bool ConvertPrefabs = true;
            public bool ConvertScenes = true;
            public bool SkipThirdParty = true;
            public bool ConvertInputFields = true;
        }

        public sealed class Report
        {
            public int PrefabAssetCount;
            public int SceneAssetCount;
            public int ConvertedTextCount;
            public int ConvertedInputFieldCount;
            public int SkippedCount;
            public readonly List<string> Logs = new List<string>();

            public override string ToString()
            {
                return
                    $"Text {ConvertedTextCount} 个，InputField {ConvertedInputFieldCount} 个，" +
                    $"跳过 {SkippedCount} 个，Prefab {PrefabAssetCount} 个，场景 {SceneAssetCount} 个";
            }
        }

        public static TMP_FontAsset LoadDefaultFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/GameFont SDF.asset");
            if (font != null)
            {
                return font;
            }

            return TMP_Settings.defaultFontAsset;
        }

        public static Report ScanProject(Options options)
        {
            return ConvertProject(options, dryRun: true);
        }

        public static Report ConvertProject(Options options)
        {
            return ConvertProject(options, dryRun: false);
        }

        public static int ConvertSelection(Options options, Report report)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            report ??= new Report();
            var converted = 0;
            var processedPrefabs = new HashSet<string>();
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var openPrefabPath = prefabStage != null ? prefabStage.assetPath : null;

            var selectedObjects = Selection.objects;
            for (var i = 0; i < selectedObjects.Length; i++)
            {
                var path = AssetDatabase.GetAssetPath(selectedObjects[i]);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(openPrefabPath) &&
                    string.Equals(path, openPrefabPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!processedPrefabs.Add(path))
                {
                    continue;
                }

                converted += ConvertPrefab(path, options, report, dryRun: false);
            }

            var selected = Selection.gameObjects;
            for (var i = 0; i < selected.Length; i++)
            {
                var go = selected[i];
                if (go == null)
                {
                    continue;
                }

                if (prefabStage != null && prefabStage.IsPartOfPrefabContents(go))
                {
                    converted += ConvertHierarchy(go, options, report, dryRun: false);
                    EditorSceneManager.MarkSceneDirty(prefabStage.scene);
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
                    var path = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
                    if (!string.IsNullOrEmpty(path) &&
                        path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                        processedPrefabs.Add(path))
                    {
                        converted += ConvertPrefab(path, options, report, dryRun: false);
                    }

                    continue;
                }

                converted += ConvertHierarchy(go, options, report, dryRun: false);
                EditorUtility.SetDirty(go);
            }

            return converted;
        }

        private static Report ConvertProject(Options options, bool dryRun)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var report = new Report();
            var prefabGuids = options.ConvertPrefabs
                ? AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                : Array.Empty<string>();
            var sceneGuids = options.ConvertScenes
                ? AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                : Array.Empty<string>();

            var total = prefabGuids.Length + sceneGuids.Length;
            var current = 0;

            try
            {
                for (var i = 0; i < prefabGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    current++;
                    if (ShouldSkipPath(path, options))
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        dryRun ? "扫描 Text" : "转换 Text → TMP",
                        path,
                        total == 0 ? 1f : current / (float)total);

                    var count = ConvertPrefab(path, options, report, dryRun);
                    if (count > 0)
                    {
                        report.PrefabAssetCount++;
                    }
                }

                if (options.ConvertScenes && sceneGuids.Length > 0)
                {
                    ConvertScenes(sceneGuids, options, report, dryRun, ref current, total);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!dryRun)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }

            return report;
        }

        private static int ConvertPrefab(string path, Options options, Report report, bool dryRun)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var count = ConvertHierarchy(root, options, report, dryRun);
                if (!dryRun && count > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }

                return count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConvertScenes(
            string[] sceneGuids,
            Options options,
            Report report,
            bool dryRun,
            ref int current,
            int total)
        {
            var previousPath = SceneManager.GetActiveScene().path;
            var previousDirty = SceneManager.GetActiveScene().isDirty;
            if (previousDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                report.Logs.Add("场景有未保存修改，已取消场景转换。");
                return;
            }

            try
            {
                for (var i = 0; i < sceneGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    current++;
                    if (ShouldSkipPath(path, options))
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        dryRun ? "扫描 Text" : "转换 Text → TMP",
                        path,
                        total == 0 ? 1f : current / (float)total);

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var converted = 0;
                    var roots = scene.GetRootGameObjects();
                    for (var r = 0; r < roots.Length; r++)
                    {
                        converted += ConvertHierarchy(roots[r], options, report, dryRun);
                    }

                    if (converted > 0)
                    {
                        report.SceneAssetCount++;
                        if (!dryRun)
                        {
                            EditorSceneManager.SaveScene(scene);
                        }
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousPath) && previousPath != SceneManager.GetActiveScene().path)
                {
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
                }
            }
        }

        private static int ConvertHierarchy(GameObject root, Options options, Report report, bool dryRun)
        {
            if (root == null)
            {
                return 0;
            }

            var converted = 0;
            if (options.ConvertInputFields)
            {
                var inputs = root.GetComponentsInChildren<InputField>(true);
                for (var i = 0; i < inputs.Length; i++)
                {
                    var input = inputs[i];
                    if (input == null || ShouldSkipInstance(input))
                    {
                        continue;
                    }

                    if (dryRun)
                    {
                        report.ConvertedInputFieldCount++;
                        report.Logs.Add($"{GetPath(input.transform)}  [InputField]");
                        continue;
                    }

                    if (ConvertInputField(input, options, report))
                    {
                        converted++;
                        report.ConvertedInputFieldCount++;
                    }
                }
            }

            var texts = root.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null)
                {
                    continue;
                }

                if (ShouldSkipText(text, options, out var reason))
                {
                    if (!string.IsNullOrEmpty(reason))
                    {
                        report.SkippedCount++;
                        report.Logs.Add(reason);
                    }

                    continue;
                }

                if (dryRun)
                {
                    converted++;
                    report.ConvertedTextCount++;
                    report.Logs.Add(GetPath(text.transform));
                    continue;
                }

                if (ConvertText(text, options, report, out _))
                {
                    converted++;
                    report.ConvertedTextCount++;
                }
            }

            return converted;
        }

        private static bool ConvertInputField(InputField input, Options options, Report report)
        {
            var go = input.gameObject;
            var originalIndex = GetComponentIndex(input);
            var textComponent = input.textComponent;
            var placeholder = input.placeholder as Text;
            var snapshot = InputFieldSnapshot.Capture(input);

            TextMeshProUGUI tmpText = null;
            TMP_Text tmpPlaceholder = null;
            if (textComponent != null && ConvertText(textComponent, options, report, out tmpText))
            {
                report.ConvertedTextCount++;
            }

            if (placeholder != null && ConvertText(placeholder, options, report, out var tmp))
            {
                tmpPlaceholder = tmp;
                report.ConvertedTextCount++;
            }

            var refs = CollectReferences(go, input);
            UnityObject.DestroyImmediate(input, true);
            var tmpInput = go.AddComponent<TMP_InputField>();
            snapshot.Apply(tmpInput, tmpText, tmpPlaceholder);
            ApplyReferences(refs, tmpInput);
            MoveComponentToIndex(tmpInput, originalIndex);
            report.Logs.Add($"{GetPath(go.transform)}  [InputField → TMP_InputField]");
            return true;
        }

        private static bool ConvertText(Text text, Options options, Report report, out TextMeshProUGUI tmp)
        {
            tmp = null;
            if (text == null)
            {
                return false;
            }

            var go = text.gameObject;
            if (go.GetComponent<TextMeshProUGUI>() != null)
            {
                report.Logs.Add($"跳过 {GetPath(go.transform)}：已有 TextMeshProUGUI");
                report.SkippedCount++;
                return false;
            }

            var originalIndex = GetComponentIndex(text);
            var snapshot = TextSnapshot.Capture(text);
            var refs = CollectReferences(go, text);

            UnityObject.DestroyImmediate(text, true);
            tmp = go.AddComponent<TextMeshProUGUI>();
            snapshot.Apply(tmp, options.Font);
            ApplyReferences(refs, tmp);
            MoveComponentToIndex(tmp, originalIndex);

            var path = GetPath(go.transform);
            report.Logs.Add(path);
            return true;
        }

        private static bool ShouldSkipText(Text text, Options options, out string reason)
        {
            reason = null;
            if (ShouldSkipInstance(text))
            {
                return true;
            }

            if (text.GetComponent<TextMeshProUGUI>() != null)
            {
                reason = $"跳过 {GetPath(text.transform)}：已有 TextMeshProUGUI";
                return true;
            }

            if (IsOwnedByLegacyInputField(text))
            {
                if (options.ConvertInputFields)
                {
                    return true;
                }

                reason = $"跳过 {GetPath(text.transform)}：属于 InputField，请勾选同时转换 InputField";
                return true;
            }

            if (IsOwnedByLegacyDropdown(text))
            {
                reason = $"跳过 {GetPath(text.transform)}：属于 Dropdown，请手动改为 TMP_Dropdown";
                return true;
            }

            return false;
        }

        private static bool ShouldSkipInstance(Component component)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(component))
            {
                return false;
            }

            return !PrefabUtility.IsAddedComponentOverride(component);
        }

        private static bool IsOwnedByLegacyInputField(Text text)
        {
            var inputs = text.GetComponentsInParent<InputField>(true);
            for (var i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                if (input.textComponent == text || input.placeholder == text)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOwnedByLegacyDropdown(Text text)
        {
            var dropdowns = text.GetComponentsInParent<Dropdown>(true);
            for (var i = 0; i < dropdowns.Length; i++)
            {
                var dropdown = dropdowns[i];
                if (dropdown.captionText == text || dropdown.itemText == text)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldSkipPath(string path, Options options)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            if (!options.SkipThirdParty)
            {
                return false;
            }

            for (var i = 0; i < DefaultSkipPathParts.Length; i++)
            {
                if (path.IndexOf(DefaultSkipPathParts[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<(UnityObject Target, string PropertyPath)> CollectReferences(GameObject start, UnityObject oldValue)
        {
            var result = new List<(UnityObject, string)>();
            if (start == null || oldValue == null)
            {
                return result;
            }

            var root = start.transform.root.gameObject;
            var components = root.GetComponentsInChildren<Component>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component == oldValue)
                {
                    continue;
                }

                var so = new SerializedObject(component);
                var iterator = so.GetIterator();
                var enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = iterator.propertyType != SerializedPropertyType.String;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    if (iterator.objectReferenceValue == oldValue)
                    {
                        result.Add((component, iterator.propertyPath));
                    }
                }
            }

            return result;
        }

        private static void ApplyReferences(List<(UnityObject Target, string PropertyPath)> refs, UnityObject newValue)
        {
            if (refs == null || newValue == null)
            {
                return;
            }

            for (var i = 0; i < refs.Count; i++)
            {
                var target = refs[i].Target;
                if (target == null)
                {
                    continue;
                }

                var so = new SerializedObject(target);
                var property = so.FindProperty(refs[i].PropertyPath);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                property.objectReferenceValue = newValue;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static int GetComponentIndex(Component component)
        {
            var components = component.gameObject.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == component)
                {
                    return i;
                }
            }

            return components.Length - 1;
        }

        private static void MoveComponentToIndex(Component component, int targetIndex)
        {
            targetIndex = Mathf.Max(1, targetIndex);
            var guard = 32;
            while (guard-- > 0)
            {
                var current = GetComponentIndex(component);
                if (current <= targetIndex)
                {
                    break;
                }

                ComponentUtility.MoveComponentUp(component);
            }
        }

        private static string GetPath(Transform transform)
        {
            var path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            var assetPath = AssetDatabase.GetAssetPath(transform.root.gameObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                assetPath = transform.gameObject.scene.path;
            }

            return string.IsNullOrEmpty(assetPath) ? path : assetPath + "  " + path;
        }

        private readonly struct TextSnapshot
        {
            private readonly string _text;
            private readonly int _fontSize;
            private readonly FontStyle _fontStyle;
            private readonly TextAnchor _alignment;
            private readonly Color _color;
            private readonly bool _raycastTarget;
            private readonly Vector4 _raycastPadding;
            private readonly bool _maskable;
            private readonly bool _richText;
            private readonly bool _bestFit;
            private readonly int _minSize;
            private readonly int _maxSize;
            private readonly HorizontalWrapMode _horizontalOverflow;
            private readonly VerticalWrapMode _verticalOverflow;
            private readonly float _lineSpacing;
            private readonly bool _enabled;

            private TextSnapshot(
                string text,
                int fontSize,
                FontStyle fontStyle,
                TextAnchor alignment,
                Color color,
                bool raycastTarget,
                Vector4 raycastPadding,
                bool maskable,
                bool richText,
                bool bestFit,
                int minSize,
                int maxSize,
                HorizontalWrapMode horizontalOverflow,
                VerticalWrapMode verticalOverflow,
                float lineSpacing,
                bool enabled)
            {
                _text = text;
                _fontSize = fontSize;
                _fontStyle = fontStyle;
                _alignment = alignment;
                _color = color;
                _raycastTarget = raycastTarget;
                _raycastPadding = raycastPadding;
                _maskable = maskable;
                _richText = richText;
                _bestFit = bestFit;
                _minSize = minSize;
                _maxSize = maxSize;
                _horizontalOverflow = horizontalOverflow;
                _verticalOverflow = verticalOverflow;
                _lineSpacing = lineSpacing;
                _enabled = enabled;
            }

            public static TextSnapshot Capture(Text text)
            {
                return new TextSnapshot(
                    text.text,
                    text.fontSize,
                    text.fontStyle,
                    text.alignment,
                    text.color,
                    text.raycastTarget,
                    text.raycastPadding,
                    text.maskable,
                    text.supportRichText,
                    text.resizeTextForBestFit,
                    text.resizeTextMinSize,
                    text.resizeTextMaxSize,
                    text.horizontalOverflow,
                    text.verticalOverflow,
                    text.lineSpacing,
                    text.enabled);
            }

            public void Apply(TextMeshProUGUI tmp, TMP_FontAsset font)
            {
                if (font != null)
                {
                    tmp.font = font;
                }

                tmp.text = _text ?? string.Empty;
                tmp.fontSize = _fontSize;
                tmp.fontStyle = ConvertFontStyle(_fontStyle);
                tmp.alignment = ConvertAlignment(_alignment);
                tmp.color = _color;
                tmp.raycastTarget = _raycastTarget;
                tmp.raycastPadding = _raycastPadding;
                tmp.maskable = _maskable;
                tmp.richText = _richText;
                tmp.enableAutoSizing = _bestFit;
                tmp.fontSizeMin = _minSize;
                tmp.fontSizeMax = _maxSize;
                tmp.enableWordWrapping = _horizontalOverflow == HorizontalWrapMode.Wrap;
                tmp.overflowMode = _horizontalOverflow == HorizontalWrapMode.Overflow ||
                                   _verticalOverflow == VerticalWrapMode.Overflow
                    ? TextOverflowModes.Overflow
                    : TextOverflowModes.Truncate;
                tmp.lineSpacing = (_lineSpacing - 1f) * tmp.fontSize;
                tmp.enabled = _enabled;
            }

            private static FontStyles ConvertFontStyle(FontStyle style)
            {
                switch (style)
                {
                    case FontStyle.Bold:
                        return FontStyles.Bold;
                    case FontStyle.Italic:
                        return FontStyles.Italic;
                    case FontStyle.BoldAndItalic:
                        return FontStyles.Bold | FontStyles.Italic;
                    default:
                        return FontStyles.Normal;
                }
            }

            private static TextAlignmentOptions ConvertAlignment(TextAnchor anchor)
            {
                switch (anchor)
                {
                    case TextAnchor.UpperLeft:
                        return TextAlignmentOptions.TopLeft;
                    case TextAnchor.UpperCenter:
                        return TextAlignmentOptions.Top;
                    case TextAnchor.UpperRight:
                        return TextAlignmentOptions.TopRight;
                    case TextAnchor.MiddleLeft:
                        return TextAlignmentOptions.MidlineLeft;
                    case TextAnchor.MiddleCenter:
                        return TextAlignmentOptions.Midline;
                    case TextAnchor.MiddleRight:
                        return TextAlignmentOptions.MidlineRight;
                    case TextAnchor.LowerLeft:
                        return TextAlignmentOptions.BottomLeft;
                    case TextAnchor.LowerCenter:
                        return TextAlignmentOptions.Bottom;
                    case TextAnchor.LowerRight:
                        return TextAlignmentOptions.BottomRight;
                    default:
                        return TextAlignmentOptions.Midline;
                }
            }
        }

        private readonly struct InputFieldSnapshot
        {
            private readonly string _text;
            private readonly int _characterLimit;
            private readonly InputField.ContentType _contentType;
            private readonly InputField.LineType _lineType;
            private readonly InputField.InputType _inputType;
            private readonly TouchScreenKeyboardType _keyboardType;
            private readonly InputField.CharacterValidation _characterValidation;
            private readonly char _asteriskChar;
            private readonly float _caretBlinkRate;
            private readonly int _caretWidth;
            private readonly bool _customCaretColor;
            private readonly Color _caretColor;
            private readonly Color _selectionColor;
            private readonly bool _readOnly;
            private readonly bool _enabled;

            private InputFieldSnapshot(
                string text,
                int characterLimit,
                InputField.ContentType contentType,
                InputField.LineType lineType,
                InputField.InputType inputType,
                TouchScreenKeyboardType keyboardType,
                InputField.CharacterValidation characterValidation,
                char asteriskChar,
                float caretBlinkRate,
                int caretWidth,
                bool customCaretColor,
                Color caretColor,
                Color selectionColor,
                bool readOnly,
                bool enabled)
            {
                _text = text;
                _characterLimit = characterLimit;
                _contentType = contentType;
                _lineType = lineType;
                _inputType = inputType;
                _keyboardType = keyboardType;
                _characterValidation = characterValidation;
                _asteriskChar = asteriskChar;
                _caretBlinkRate = caretBlinkRate;
                _caretWidth = caretWidth;
                _customCaretColor = customCaretColor;
                _caretColor = caretColor;
                _selectionColor = selectionColor;
                _readOnly = readOnly;
                _enabled = enabled;
            }

            public static InputFieldSnapshot Capture(InputField input)
            {
                return new InputFieldSnapshot(
                    input.text,
                    input.characterLimit,
                    input.contentType,
                    input.lineType,
                    input.inputType,
                    input.keyboardType,
                    input.characterValidation,
                    input.asteriskChar,
                    input.caretBlinkRate,
                    input.caretWidth,
                    input.customCaretColor,
                    input.caretColor,
                    input.selectionColor,
                    input.readOnly,
                    input.enabled);
            }

            public void Apply(TMP_InputField tmp, TMP_Text textComponent, TMP_Text placeholder)
            {
                tmp.textComponent = textComponent;
                tmp.placeholder = placeholder;
                if (textComponent != null)
                {
                    tmp.textViewport = textComponent.rectTransform;
                }

                tmp.text = _text ?? string.Empty;
                tmp.characterLimit = _characterLimit;
                tmp.contentType = (TMP_InputField.ContentType)(int)_contentType;
                tmp.lineType = (TMP_InputField.LineType)(int)_lineType;
                tmp.inputType = (TMP_InputField.InputType)(int)_inputType;
                tmp.keyboardType = _keyboardType;
                tmp.characterValidation = (TMP_InputField.CharacterValidation)(int)_characterValidation;
                tmp.asteriskChar = _asteriskChar;
                tmp.caretBlinkRate = _caretBlinkRate;
                tmp.caretWidth = _caretWidth;
                tmp.customCaretColor = _customCaretColor;
                tmp.caretColor = _caretColor;
                tmp.selectionColor = _selectionColor;
                tmp.readOnly = _readOnly;
                tmp.enabled = _enabled;
            }
        }
    }
}
