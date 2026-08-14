using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace App.Config.Editor
{
    /// <summary>
    /// 编辑器菜单：调用 Tools/ConfigExporter 完成 xlsx → csv/json/C#/Unity 全流程。
    /// </summary>
    public static class ConfigExportMenu
    {
        private const string MenuExport = "Config/一键导出配置表";
        private const string ExporterProject = "Tools/ConfigExporter/ConfigExporter.csproj";
        private const string ConfigFolder = "Config";

        [MenuItem(MenuExport, false, 100)]
        private static void ExportAll()
        {
            var repoRoot = GetRepoRoot();
            var projectPath = Path.Combine(repoRoot, ExporterProject.Replace('/', Path.DirectorySeparatorChar));
            var configDir = Path.Combine(repoRoot, ConfigFolder);

            if (!File.Exists(projectPath))
            {
                Debug.LogError($"[Config] 未找到导出工具: {projectPath}");
                return;
            }

            if (!Directory.Exists(configDir))
            {
                Debug.LogError($"[Config] 未找到配置目录: {configDir}");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("配置导出", "正在执行 ConfigExporter…", 0.5f);

                var arguments =
                    $"run --project \"{projectPath}\" -- --config \"{configDir}\"";
                var exitCode = RunDotnet(repoRoot, arguments, out var output, out var error);

                if (exitCode != 0)
                {
                    Debug.LogError($"[Config] 导出失败 (exit={exitCode})\n{output}\n{error}");
                    return;
                }

                Debug.Log($"[Config] 导出完成\n{output}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Config] 导出异常: {ex.Message}\n请确认已安装 .NET SDK 且 dotnet 在 PATH 中。");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
        }

        [MenuItem(MenuExport, true)]
        private static bool ExportAllValidate()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static int RunDotnet(string workingDirectory, string arguments, out string output, out string error)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动 dotnet 进程。");
                }

                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static string GetRepoRoot()
        {
            // Application.dataPath = <repo>/Card/Assets
            var unityProject = Directory.GetParent(UnityEngine.Application.dataPath);
            return unityProject?.Parent?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
