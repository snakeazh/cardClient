using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TMPro.EditorUtilities
{
    /// <summary>
    /// 按 Lang-zh.txt 的字符集重建 GameFont SDF 图集，保留原资源 GUID 与材质引用。
    /// </summary>
    public static class GameFontAtlasBuilder
    {
        public const string FontAssetPath = "Assets/TextMesh Pro/Fonts/GameFont SDF.asset";

        /// <summary>2026-09 起主源字体换为美工提供的像素字体，LeMi.ttf 已弃用。</summary>
        public const string SourceFontPath = "Assets/TextMesh Pro/Fonts/FusionPixel12Mono.otf";

        /// <summary>回退源字体：LeMi 缺字时补字形（覆盖全部中英文符号）。</summary>
        public const string FallbackSourceFontPath = "Assets/TextMesh Pro/Fonts/GameFont.otf";

        public const string FallbackFontAssetPath = "Assets/TextMesh Pro/Fonts/GameFont SDF Fallback.asset";

        private const int FallbackAtlasSize = 512;

        public sealed class BuildResult
        {
            public bool Success;
            public int RequestedCharacterCount;
            public int IncludedCharacterCount;
            public int MissingCharacterCount;
            public int FallbackCharacterCount;
            public int AtlasTextureCount;
            public string Message;
        }

        public static BuildResult RebuildGameFontFromLangFile()
        {
            var characterSequence = GameFontCharacterCollector.ReadCharacterSequenceFromLangFile();
            if (string.IsNullOrEmpty(characterSequence))
            {
                return Fail("Lang-zh.txt 为空或不存在，请先执行字符收集。");
            }

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                return Fail($"未找到字体资源: {FontAssetPath}");
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                return Fail($"未找到源字体文件: {SourceFontPath}");
            }

            var uniqueCharacters = BuildUniqueCharacterSequence(characterSequence);
            if (uniqueCharacters.Length == 0)
            {
                return Fail("Lang-zh.txt 解析后没有可用字符。");
            }

            var originalPopulationMode = fontAsset.atlasPopulationMode;

            // Dynamic 模式才允许写入字形；setter 会从 m_SourceFontFile_EditorRef 恢复源字体引用。
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            EnsureSourceFontAssigned(fontAsset, sourceFont);

            if (fontAsset.sourceFontFile == null)
            {
                return Fail($"字体资源未关联源字体，请在 Inspector 中把 {SourceFontPath} 指给 Source Font File。");
            }

            fontAsset.ClearFontAssetData();

            fontAsset.TryAddCharacters(uniqueCharacters, out var missingCharacters);
            fontAsset.ReadFontAssetDefinition();

            fontAsset.creationSettings = BuildCreationSettings(fontAsset, characterSequence);

            if (originalPopulationMode == AtlasPopulationMode.Static)
            {
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            }

            EditorUtility.SetDirty(fontAsset);
            if (fontAsset.atlasTexture != null)
            {
                EditorUtility.SetDirty(fontAsset.atlasTexture);
            }

            // 主字体缺字 → 用 GameFont.otf 重建回退字体并挂入 Fallback 表；无缺字则移除
            var fallbackCount = EnsureFallbackFontAsset(fontAsset, missingCharacters);

            AssetDatabase.SaveAssets();
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);

            var missingCount = CountCharacters(missingCharacters);
            var unresolvedCount = missingCount - fallbackCount;
            var message =
                $"GameFont 生成完成。字符 {fontAsset.characterTable.Count}/{uniqueCharacters.Length}，" +
                $"回退 {fallbackCount}，未写入 {unresolvedCount}，图集数量 {fontAsset.atlasTextureCount}。";

            if (unresolvedCount > 0)
            {
                Debug.LogWarning(BuildMissingReport(message, missingCharacters), fontAsset);
            }
            else
            {
                Debug.Log("[GameFont] " + message, fontAsset);
            }

            return new BuildResult
            {
                Success = unresolvedCount == 0,
                RequestedCharacterCount = uniqueCharacters.Length,
                IncludedCharacterCount = fontAsset.characterTable.Count,
                MissingCharacterCount = unresolvedCount,
                FallbackCharacterCount = fallbackCount,
                AtlasTextureCount = fontAsset.atlasTextureCount,
                Message = message
            };
        }

        private static BuildResult Fail(string message)
        {
            Debug.LogError("[GameFont] " + message);
            return new BuildResult
            {
                Success = false,
                Message = message
            };
        }

        /// <summary>
        /// sourceFontFile 的 setter 是 internal，只能通过序列化字段兜底写入。
        /// </summary>
        private static void EnsureSourceFontAssigned(TMP_FontAsset fontAsset, Font sourceFont)
        {
            if (fontAsset.sourceFontFile == sourceFont)
            {
                return;
            }

            var serializedObject = new SerializedObject(fontAsset);
            var editorRef = serializedObject.FindProperty("m_SourceFontFile_EditorRef");
            if (editorRef != null)
            {
                editorRef.objectReferenceValue = sourceFont;
            }

            var runtimeRef = serializedObject.FindProperty("m_SourceFontFile");
            if (runtimeRef != null)
            {
                runtimeRef.objectReferenceValue = sourceFont;
            }

            var guid = serializedObject.FindProperty("m_SourceFontFileGUID");
            if (guid != null)
            {
                guid.stringValue = AssetDatabase.AssetPathToGUID(SourceFontPath);
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static FontAssetCreationSettings BuildCreationSettings(TMP_FontAsset fontAsset, string characterSequence)
        {
            var settings = fontAsset.creationSettings;
            settings.sourceFontFileGUID = AssetDatabase.AssetPathToGUID(SourceFontPath);
            settings.pointSize = fontAsset.faceInfo.pointSize;
            settings.padding = fontAsset.atlasPadding;
            settings.atlasWidth = fontAsset.atlasWidth;
            settings.atlasHeight = fontAsset.atlasHeight;
            settings.renderMode = (int)fontAsset.atlasRenderMode;
            settings.characterSetSelectionMode = 8;
            settings.characterSequence = characterSequence;
            settings.referencedFontAssetGUID = AssetDatabase.AssetPathToGUID(FontAssetPath);
            settings.referencedTextAssetGUID = AssetDatabase.AssetPathToGUID(GameFontCharacterCollector.LangZhAssetPath);
            return settings;
        }

        /// <summary>
        /// 主字体缺字时，用 GameFont.otf 重建回退字体资产（只含当前缺字，烘焙后转 Static），
        /// 挂入主字体 Fallback 表；无缺字则移除旧回退，保持工程干净。
        /// 返回成功写入回退字体的字符数。
        /// </summary>
        private static int EnsureFallbackFontAsset(TMP_FontAsset mainFont, string missingCharacters)
        {
            RemoveFallback(mainFont);

            if (string.IsNullOrEmpty(missingCharacters))
            {
                return 0;
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FallbackSourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogWarning(
                    $"[GameFont] 缺字 {CountCharacters(missingCharacters)} 个，但回退源字体不存在: {FallbackSourceFontPath}",
                    mainFont);
                return 0;
            }

            // 采样参数与主字体一致，保证回退字形渲染观感接近
            var fallback = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                mainFont.faceInfo.pointSize,
                mainFont.atlasPadding,
                mainFont.atlasRenderMode,
                FallbackAtlasSize,
                FallbackAtlasSize,
                AtlasPopulationMode.Dynamic,
                false);
            if (fallback == null)
            {
                Debug.LogWarning("[GameFont] 回退字体创建失败（源字体无法加载字形）。", mainFont);
                return 0;
            }

            fallback.name = "GameFont SDF Fallback";
            fallback.material.name = "GameFont SDF Fallback Material";
            fallback.atlasTexture.name = "GameFont SDF Fallback Atlas";

            AssetDatabase.CreateAsset(fallback, FallbackFontAssetPath);
            AssetDatabase.AddObjectToAsset(fallback.atlasTexture, fallback);
            AssetDatabase.AddObjectToAsset(fallback.material, fallback);

            fallback.TryAddCharacters(missingCharacters, out var unresolved);
            fallback.ReadFontAssetDefinition();
            fallback.atlasPopulationMode = AtlasPopulationMode.Static;

            EditorUtility.SetDirty(fallback);
            if (fallback.atlasTexture != null)
            {
                EditorUtility.SetDirty(fallback.atlasTexture);
            }

            var table = mainFont.fallbackFontAssetTable;
            if (table == null)
            {
                table = new List<TMP_FontAsset>();
                mainFont.fallbackFontAssetTable = table;
            }

            table.Add(fallback);
            EditorUtility.SetDirty(mainFont);

            var unresolvedCount = CountCharacters(unresolved);
            if (unresolvedCount > 0)
            {
                Debug.LogWarning(
                    $"[GameFont] 以下 {unresolvedCount} 个字符主字体与回退字体均缺字，需更换回退源字体：\n{unresolved}",
                    mainFont);
            }

            return CountCharacters(missingCharacters) - unresolvedCount;
        }

        /// <summary>移除并删除旧的回退字体资产；保留用户手工挂的其它 Fallback。</summary>
        private static void RemoveFallback(TMP_FontAsset mainFont)
        {
            var table = mainFont.fallbackFontAssetTable;
            if (table != null)
            {
                for (var i = table.Count - 1; i >= 0; i--)
                {
                    var entry = table[i];
                    if (entry == null || AssetDatabase.GetAssetPath(entry) == FallbackFontAssetPath)
                    {
                        table.RemoveAt(i);
                    }
                }
            }

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(FallbackFontAssetPath);
            }
        }

        private static string BuildUniqueCharacterSequence(string characterSequence)
        {
            var builder = new StringBuilder(characterSequence.Length);
            var seen = new HashSet<int>();

            for (var i = 0; i < characterSequence.Length; i++)
            {
                var codePoint = (int)characterSequence[i];
                var length = 1;

                if (i < characterSequence.Length - 1 &&
                    char.IsHighSurrogate(characterSequence[i]) &&
                    char.IsLowSurrogate(characterSequence[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(characterSequence, i);
                    length = 2;
                }

                if (seen.Add(codePoint))
                {
                    builder.Append(characterSequence, i, length);
                }

                i += length - 1;
            }

            return builder.ToString();
        }

        private static int CountCharacters(string characters)
        {
            if (string.IsNullOrEmpty(characters))
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < characters.Length; i++)
            {
                if (i < characters.Length - 1 &&
                    char.IsHighSurrogate(characters[i]) &&
                    char.IsLowSurrogate(characters[i + 1]))
                {
                    i++;
                }

                count++;
            }

            return count;
        }

        private static string BuildMissingReport(string header, string missingCharacters)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[GameFont] " + header);
            builder.AppendLine("以下字符未写入字体（源字体缺字，或图集空间不足）：");
            builder.AppendLine(missingCharacters);
            builder.AppendLine("若为空间不足，可增大 Atlas 尺寸或开启 Multi Atlas Textures。");
            return builder.ToString();
        }
    }
}
