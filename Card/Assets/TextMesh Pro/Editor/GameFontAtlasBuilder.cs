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
        public const string SourceFontPath = "Assets/TextMesh Pro/Fonts/GameFont.otf";

        public sealed class BuildResult
        {
            public bool Success;
            public int RequestedCharacterCount;
            public int IncludedCharacterCount;
            public int MissingCharacterCount;
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

            var allAdded = fontAsset.TryAddCharacters(uniqueCharacters, out var missingCharacters);
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

            AssetDatabase.SaveAssets();
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);

            var missingCount = CountCharacters(missingCharacters);
            var message =
                $"GameFont 生成完成。字符 {fontAsset.characterTable.Count}/{uniqueCharacters.Length}，" +
                $"未写入 {missingCount}，图集数量 {fontAsset.atlasTextureCount}。";

            if (missingCount > 0)
            {
                Debug.LogWarning(BuildMissingReport(message, missingCharacters), fontAsset);
            }
            else
            {
                Debug.Log("[GameFont] " + message, fontAsset);
            }

            return new BuildResult
            {
                Success = allAdded && missingCount == 0,
                RequestedCharacterCount = uniqueCharacters.Length,
                IncludedCharacterCount = fontAsset.characterTable.Count,
                MissingCharacterCount = missingCount,
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
