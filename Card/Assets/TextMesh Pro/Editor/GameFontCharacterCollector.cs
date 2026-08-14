using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TMPro.EditorUtilities
{
    /// <summary>
    /// 从 Prefab（TMP m_text + Legacy m_Text）、配置 JSON 与业务 C# 源码中收集 UI 用字，写入 Lang-zh.txt。
    /// </summary>
    public static class GameFontCharacterCollector
    {
        public const string LangZhAssetPath = "Assets/TextMesh Pro/Fonts/Lang-zh.txt";

        private const string PrefabRoot = "Assets/Res";
        private const string JsonConfigRoot = "Assets/Res/Config";
        private const string GeneratedConfigRoot = "Assets/App/Config/Generated";
        private const string AppScriptRoot = "Assets/App";

        private static readonly Regex PrefabTextRegex = new Regex(
            @"^\s*m_[Tt]ext:\s*(?:""(?<quoted>(?:\\.|[^""\\])*)""|'(?<single>(?:\\.|[^'\\])*)'|(?<plain>[^\r\n]+?))\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex CSharpStringRegex = new Regex(
            @"(?<quote>@?"")(?<content>(?:\\.|[^""\\])*)(?<end>"")",
            RegexOptions.Compiled);

        private static readonly Regex JsonStringFieldRegex = new Regex(
            @"""([^""\\]+)""\s*:\s*""((?:\\.|[^""\\])*)""",
            RegexOptions.Compiled);

        private static readonly Regex UnicodeEscapeRegex = new Regex(
            @"(?<!\\)(?:\\u[0-9a-fA-F]{4}|\\U[0-9a-fA-F]{8})",
            RegexOptions.Compiled);

        public sealed class CollectResult
        {
            public int PrefabCount;
            public int JsonFileCount;
            public int ScriptFileCount;
            public int CharacterCount;
            public string OutputPath;
        }

        public static CollectResult CollectToLangZhFile(bool includeAppScripts = true, bool preserveManualCharacters = true)
        {
            var characters = new SortedSet<int>();

            AddBaselineCharacters(characters);

            if (preserveManualCharacters)
            {
                AddCharactersFromExistingLangFile(characters);
            }

            var prefabPaths = Directory.Exists(PrefabRoot)
                ? Directory.GetFiles(PrefabRoot, "*.prefab", SearchOption.AllDirectories)
                : Array.Empty<string>();

            foreach (var prefabPath in NormalizePaths(prefabPaths))
            {
                AddCharactersFromPrefab(prefabPath, characters);
            }

            var jsonPaths = Directory.Exists(JsonConfigRoot)
                ? Directory.GetFiles(JsonConfigRoot, "*.json", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            var jsonTextFields = LoadJsonTextFieldNames();
            foreach (var jsonPath in NormalizePaths(jsonPaths))
            {
                AddCharactersFromJson(jsonPath, jsonTextFields, characters);
            }

            var scriptFileCount = 0;
            if (includeAppScripts && Directory.Exists(AppScriptRoot))
            {
                var scriptPaths = Directory.GetFiles(AppScriptRoot, "*.cs", SearchOption.AllDirectories);
                foreach (var scriptPath in NormalizePaths(scriptPaths))
                {
                    if (scriptPath.Replace('\\', '/').Contains("/Editor/"))
                    {
                        continue;
                    }

                    AddCharactersFromCSharp(scriptPath, characters);
                    scriptFileCount++;
                }
            }

            var output = BuildCharacterSequence(characters);
            var absolutePath = Path.GetFullPath(LangZhAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            File.WriteAllText(absolutePath, output, new UTF8Encoding(false));

            return new CollectResult
            {
                PrefabCount = prefabPaths.Length,
                JsonFileCount = jsonPaths.Length,
                ScriptFileCount = scriptFileCount,
                CharacterCount = characters.Count,
                OutputPath = LangZhAssetPath
            };
        }

        public static string ReadCharacterSequenceFromLangFile()
        {
            var absolutePath = Path.GetFullPath(LangZhAssetPath);
            if (!File.Exists(absolutePath))
            {
                return string.Empty;
            }

            return ParseCharacterFileText(File.ReadAllText(absolutePath, Encoding.UTF8));
        }

        public static string ParseCharacterFileText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return UnicodeEscapeRegex.Replace(text, match =>
                char.ConvertFromUtf32(int.Parse(
                    match.Value.Substring(2),
                    System.Globalization.NumberStyles.HexNumber)));
        }

        private static void AddBaselineCharacters(ISet<int> characters)
        {
            for (var codePoint = 0x20; codePoint <= 0x7E; codePoint++)
            {
                characters.Add(codePoint);
            }

            const string commonPunctuation = "，。！？：；、（）【】《》“”‘’…—·";
            AddText(commonPunctuation, characters);
        }

        private static void AddCharactersFromExistingLangFile(ISet<int> characters)
        {
            var absolutePath = Path.GetFullPath(LangZhAssetPath);
            if (!File.Exists(absolutePath))
            {
                return;
            }

            AddText(ParseCharacterFileText(File.ReadAllText(absolutePath, Encoding.UTF8)), characters);
        }

        private static HashSet<string> LoadJsonTextFieldNames()
        {
            var fieldNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "Desc", "Name", "Title", "Tip", "Hint", "Message", "Effect", "Label", "Text", "Content", "Dialog"
            };

            if (!Directory.Exists(GeneratedConfigRoot))
            {
                return fieldNames;
            }

            var generatedFiles = Directory.GetFiles(GeneratedConfigRoot, "*.cs", SearchOption.TopDirectoryOnly);
            var fieldRegex = new Regex(@"public\s+string\s+(\w+)\s*;", RegexOptions.Compiled);
            foreach (var file in generatedFiles)
            {
                var source = File.ReadAllText(file, Encoding.UTF8);
                foreach (Match match in fieldRegex.Matches(source))
                {
                    fieldNames.Add(match.Groups[1].Value);
                }
            }

            return fieldNames;
        }

        private static void AddCharactersFromPrefab(string prefabPath, ISet<int> characters)
        {
            var yaml = File.ReadAllText(prefabPath, Encoding.UTF8);
            foreach (Match match in PrefabTextRegex.Matches(yaml))
            {
                var raw = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["single"].Success
                        ? match.Groups["single"].Value
                        : match.Groups["plain"].Value;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                AddText(UnescapeYamlString(raw), characters);
            }
        }

        private static void AddCharactersFromJson(string jsonPath, HashSet<string> textFieldNames, ISet<int> characters)
        {
            var json = File.ReadAllText(jsonPath, Encoding.UTF8);
            foreach (Match match in JsonStringFieldRegex.Matches(json))
            {
                var fieldName = match.Groups[1].Value;
                if (!textFieldNames.Contains(fieldName))
                {
                    continue;
                }

                AddText(UnescapeJsonString(match.Groups[2].Value), characters);
            }
        }

        private static void AddCharactersFromCSharp(string scriptPath, ISet<int> characters)
        {
            var source = File.ReadAllText(scriptPath, Encoding.UTF8);
            foreach (Match match in CSharpStringRegex.Matches(source))
            {
                var literal = match.Groups["content"].Value;
                if (!ShouldCollectFromScriptString(literal))
                {
                    continue;
                }

                AddText(UnescapeCSharpString(literal), characters);
            }
        }

        private static void AddText(string text, ISet<int> characters)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    characters.Add(char.ConvertToUtf32(text, i));
                    i++;
                    continue;
                }

                // 控制字符与空白由 TMP 自行合成，写进字符文件只会污染文本。
                if (text[i] < 0x21 || text[i] == 0x7F)
                {
                    continue;
                }

                characters.Add(text[i]);
            }
        }

        private static bool ShouldCollectFromScriptString(string text)
        {
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch) || ch < 128)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static string BuildCharacterSequence(IEnumerable<int> codePoints)
        {
            var builder = new StringBuilder();
            foreach (var codePoint in codePoints)
            {
                builder.Append(char.ConvertFromUtf32(codePoint));
            }

            return builder.ToString();
        }

        private static string UnescapeYamlString(string value)
        {
            return value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\'", "'")
                .Replace("\\\\", "\\");
        }

        private static string UnescapeJsonString(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(ch);
                    continue;
                }

                var next = value[++i];
                switch (next)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(next);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (i + 4 < value.Length &&
                            int.TryParse(value.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var unicode))
                        {
                            builder.Append((char)unicode);
                            i += 4;
                        }
                        break;
                    default:
                        builder.Append(next);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string UnescapeCSharpString(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(ch);
                    continue;
                }

                var next = value[++i];
                switch (next)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case '"':
                    case '\\':
                        builder.Append(next);
                        break;
                    case 'u':
                        if (i + 4 < value.Length &&
                            int.TryParse(value.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var unicode))
                        {
                            builder.Append((char)unicode);
                            i += 4;
                        }
                        break;
                    default:
                        builder.Append(next);
                        break;
                }
            }

            return builder.ToString();
        }

        private static IEnumerable<string> NormalizePaths(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                yield return path.Replace('\\', '/');
            }
        }
    }
}
