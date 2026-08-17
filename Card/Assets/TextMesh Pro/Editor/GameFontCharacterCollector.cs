using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TMPro.EditorUtilities
{
    /// <summary>
    /// 从 Prefab（TMP m_text + Legacy m_Text）、配置 JSON 与 C# 源码中收集 UI 用字，写入 Lang-zh.txt。
    /// C# 扫描 Assets/App、Assets/Framework 下非 Editor 脚本的字符串 / 逐字 / 插值字面量（跳过注释）。
    /// </summary>
    public static class GameFontCharacterCollector
    {
        public const string LangZhAssetPath = "Assets/TextMesh Pro/Fonts/Lang-zh.txt";

        private static readonly string[] ScriptRoots = { "App", "Framework" };

        private static readonly Regex PrefabTextRegex = new Regex(
            @"^\s*m_[Tt]ext:\s*(?:""(?<quoted>(?:\\.|[^""\\])*)""|'(?<single>(?:\\.|[^'\\])*)'|(?<plain>[^\r\n]+?))\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

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

            var prefabRoot = Path.Combine(AssetsRoot, "Res");
            var prefabPaths = Directory.Exists(prefabRoot)
                ? Directory.GetFiles(prefabRoot, "*.prefab", SearchOption.AllDirectories)
                : Array.Empty<string>();

            foreach (var prefabPath in NormalizePaths(prefabPaths))
            {
                AddCharactersFromPrefab(prefabPath, characters);
            }

            var jsonRoot = Path.Combine(AssetsRoot, "Res", "Config");
            var jsonPaths = Directory.Exists(jsonRoot)
                ? Directory.GetFiles(jsonRoot, "*.json", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            var jsonTextFields = LoadJsonTextFieldNames();
            foreach (var jsonPath in NormalizePaths(jsonPaths))
            {
                AddCharactersFromJson(jsonPath, jsonTextFields, characters);
            }

            var scriptFileCount = 0;
            if (includeAppScripts)
            {
                foreach (var rootName in ScriptRoots)
                {
                    var scriptRoot = Path.Combine(AssetsRoot, rootName);
                    if (!Directory.Exists(scriptRoot))
                    {
                        continue;
                    }

                    foreach (var scriptPath in NormalizePaths(
                                 Directory.GetFiles(scriptRoot, "*.cs", SearchOption.AllDirectories)))
                    {
                        if (scriptPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        AddCharactersFromCSharp(scriptPath, characters);
                        scriptFileCount++;
                    }
                }
            }

            var output = BuildCharacterSequence(characters);
            var absolutePath = LangZhAbsolutePath;
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

        private static string AssetsRoot =>
            string.IsNullOrEmpty(Application.dataPath)
                ? Path.GetFullPath("Assets")
                : Application.dataPath;

        private static string LangZhAbsolutePath =>
            Path.Combine(AssetsRoot, "TextMesh Pro", "Fonts", "Lang-zh.txt");

        public static string ReadCharacterSequenceFromLangFile()
        {
            var absolutePath = LangZhAbsolutePath;
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
            var absolutePath = LangZhAbsolutePath;
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

            var generatedConfigRoot = Path.Combine(AssetsRoot, "App", "Config", "Generated");
            if (!Directory.Exists(generatedConfigRoot))
            {
                return fieldNames;
            }

            var generatedFiles = Directory.GetFiles(generatedConfigRoot, "*.cs", SearchOption.TopDirectoryOnly);
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

                AddText(ParseCharacterFileText(UnescapeYamlString(raw)), characters);
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
            var i = 0;
            var length = source.Length;
            while (i < length)
            {
                var c = source[i];

                if (c == '/' && i + 1 < length)
                {
                    if (source[i + 1] == '/')
                    {
                        i += 2;
                        while (i < length && source[i] != '\n')
                        {
                            i++;
                        }

                        continue;
                    }

                    if (source[i + 1] == '*')
                    {
                        i += 2;
                        while (i + 1 < length && !(source[i] == '*' && source[i + 1] == '/'))
                        {
                            i++;
                        }

                        i = Math.Min(length, i + 2);
                        continue;
                    }
                }

                if (c == '\'')
                {
                    if (TryReadCharLiteral(source, ref i, out var charLiteral))
                    {
                        AddText(UnescapeCSharpString(charLiteral), characters);
                    }

                    continue;
                }

                if (c == '"' || c == '@' || c == '$')
                {
                    if (TryReadStringLiteral(source, ref i, out var literal, out var isVerbatim))
                    {
                        var text = isVerbatim
                            ? literal.Replace("\"\"", "\"")
                            : UnescapeCSharpString(literal);
                        if (ContainsNonAscii(text))
                        {
                            AddText(text, characters);
                        }

                        continue;
                    }
                }

                i++;
            }
        }

        private static bool TryReadStringLiteral(string source, ref int index, out string literal, out bool isVerbatim)
        {
            literal = null;
            isVerbatim = false;
            var start = index;
            var length = source.Length;
            var i = index;

            if (i < length && source[i] == '$')
            {
                i++;
            }

            if (i < length && source[i] == '@')
            {
                isVerbatim = true;
                i++;
                if (source[start] == '@' && i < length && source[i] == '$')
                {
                    i++;
                }
            }

            if (i >= length || source[i] != '"')
            {
                return false;
            }

            i++;
            var contentStart = i;
            if (isVerbatim)
            {
                while (i < length)
                {
                    if (source[i] != '"')
                    {
                        i++;
                        continue;
                    }

                    if (i + 1 < length && source[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }

                    literal = source.Substring(contentStart, i - contentStart);
                    index = i + 1;
                    return true;
                }
            }
            else
            {
                while (i < length)
                {
                    if (source[i] == '\\' && i + 1 < length)
                    {
                        i += 2;
                        continue;
                    }

                    if (source[i] == '"')
                    {
                        literal = source.Substring(contentStart, i - contentStart);
                        index = i + 1;
                        return true;
                    }

                    i++;
                }
            }

            return false;
        }

        private static bool TryReadCharLiteral(string source, ref int index, out string literal)
        {
            literal = null;
            var length = source.Length;
            var i = index + 1;
            var contentStart = i;
            while (i < length)
            {
                if (source[i] == '\\' && i + 1 < length)
                {
                    i += 2;
                    continue;
                }

                if (source[i] == '\'')
                {
                    literal = source.Substring(contentStart, i - contentStart);
                    index = i + 1;
                    return true;
                }

                if (source[i] == '\n')
                {
                    break;
                }

                i++;
            }

            index++;
            return false;
        }

        private static bool ContainsNonAscii(string text)
        {
            foreach (var ch in text)
            {
                if (ch > 127)
                {
                    return true;
                }
            }

            return false;
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
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
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
                    case '\'':
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
