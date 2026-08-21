using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Framework.Assets.Editor
{
    /// <summary>
    /// 1) Build AssetBundles from Assets/Res to ../Bundles/{version}/ (semver auto-increment from 1.0.0)
    /// 2) Copy bundle files into Assets/StreamingAssets/Bundles for runtime.
    /// </summary>
    public static class ResAssetBundleBuilder
    {
        public const string ResRoot = ResPaths.AssetRoot;
        public const string StreamingBundlesRelative = "StreamingAssets/Bundles";

        /// <summary>
        /// 相对 Assets/Res 的路径前缀，这些目录不参与 AssetBundle 打包。
        /// </summary>
        private static readonly string[] ExcludedResRelativePrefixes =
        {
            "Textures/test/"
        };

        public static string GetStreamingBundlesAssetPath()
        {
            return Path.Combine("Assets", StreamingBundlesRelative);
        }

        public static string GetStreamingBundlesFullPath()
        {
            return Path.Combine(Application.dataPath, "StreamingAssets", "Bundles");
        }


        [MenuItem("Res/Build AssetBundles")]
        public static void Build()
        {
            BuildInternal(bumpVersion: true);
        }

        /// <summary>
        /// Rebuild bundles without bumping semver (used after prefab regeneration).
        /// </summary>
        public static void RebuildCurrentVersion()
        {
            BuildInternal(bumpVersion: false);
        }

        [MenuItem("Res/Clear Bundle Dir")]
        public static void ClearBundleDirMenu()
        {
            var versionHint = BundleVersionManager.ReadSavedVersion();
            var versionLine = string.IsNullOrWhiteSpace(versionHint)
                ? "当前无已保存版本号。"
                : $"保留版本号：{versionHint}（下次 Build 继续从此版本递增）。";

            if (!EditorUtility.DisplayDialog(
                    "Clear Bundle Dir",
                    "将清空以下目录内容：\n" +
                    $"• {BundleVersionManager.GetBundlesRoot()}\n" +
                    $"• {GetStreamingBundlesFullPath()}\n\n" +
                    versionLine,
                    "清空",
                    "取消"))
            {
                return;
            }

            ClearBundleDir();
        }

        /// <summary>
        /// 清空 Card/Bundles 与 StreamingAssets/Bundles 的产物；保留 Bundles/version.txt 版本号。
        /// </summary>
        public static void ClearBundleDir()
        {
            var bundlesRoot = BundleVersionManager.GetBundlesRoot();
            var streaming = GetStreamingBundlesFullPath();
            var preservedVersion = BundleVersionManager.ReadSavedVersion();

            var clearedRoot = ClearDirectoryContents(bundlesRoot);
            var clearedStreaming = ClearDirectoryContents(streaming);

            if (!string.IsNullOrWhiteSpace(preservedVersion) &&
                BundleVersionManager.IsValidSemVer(preservedVersion))
            {
                Directory.CreateDirectory(bundlesRoot);
                File.WriteAllText(BundleVersionManager.GetVersionFilePath(), preservedVersion);
            }

            AssetDatabase.Refresh();

            Debug.Log(
                $"[Res] Cleared Bundle Dir.\n" +
                $"Bundles root: {(clearedRoot ? "ok" : "skip/missing")} → {bundlesRoot}\n" +
                $"Streaming: {(clearedStreaming ? "ok" : "skip/missing")} → {streaming}\n" +
                $"Preserved version: {(string.IsNullOrWhiteSpace(preservedVersion) ? "(none)" : preservedVersion)}");
        }

        private static bool ClearDirectoryContents(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (var subDir in Directory.GetDirectories(directory))
            {
                Directory.Delete(subDir, recursive: true);
            }

            return true;
        }

        private static void BuildInternal(bool bumpVersion)
        {
            if (!AssetDatabase.IsValidFolder(ResRoot))
            {
                Debug.LogError($"Res folder not found: {ResRoot}");
                return;
            }

            // 清掉历史写在 meta 上的 Bundle 名，避免与 AssetBundleBuild 分组冲突。
            ClearBundleNames();

            var builds = CollectAssetBundleBuilds();
            if (builds.Length == 0)
            {
                Debug.LogError($"No assets found under {ResRoot} to build.");
                return;
            }

            var version = bumpVersion
                ? BundleVersionManager.BumpAndSaveNextBuildVersion()
                : BundleVersionManager.GetOrInitializeVersion();
            var versionOutput = BundleVersionManager.GetVersionOutputPath(version);
            Directory.CreateDirectory(versionOutput);

            var manifest = BuildPipeline.BuildAssetBundles(
                versionOutput,
                builds,
                BuildAssetBundleOptions.None,
                EditorUserBuildSettings.activeBuildTarget);

            if (manifest == null)
            {
                Debug.LogError("AssetBundle build failed.");
                return;
            }

            CopyToStreamingAssets(versionOutput, version);
            WriteStreamingVersion(version);
            WriteStreamingCatalog();

            AssetDatabase.Refresh();

            Debug.Log(
                $"AssetBundles v{version} built to {versionOutput} and copied to {GetStreamingBundlesAssetPath()} " +
                $"({EditorUserBuildSettings.activeBuildTarget}), {builds.Length} bundles");
        }

        private static void WriteStreamingVersion(string version)
        {
            var dest = GetStreamingBundlesFullPath();
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "version.txt"), version);
        }

        private static void CopyToStreamingAssets(string versionOutput, string version)
        {
            var dest = GetStreamingBundlesFullPath();
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(versionOutput))
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                File.Copy(file, Path.Combine(dest, name), overwrite: true);
            }

            // The manifest bundle is named after the version folder (e.g. "1.0.4");
            // also copy it under the fixed name the runtime looks for.
            var manifestBundle = Path.Combine(versionOutput, version);
            if (File.Exists(manifestBundle))
            {
                File.Copy(manifestBundle, Path.Combine(dest, "Bundles"), overwrite: true);
            }
        }

        private static void WriteStreamingCatalog()
        {
            var dest = GetStreamingBundlesFullPath();
            Directory.CreateDirectory(dest);

            var names = new List<string>();
            foreach (var file in Directory.GetFiles(dest))
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrEmpty(name) ||
                    name == "catalog.txt" ||
                    name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                names.Add(name);
            }

            names.Sort(StringComparer.Ordinal);
            File.WriteAllLines(Path.Combine(dest, "catalog.txt"), names);
        }

        /// <summary>
        /// 按 Assets/Res 下一级目录分组（与运行时 ResourceKeyResolver 一致）。
        /// </summary>
        private static AssetBundleBuild[] CollectAssetBundleBuilds()
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { ResRoot });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || Directory.Exists(path))
                {
                    continue;
                }

                var relative = path.Substring(ResRoot.Length + 1).Replace('\\', '/');
                if (IsExcludedResAsset(relative))
                {
                    continue;
                }

                var slash = relative.IndexOf('/');
                var bundleName = slash > 0
                    ? relative.Substring(0, slash).ToLowerInvariant()
                    : Path.GetFileNameWithoutExtension(relative).ToLowerInvariant();

                if (!groups.TryGetValue(bundleName, out var list))
                {
                    list = new List<string>();
                    groups.Add(bundleName, list);
                }

                list.Add(path);
            }

            var builds = new AssetBundleBuild[groups.Count];
            var index = 0;
            foreach (var pair in groups)
            {
                builds[index++] = new AssetBundleBuild
                {
                    assetBundleName = pair.Key,
                    assetNames = pair.Value.ToArray()
                };
            }

            return builds;
        }

        private static bool IsExcludedResAsset(string relativePath)
        {
            foreach (var prefix in ExcludedResRelativePrefixes)
            {
                if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 清空工程内历史 AssetBundleName，避免残留干扰本次 Build。
        /// </summary>
        private static void ClearBundleNames()
        {
            var bundleNames = AssetDatabase.GetAllAssetBundleNames();
            foreach (var bundleName in bundleNames)
            {
                var assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                foreach (var path in assetPaths)
                {
                    var importer = AssetImporter.GetAtPath(path);
                    if (importer == null || string.IsNullOrEmpty(importer.assetBundleName))
                    {
                        continue;
                    }

                    // 必须先清 variant 再清 name；name 为空时不能再写 variant。
                    importer.assetBundleVariant = string.Empty;
                    importer.assetBundleName = string.Empty;
                }
            }

            AssetDatabase.RemoveUnusedAssetBundleNames();
        }
    }
}
