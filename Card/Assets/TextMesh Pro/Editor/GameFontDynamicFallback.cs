using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TMPro.EditorUtilities
{
    /// <summary>
    /// 昵称等运行时任意文本无法静态收集，主图集（Static）缺字时由本动态字体按需即时栅格化补字。
    /// 源字体 = 主字体的 GB2312 子集（同风格，约 1MB）。资产路径不同于 GameFontAtlasBuilder
    /// 管理的静态回退（GameFont SDF Fallback），菜单 3 重建主图集时不会被删除，只需创建一次。
    /// </summary>
    public static class GameFontDynamicFallback
    {
        private const string MenuPath = "Tools/GameFont/4. 创建动态回退字体（昵称等任意文本）";

        public const string DynamicSourceFontPath = "Assets/TextMesh Pro/Fonts/FusionPixel12Mono-GB2312.otf";
        public const string DynamicFallbackAssetPath = "Assets/TextMesh Pro/Fonts/GameFont SDF Dynamic.asset";

        private const int InitialAtlasSize = 512;

        [MenuItem(MenuPath, false, 103)]
        private static void CreateOrRepair()
        {
            var mainFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GameFontAtlasBuilder.FontAssetPath);
            if (mainFont == null)
            {
                Debug.LogError("[GameFont] 未找到主字体资源: " + GameFontAtlasBuilder.FontAssetPath);
                return;
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(DynamicSourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError("[GameFont] 未找到动态回退源字体: " + DynamicSourceFontPath);
                return;
            }

            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFallbackAssetPath);
            if (fallback == null)
            {
                // 采样参数复制主字体，保证回退字形与主字体观感一致
                fallback = TMP_FontAsset.CreateFontAsset(
                    sourceFont,
                    mainFont.faceInfo.pointSize,
                    mainFont.atlasPadding,
                    mainFont.atlasRenderMode,
                    InitialAtlasSize,
                    InitialAtlasSize,
                    AtlasPopulationMode.Dynamic,
                    true);
                if (fallback == null)
                {
                    Debug.LogError("[GameFont] 动态回退字体创建失败：源字体无法加载字形，检查 Import Settings 的 Include Font Data。");
                    return;
                }

                fallback.name = "GameFont SDF Dynamic";
                fallback.material.name = "GameFont SDF Dynamic Material";
                fallback.atlasTexture.name = "GameFont SDF Dynamic Atlas";

                AssetDatabase.CreateAsset(fallback, DynamicFallbackAssetPath);
                AssetDatabase.AddObjectToAsset(fallback.atlasTexture, fallback);
                AssetDatabase.AddObjectToAsset(fallback.material, fallback);
            }

            // 材质 shader 与主字体对齐（CreateFontAsset 默认给 Mobile 变体）
            if (mainFont.material != null && fallback.material.shader != mainFont.material.shader)
            {
                fallback.material.shader = mainFont.material.shader;
                fallback.material.SetTexture(ShaderUtilities.ID_MainTex, fallback.atlasTexture);
            }

            fallback.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            // 挂到主字体 Fallback 表；先清同路径旧引用与空引用，重复执行安全
            var table = mainFont.fallbackFontAssetTable;
            if (table == null)
            {
                table = new List<TMP_FontAsset>();
                mainFont.fallbackFontAssetTable = table;
            }

            for (var i = table.Count - 1; i >= 0; i--)
            {
                if (table[i] == null || AssetDatabase.GetAssetPath(table[i]) == DynamicFallbackAssetPath)
                {
                    table.RemoveAt(i);
                }
            }

            table.Add(fallback);

            EditorUtility.SetDirty(fallback);
            EditorUtility.SetDirty(mainFont);
            AssetDatabase.SaveAssets();
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, mainFont);

            Debug.Log(
                "[GameFont] 动态回退字体已就绪并挂入主字体 Fallback 表。源=" + DynamicSourceFontPath +
                "，采样 " + mainFont.faceInfo.pointSize + "pt / padding " + mainFont.atlasPadding +
                "。主图集缺字（昵称等任意文本）将在运行时按需栅格化。");
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateMenu()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
