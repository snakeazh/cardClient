using UnityEditor;
using UnityEngine;

namespace TMPro.EditorUtilities
{
    /// <summary>
    /// GameFont 自动生成工具菜单。
    /// </summary>
    public static class GameFontToolMenu
    {
        private const string MenuCollect = "Tools/GameFont/1. 收集字符到 Lang-zh.txt";
        private const string MenuGenerate = "Tools/GameFont/2. 根据 Lang-zh.txt 生成 GameFont";
        private const string MenuCollectAndGenerate = "Tools/GameFont/3. 一键收集并生成 GameFont";

        [MenuItem(MenuCollect, false, 100)]
        private static void CollectCharacters()
        {
            if (!ValidateEditorState())
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("GameFont", "正在收集字符…", 0.35f);
                var result = GameFontCharacterCollector.CollectToLangZhFile();
                AssetDatabase.Refresh();

                Debug.Log(
                    $"[GameFont] 字符收集完成。Prefab={result.PrefabCount}, JSON={result.JsonFileCount}, " +
                    $"源码={result.ScriptFileCount}, 字符数={result.CharacterCount}, 输出={result.OutputPath}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(MenuGenerate, false, 101)]
        private static void GenerateFont()
        {
            if (!ValidateEditorState())
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("GameFont", "正在生成字体图集…", 0.7f);
                var result = GameFontAtlasBuilder.RebuildGameFontFromLangFile();
                AssetDatabase.Refresh();

                if (!result.Success)
                {
                    EditorUtility.DisplayDialog(
                        "GameFont",
                        result.Message + "\n\n请查看 Console 中的缺失/溢出字符详情。",
                        "确定");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(MenuCollectAndGenerate, false, 102)]
        private static void CollectAndGenerate()
        {
            if (!ValidateEditorState())
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("GameFont", "正在收集字符…", 0.3f);
                var collectResult = GameFontCharacterCollector.CollectToLangZhFile();
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar("GameFont", "正在生成字体图集…", 0.75f);
                var buildResult = GameFontAtlasBuilder.RebuildGameFontFromLangFile();
                AssetDatabase.Refresh();

                var message =
                    $"收集: Prefab={collectResult.PrefabCount}, JSON={collectResult.JsonFileCount}, " +
                    $"源码={collectResult.ScriptFileCount}, 字符数={collectResult.CharacterCount}\n" +
                    $"生成: {buildResult.IncludedCharacterCount}/{buildResult.RequestedCharacterCount}，" +
                    $"回退={buildResult.FallbackCharacterCount}，" +
                    $"未写入={buildResult.MissingCharacterCount}，图集数={buildResult.AtlasTextureCount}";

                if (buildResult.Success)
                {
                    Debug.Log("[GameFont] 一键完成。\n" + message);
                }
                else
                {
                    EditorUtility.DisplayDialog("GameFont", message + "\n\n部分字符未成功写入字体，请查看 Console。", "确定");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(MenuCollect, true)]
        [MenuItem(MenuGenerate, true)]
        [MenuItem(MenuCollectAndGenerate, true)]
        private static bool ValidateMenu()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static bool ValidateEditorState()
        {
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("[GameFont] 编译中，请稍后再试。");
                return false;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[GameFont] 运行中无法生成字体。");
                return false;
            }

            return true;
        }
    }
}
