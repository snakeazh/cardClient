using TMPro;
using UnityEditor;
using UnityEngine;

namespace Framework.UI.Editor
{
    public sealed class TextToTmpConverterWindow : EditorWindow
    {
        private const string MenuRoot = "Tools/Text To TMP/";
        private const string DefaultFontPath = "Assets/TextMesh Pro/Fonts/GameFont SDF.asset";

        private TMP_FontAsset _font;
        private bool _convertPrefabs = true;
        private bool _convertScenes = true;
        private bool _skipThirdParty = true;
        private bool _convertInputFields = true;
        private Vector2 _scroll;
        private string _status = "先扫描，确认列表后再一键转换。";
        private TextToTmpConverter.Report _lastReport;

        [MenuItem(MenuRoot + "打开转换窗口", false, 200)]
        public static void Open()
        {
            var window = GetWindow<TextToTmpConverterWindow>("Text → TMP");
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        [MenuItem(MenuRoot + "一键转换项目中的 Text", false, 201)]
        public static void ConvertAllMenu()
        {
            if (!ValidateEditorState())
            {
                return;
            }

            var options = CreateDefaultOptions();
            var scan = TextToTmpConverter.ScanProject(options);
            if (scan.ConvertedTextCount == 0 && scan.ConvertedInputFieldCount == 0)
            {
                EditorUtility.DisplayDialog("Text → TMP", "没有找到可转换的 Text。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Text → TMP",
                    $"即将转换：\n{scan}\n\n默认跳过 Plugins 与 TMP 示例。此操作会改 Prefab/场景，请先确认已提交或备份。",
                    "开始转换",
                    "取消"))
            {
                return;
            }

            var report = TextToTmpConverter.ConvertProject(options);
            EditorUtility.DisplayDialog("Text → TMP", "转换完成。\n" + report, "确定");
            Debug.Log("[TextToTMP] " + report + "\n" + string.Join("\n", report.Logs));
        }

        [MenuItem(MenuRoot + "转换选中对象", false, 202)]
        public static void ConvertSelectionMenu()
        {
            if (!ValidateEditorState())
            {
                return;
            }

            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Text → TMP", "请先在 Hierarchy 或 Prefab 中选中对象。", "确定");
                return;
            }

            var report = new TextToTmpConverter.Report();
            var count = TextToTmpConverter.ConvertSelection(CreateDefaultOptions(), report);
            EditorUtility.DisplayDialog("Text → TMP", count > 0 ? "已转换选中对象。\n" + report : "选中对象下没有可转换的 Text。", "确定");
            if (count > 0)
            {
                Debug.Log("[TextToTMP] 选中对象转换完成。\n" + string.Join("\n", report.Logs));
            }
        }

        [MenuItem(MenuRoot + "一键转换项目中的 Text", true)]
        [MenuItem(MenuRoot + "转换选中对象", true)]
        private static bool ValidateMenu()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private void OnEnable()
        {
            if (_font == null)
            {
                _font = TextToTmpConverter.LoadDefaultFont();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("把 Unity UI Text 转成 TextMeshProUGUI", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "会保留文字、字号、颜色、对齐、Overflow、Best Fit，并把 UIBind 等对象引用指到新组件。\n" +
                "C# 里 GetComponent<Text>() 需要自行改成 TMP_Text / TextMeshProUGUI。",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            _font = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP 字体", _font, typeof(TMP_FontAsset), false);
            if (_font == null)
            {
                EditorGUILayout.HelpBox($"未指定字体时使用 {DefaultFontPath} 或 TMP Settings 默认字体。", MessageType.None);
            }

            _convertPrefabs = EditorGUILayout.ToggleLeft("转换 Prefab", _convertPrefabs);
            _convertScenes = EditorGUILayout.ToggleLeft("转换场景", _convertScenes);
            _skipThirdParty = EditorGUILayout.ToggleLeft("跳过 Plugins / TMP 示例", _skipThirdParty);
            _convertInputFields = EditorGUILayout.ToggleLeft("同时把 InputField 转成 TMP_InputField", _convertInputFields);

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!ValidateMenu()))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("扫描项目", GUILayout.Height(28f)))
                    {
                        _lastReport = TextToTmpConverter.ScanProject(CreateOptions());
                        _status = "扫描完成。" + _lastReport;
                    }

                    if (GUILayout.Button("转换选中", GUILayout.Height(28f)))
                    {
                        _lastReport = new TextToTmpConverter.Report();
                        TextToTmpConverter.ConvertSelection(CreateOptions(), _lastReport);
                        _status = "选中对象转换完成。" + _lastReport;
                    }
                }

                var convertColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
                if (GUILayout.Button("一键转换项目中的 Text", GUILayout.Height(32f)))
                {
                    GUI.backgroundColor = convertColor;
                    RunConvertAll();
                }
                else
                {
                    GUI.backgroundColor = convertColor;
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(_status, EditorStyles.wordWrappedLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_lastReport != null)
            {
                for (var i = 0; i < _lastReport.Logs.Count; i++)
                {
                    EditorGUILayout.LabelField(_lastReport.Logs[i], EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void RunConvertAll()
        {
            var options = CreateOptions();
            var scan = TextToTmpConverter.ScanProject(options);
            if (scan.ConvertedTextCount == 0 && scan.ConvertedInputFieldCount == 0)
            {
                _lastReport = scan;
                _status = "没有找到可转换的 Text。";
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Text → TMP",
                    $"即将转换：\n{scan}\n\n此操作会改 Prefab/场景，请先确认已提交或备份。",
                    "开始转换",
                    "取消"))
            {
                _lastReport = scan;
                _status = "已取消。扫描结果如下。";
                return;
            }

            _lastReport = TextToTmpConverter.ConvertProject(options);
            _status = "转换完成。" + _lastReport;
            Debug.Log("[TextToTMP] " + _lastReport + "\n" + string.Join("\n", _lastReport.Logs));
        }

        private TextToTmpConverter.Options CreateOptions()
        {
            return new TextToTmpConverter.Options
            {
                Font = _font != null ? _font : TextToTmpConverter.LoadDefaultFont(),
                ConvertPrefabs = _convertPrefabs,
                ConvertScenes = _convertScenes,
                SkipThirdParty = _skipThirdParty,
                ConvertInputFields = _convertInputFields,
            };
        }

        private static TextToTmpConverter.Options CreateDefaultOptions()
        {
            return new TextToTmpConverter.Options
            {
                Font = TextToTmpConverter.LoadDefaultFont(),
                ConvertPrefabs = true,
                ConvertScenes = true,
                SkipThirdParty = true,
                ConvertInputFields = true,
            };
        }

        private static bool ValidateEditorState()
        {
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("[TextToTMP] 编译中，请稍后再试。");
                return false;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[TextToTMP] 运行中无法转换。");
                return false;
            }

            return true;
        }
    }
}
