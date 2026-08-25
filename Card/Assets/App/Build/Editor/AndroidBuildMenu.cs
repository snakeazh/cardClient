using System;
using System.IO;
using System.Linq;
using Framework.Assets.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace App.Build.Editor
{
    /// <summary>
    /// Unity 菜单：一键打 Android APK / AAB。
    /// 打包前会切到 Android 目标；可选先重建 AssetBundles。
    /// </summary>
    public static class AndroidBuildMenu
    {
        private const string FallbackScene = "Assets/Scenes/SampleScene.unity";
        private const string OutputFolderName = "Builds/Android";

        [MenuItem("Build/Android/Build APK", false, 100)]
        public static void BuildApk()
        {
            BuildAndroid(appBundle: false, rebuildBundles: false);
        }

        [MenuItem("Build/Android/Build AAB", false, 101)]
        public static void BuildAab()
        {
            BuildAndroid(appBundle: true, rebuildBundles: false);
        }

        [MenuItem("Build/Android/Build APK (Rebuild Bundles)", false, 110)]
        public static void BuildApkWithBundles()
        {
            BuildAndroid(appBundle: false, rebuildBundles: true);
        }

        [MenuItem("Build/Android/Build AAB (Rebuild Bundles)", false, 111)]
        public static void BuildAabWithBundles()
        {
            BuildAndroid(appBundle: true, rebuildBundles: true);
        }

        [MenuItem("Build/Android/Build APK (Test Logs)", false, 120)]
        public static void BuildApkTestLogs()
        {
            BuildAndroid(appBundle: false, rebuildBundles: false, enableAppLog: true);
        }

        [MenuItem("Build/Android/Open Output Folder", false, 200)]
        public static void OpenOutputFolder()
        {
            var dir = GetOutputDirectory();
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem("Build/Android/Build APK", true)]
        [MenuItem("Build/Android/Build AAB", true)]
        [MenuItem("Build/Android/Build APK (Rebuild Bundles)", true)]
        [MenuItem("Build/Android/Build AAB (Rebuild Bundles)", true)]
        [MenuItem("Build/Android/Build APK (Test Logs)", true)]
        private static bool ValidateBuild()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildAndroid(bool appBundle, bool rebuildBundles, bool enableAppLog = false)
        {
            var formatLabel = appBundle ? "AAB" : "APK";
            if (enableAppLog)
            {
                formatLabel += " Test Logs";
            }
            try
            {
                EditorUtility.DisplayProgressBar($"Build Android {formatLabel}", "切换构建目标…", 0.1f);

                if (!EnsureAndroidBuildTarget())
                {
                    return;
                }

                EnsureBuildScenes();
                EnsureAndroidArchitectures();
                AndroidKeystoreSettingsWindow.ApplyVersionBumpIfNeeded();

                if (!AndroidKeystoreSettingsWindow.ApplySavedSettings(requirePasswords: true))
                {
                    return;
                }

                if (rebuildBundles)
                {
                    EditorUtility.DisplayProgressBar($"Build Android {formatLabel}", "重建 AssetBundles…", 0.3f);
                    ResAssetBundleBuilder.RebuildCurrentVersion();
                }
                else
                {
                    WarnIfStreamingBundlesMissing();
                }

                var scenes = GetEnabledScenes();
                if (scenes.Length == 0)
                {
                    Debug.LogError("[AndroidBuild] 没有可打包的场景。请在 File > Build Settings 中添加场景。");
                    return;
                }

                var outputPath = PrepareOutputPath(appBundle, enableAppLog);
                EditorUtility.DisplayProgressBar(
                    $"Build Android {formatLabel}",
                    $"输出: {Path.GetFileName(outputPath)}",
                    0.6f);

                EditorUserBuildSettings.buildAppBundle = appBundle;

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };
                if (enableAppLog)
                {
                    options.extraScriptingDefines = new[] { "ENABLE_APP_LOG" };
                    Debug.Log("[AndroidBuild] 本包追加 ENABLE_APP_LOG，Info/Debug 会输出。");
                }

                var report = BuildPipeline.BuildPlayer(options);
                LogBuildReport(report, outputPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidBuild] 打包异常: {ex}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool EnsureAndroidBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                return true;
            }

            var ok = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android,
                BuildTarget.Android);
            if (!ok)
            {
                Debug.LogError("[AndroidBuild] 切换到 Android 构建目标失败。请确认已安装 Android Build Support。");
            }

            return ok;
        }

        private static void EnsureBuildScenes()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .ToArray();
            if (enabled.Length > 0)
            {
                return;
            }

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(FallbackScene))
            {
                Debug.LogError($"[AndroidBuild] Build Settings 场景为空，且找不到兜底场景: {FallbackScene}");
                return;
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(FallbackScene, true)
            };
            Debug.LogWarning($"[AndroidBuild] Build Settings 无场景，已自动加入: {FallbackScene}");
        }

        private static string[] GetEnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
        }

        /// <summary>
        /// Google Play 需要 ARM64；同时保留 ARMv7 以覆盖旧设备。
        /// </summary>
        private static void EnsureAndroidArchitectures()
        {
            var required = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            if (PlayerSettings.Android.targetArchitectures == required)
            {
                return;
            }

            PlayerSettings.Android.targetArchitectures = required;
            Debug.Log("[AndroidBuild] 已设置 Android CPU: ARMv7 + ARM64");
        }

        private static void WarnIfStreamingBundlesMissing()
        {
            var bundlesDir = ResAssetBundleBuilder.GetStreamingBundlesFullPath();
            if (!Directory.Exists(bundlesDir) || Directory.GetFiles(bundlesDir).Length == 0)
            {
                Debug.LogWarning(
                    "[AndroidBuild] StreamingAssets/Bundles 为空。真机将无法加载资源。" +
                    "可改用菜单 Build/Android/Build APK (Rebuild Bundles)。");
            }
        }

        private static string PrepareOutputPath(bool appBundle, bool enableAppLog)
        {
            var dir = GetOutputDirectory();
            Directory.CreateDirectory(dir);

            var safeName = SanitizeFileName(PlayerSettings.productName);
            if (string.IsNullOrEmpty(safeName))
            {
                safeName = "Card";
            }

            var version = PlayerSettings.bundleVersion;
            var code = PlayerSettings.Android.bundleVersionCode;
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var tag = enableAppLog ? "_TestLogs" : string.Empty;
            var ext = appBundle ? "aab" : "apk";
            return Path.Combine(dir, $"{safeName}_{version}_{code}{tag}_{stamp}.{ext}");
        }

        private static string GetOutputDirectory()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                              ?? Application.dataPath;
            return Path.Combine(projectRoot, OutputFolderName.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }

        private static void LogBuildReport(BuildReport report, string outputPath)
        {
            if (report == null)
            {
                Debug.LogError("[AndroidBuild] BuildReport 为空。");
                return;
            }

            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log(
                    $"[AndroidBuild] 成功: {outputPath}\n" +
                    $"大小: {summary.totalSize / (1024f * 1024f):F2} MB, 耗时: {summary.totalTime}");
                EditorUtility.RevealInFinder(outputPath);
            }
            else
            {
                Debug.LogError(
                    $"[AndroidBuild] 失败: {summary.result}\n" +
                    $"错误数: {summary.totalErrors}, 请查看 Console。\n" +
                    "若签名失败，请打开 Build/Android/Build Settings 手写 Keystore 路径与密码。");
            }
        }
    }
}
