using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace App.Build.Editor
{
    /// <summary>
    /// 游戏上架 / 软著审核用：一键导出源代码前 N 行与后 N 行（默认每段 50 行），
    /// 或按「每页 50 行、前 30 页 + 后 30 页」生成连续鉴别材料。
    /// </summary>
    public sealed class ReviewCodeExportWindow : EditorWindow
    {
        private const string MenuPath = "Tools/审核材料/导出代码前后50行";
        private const int DefaultSegmentLines = 50;
        private const int DefaultPageLines = 50;
        private const int DefaultFrontPages = 30;
        private const int DefaultBackPages = 30;

        private string _sourceRelative = "Assets/App";
        private string _extensions = ".cs";
        private string _excludeFolders = "Generated;Plugins;ThirdParty;Editor";
        private string _softwareName = "卡牌游戏";
        private string _softwareVersion = "V1.0";
        private int _segmentLines = DefaultSegmentLines;
        private int _pageLines = DefaultPageLines;
        private int _frontPages = DefaultFrontPages;
        private int _backPages = DefaultBackPages;
        private ExportMode _mode = ExportMode.PerFileHeadTail;
        private bool _skipBlankLines;
        private bool _openFolderWhenDone = true;
        private Vector2 _scroll;
        private string _lastResult = string.Empty;

        private enum ExportMode
        {
            /// <summary>每个源文件各导出前 N 行 + 后 N 行。</summary>
            PerFileHeadTail = 0,

            /// <summary>合并核心代码后，按每页 N 行取前 M 页 + 后 M 页（软著常用）。</summary>
            SoftCopyrightPages = 1
        }

        [MenuItem(MenuPath, false, 500)]
        private static void Open()
        {
            var window = GetWindow<ReviewCodeExportWindow>("审核代码导出");
            window.minSize = new Vector2(480f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("上架 / 软著代码鉴别材料导出", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "PerFile：每个 .cs 导出前 50 行 + 后 50 行。\n" +
                "软著页：合并代码后按每页 50 行，导出前 30 页 + 后 30 页（不足则全量）。",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            _softwareName = EditorGUILayout.TextField("软件全称（页眉）", _softwareName);
            _softwareVersion = EditorGUILayout.TextField("版本号（页眉）", _softwareVersion);
            _sourceRelative = EditorGUILayout.TextField("源码目录（相对工程）", _sourceRelative);
            _extensions = EditorGUILayout.TextField("扩展名（; 分隔）", _extensions);
            _excludeFolders = EditorGUILayout.TextField("排除目录名（; 分隔）", _excludeFolders);

            EditorGUILayout.Space(6f);
            _mode = (ExportMode)EditorGUILayout.EnumPopup("导出模式", _mode);
            if (_mode == ExportMode.PerFileHeadTail)
            {
                _segmentLines = Mathf.Max(1, EditorGUILayout.IntField("前后各取行数", _segmentLines));
            }
            else
            {
                _pageLines = Mathf.Max(1, EditorGUILayout.IntField("每页行数", _pageLines));
                _frontPages = Mathf.Max(0, EditorGUILayout.IntField("前页数", _frontPages));
                _backPages = Mathf.Max(0, EditorGUILayout.IntField("后页数", _backPages));
            }

            _skipBlankLines = EditorGUILayout.ToggleLeft("导出时跳过空行（软著建议勾选）", _skipBlankLines);
            _openFolderWhenDone = EditorGUILayout.ToggleLeft("完成后打开导出目录", _openFolderWhenDone);

            EditorGUILayout.Space(12f);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_sourceRelative)))
            {
                if (GUILayout.Button("一键导出", GUILayout.Height(36f)))
                {
                    Export();
                }
            }

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(_lastResult, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void Export()
        {
            try
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var sourceDir = Path.GetFullPath(Path.Combine(projectRoot, _sourceRelative.Replace('/', Path.DirectorySeparatorChar)));
                if (!Directory.Exists(sourceDir))
                {
                    EditorUtility.DisplayDialog("导出失败", $"源码目录不存在：\n{sourceDir}", "确定");
                    return;
                }

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var outRoot = Path.Combine(projectRoot, "Builds", "ReviewCode", stamp);
                Directory.CreateDirectory(outRoot);

                var files = CollectSourceFiles(sourceDir);
                if (files.Count == 0)
                {
                    EditorUtility.DisplayDialog("导出失败", "未找到可导出的源文件，请检查目录与扩展名。", "确定");
                    return;
                }

                EditorUtility.DisplayProgressBar("审核代码导出", "正在导出…", 0.2f);
                int fileCount;
                string mainPath;
                if (_mode == ExportMode.PerFileHeadTail)
                {
                    fileCount = ExportPerFileHeadTail(files, projectRoot, outRoot, out mainPath);
                }
                else
                {
                    fileCount = ExportSoftCopyrightPages(files, projectRoot, outRoot, out mainPath);
                }

                WriteIndex(outRoot, files.Count, fileCount, mainPath);
                EditorUtility.ClearProgressBar();

                _lastResult = $"已导出 {fileCount} 个文件 →\n{outRoot}";
                Debug.Log($"[ReviewCode] {_lastResult}");
                if (_openFolderWhenDone)
                {
                    EditorUtility.RevealInFinder(mainPath);
                }

                EditorUtility.DisplayDialog("导出完成", _lastResult, "确定");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("导出失败", ex.Message, "确定");
            }
        }

        private List<string> CollectSourceFiles(string sourceDir)
        {
            var exts = ParseList(_extensions)
                .Select(e => e.StartsWith(".", StringComparison.Ordinal) ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var excludes = ParseList(_excludeFolders)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var ext = Path.GetExtension(path);
                    if (!exts.Contains(ext))
                    {
                        return false;
                    }

                    var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    for (var i = 0; i < parts.Length; i++)
                    {
                        if (excludes.Contains(parts[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private int ExportPerFileHeadTail(
            List<string> files,
            string projectRoot,
            string outRoot,
            out string indexOrSamplePath)
        {
            var perFileDir = Path.Combine(outRoot, "PerFile");
            Directory.CreateDirectory(perFileDir);
            var combined = new StringBuilder();
            AppendHeader(combined, "按文件前后行汇总");

            var exported = 0;
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                EditorUtility.DisplayProgressBar("审核代码导出", Path.GetFileName(file), (float)i / files.Count);

                var relative = ToProjectRelative(projectRoot, file);
                var lines = ReadLines(file);
                var segment = BuildHeadTailSegment(lines, _segmentLines);

                var safeName = relative.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
                var outPath = Path.Combine(perFileDir, safeName + ".txt");
                var body = new StringBuilder();
                AppendHeader(body, relative);
                body.AppendLine($"// ===== 前 {_segmentLines} 行 =====");
                AppendLines(body, segment.Head);
                if (segment.HasOverlap)
                {
                    body.AppendLine("// （文件较短，前后段有重叠，已去重输出全文）");
                }
                else
                {
                    body.AppendLine();
                    body.AppendLine($"// ===== 后 {_segmentLines} 行 =====");
                    AppendLines(body, segment.Tail);
                }

                File.WriteAllText(outPath, body.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                combined.AppendLine();
                combined.AppendLine($"// ---------- {relative} ----------");
                combined.Append(body);
                exported++;
            }

            var combinedPath = Path.Combine(outRoot, "代码前后行_汇总.txt");
            File.WriteAllText(combinedPath, combined.ToString(), new UTF8Encoding(false));
            indexOrSamplePath = combinedPath;
            return exported;
        }

        private int ExportSoftCopyrightPages(
            List<string> files,
            string projectRoot,
            string outRoot,
            out string mainPath)
        {
            var allLines = new List<string>(4096);
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                EditorUtility.DisplayProgressBar("审核代码导出", Path.GetFileName(file), (float)i / files.Count);
                var relative = ToProjectRelative(projectRoot, file);
                allLines.Add($"// ===== FILE: {relative} =====");
                allLines.AddRange(ReadLines(file));
                allLines.Add(string.Empty);
            }

            if (_skipBlankLines)
            {
                allLines = allLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            }

            var frontLineCount = _frontPages * _pageLines;
            var backLineCount = _backPages * _pageLines;
            var totalNeeded = frontLineCount + backLineCount;

            List<string> selected;
            if (allLines.Count <= totalNeeded)
            {
                selected = allLines;
            }
            else
            {
                selected = new List<string>(totalNeeded);
                selected.AddRange(allLines.Take(frontLineCount));
                selected.AddRange(allLines.Skip(allLines.Count - backLineCount));
            }

            var sb = new StringBuilder(selected.Count * 64);
            AppendHeader(sb, "软著鉴别材料（连续页）");
            sb.AppendLine($"// 源文件数: {files.Count}");
            sb.AppendLine($"// 有效行数: {allLines.Count}，导出行数: {selected.Count}");
            sb.AppendLine($"// 版式: 每页 {_pageLines} 行 × 前 {_frontPages} 页 + 后 {_backPages} 页");
            sb.AppendLine();

            var pageIndex = 1;
            for (var i = 0; i < selected.Count; i++)
            {
                if (i % _pageLines == 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"---------- {_softwareName} {_softwareVersion}  第 {pageIndex} 页 ----------");
                    pageIndex++;
                }

                sb.AppendLine(selected[i]);
            }

            mainPath = Path.Combine(outRoot, "软著代码_前页后页.txt");
            File.WriteAllText(mainPath, sb.ToString(), new UTF8Encoding(false));

            // 便于打印成 PDF 的简易 HTML（等宽、页眉）
            var htmlPath = Path.Combine(outRoot, "软著代码_前页后页.html");
            File.WriteAllText(htmlPath, BuildPrintableHtml(selected), new UTF8Encoding(false));
            return 1;
        }

        private string BuildPrintableHtml(List<string> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>{EscapeHtml(_softwareName)} {_softwareVersion} 源代码</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("@page { size: A4; margin: 16mm 14mm; }");
            sb.AppendLine("body { font-family: Consolas, 'Courier New', monospace; font-size: 10pt; line-height: 1.25; }");
            sb.AppendLine(".page { page-break-after: always; white-space: pre-wrap; word-break: break-all; }");
            sb.AppendLine(".header { font-weight: bold; margin-bottom: 8px; border-bottom: 1px solid #333; padding-bottom: 4px; }");
            sb.AppendLine("</style></head><body>");

            var pageIndex = 1;
            for (var i = 0; i < lines.Count; i += _pageLines)
            {
                var chunk = lines.Skip(i).Take(_pageLines);
                sb.AppendLine("<div class=\"page\">");
                sb.AppendLine($"<div class=\"header\">{EscapeHtml(_softwareName)} {EscapeHtml(_softwareVersion)}　第 {pageIndex} 页</div>");
                sb.Append("<pre>");
                foreach (var line in chunk)
                {
                    sb.AppendLine(EscapeHtml(line));
                }

                sb.AppendLine("</pre></div>");
                pageIndex++;
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void WriteIndex(string outRoot, int scanned, int exported, string mainPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{_softwareName} {_softwareVersion}");
            sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"模式: {_mode}");
            sb.AppendLine($"扫描文件: {scanned}");
            sb.AppendLine($"导出条目: {exported}");
            sb.AppendLine($"主文件: {mainPath}");
            sb.AppendLine();
            sb.AppendLine("说明:");
            sb.AppendLine("1. PerFile 模式适合平台要求「各文件前后 50 行」。");
            sb.AppendLine("2. 软著页模式生成 txt + html；用浏览器打开 html → 打印 → 另存 PDF。");
            sb.AppendLine("3. 提交前请核对页眉软件名/版本号与申请表一致，并剔除密钥等敏感信息。");
            File.WriteAllText(Path.Combine(outRoot, "README.txt"), sb.ToString(), new UTF8Encoding(false));
        }

        private void AppendHeader(StringBuilder sb, string title)
        {
            sb.AppendLine($"// {_softwareName} {_softwareVersion}");
            sb.AppendLine($"// {title}");
            sb.AppendLine($"// 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
        }

        private static void AppendLines(StringBuilder sb, IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                sb.AppendLine(line);
            }
        }

        private List<string> ReadLines(string path)
        {
            var raw = File.ReadAllLines(path, Encoding.UTF8);
            if (!_skipBlankLines)
            {
                return raw.ToList();
            }

            return raw.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        }

        private static HeadTailSegment BuildHeadTailSegment(List<string> lines, int count)
        {
            if (lines.Count == 0)
            {
                return new HeadTailSegment(Array.Empty<string>(), Array.Empty<string>(), true);
            }

            if (lines.Count <= count * 2)
            {
                return new HeadTailSegment(lines.ToArray(), Array.Empty<string>(), true);
            }

            var head = lines.Take(count).ToArray();
            var tail = lines.Skip(lines.Count - count).ToArray();
            return new HeadTailSegment(head, tail, false);
        }

        private static string ToProjectRelative(string projectRoot, string fullPath)
        {
            var full = Path.GetFullPath(fullPath);
            var root = Path.GetFullPath(projectRoot);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
            }

            return full.Replace('\\', '/');
        }

        private static IEnumerable<string> ParseList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                yield break;
            }

            foreach (var part in raw.Split(new[] { ';', ',', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (t.Length > 0)
                {
                    yield return t;
                }
            }
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private readonly struct HeadTailSegment
        {
            public HeadTailSegment(string[] head, string[] tail, bool hasOverlap)
            {
                Head = head;
                Tail = tail;
                HasOverlap = hasOverlap;
            }

            public string[] Head { get; }
            public string[] Tail { get; }
            public bool HasOverlap { get; }
        }
    }
}
