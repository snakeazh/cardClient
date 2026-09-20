using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.Assets.Editor
{
    /// <summary>
    /// 1) Build AssetBundles from Assets/Res into a staging dir, then archive to ../Bundles/{version}/
    /// 2) Copy bundle files into Assets/StreamingAssets/Bundles for runtime.
    /// 版本号按内容递增：bundle 哈希与上次构建一致时保持原版本（端上缓存继续有效），
    /// 内容变化才 bump patch（微信按 URL 缓存、Android 按 version.txt 判过期，都靠版本号失效）。
    /// </summary>
    public static class ResAssetBundleBuilder
    {
        public const string ResRoot = ResPaths.AssetRoot;
        public const string StreamingBundlesRelative = "StreamingAssets/Bundles";

        private const string StagingFolderName = "_staging";
        private const string ContentHashFileName = "buildhash.txt";

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
            BuildInternal();
        }

        /// <summary>
        /// 重建 AssetBundles。版本号同样按内容决定：内容没变保持原版本，变了才递增。
        /// 微信按 URL 缓存 bundle、Android 按 version.txt 判断本地解压产物是否过期，
        /// 内容变了版本号不变会让端上继续用旧 bundle（"新代码配旧预制体"）。
        /// </summary>
        public static void RebuildCurrentVersion()
        {
            BuildInternal();
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

        private static void BuildInternal()
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

            var staging = Path.Combine(BundleVersionManager.GetBundlesRoot(), StagingFolderName);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);

            var manifest = BuildPipeline.BuildAssetBundles(
                staging,
                builds,
                BuildAssetBundleOptions.None,
                EditorUserBuildSettings.activeBuildTarget);

            if (manifest == null)
            {
                Debug.LogError("AssetBundle build failed.");
                return;
            }

            // 内容没变 → 保持版本号（端上缓存继续有效，不用重新下载）；
            // 内容变了 → 递增版本号（微信 URL 缓存 / Android 本地解压都按版本失效）。
            var contentHash = ComputeContentHash(manifest);
            var savedVersion = BundleVersionManager.ReadSavedVersion();
            string version;
            if (contentHash == ReadSavedContentHash() &&
                BundleVersionManager.IsValidSemVer(savedVersion))
            {
                version = savedVersion;
                Debug.Log($"[Res] Bundle 内容未变化，版本保持 {version}。");
            }
            else
            {
                version = BundleVersionManager.BumpAndSaveNextBuildVersion();
                WriteSavedContentHash(contentHash);
            }

            // 归档 staging → Bundles/{version}；manifest bundle 文件名跟随输出目录名，改回版本名。
            var versionOutput = BundleVersionManager.GetVersionOutputPath(version);
            if (Directory.Exists(versionOutput))
            {
                Directory.Delete(versionOutput, recursive: true);
            }

            Directory.CreateDirectory(versionOutput);
            foreach (var file in Directory.GetFiles(staging))
            {
                File.Copy(file, Path.Combine(versionOutput, Path.GetFileName(file)), overwrite: true);
            }

            RenameIfExists(versionOutput, StagingFolderName, version);
            RenameIfExists(versionOutput, StagingFolderName + ".manifest", version + ".manifest");

            CopyToStreamingAssets(versionOutput, version);
            WriteStreamingVersion(version);
            WriteStreamingCatalog();

            AssetDatabase.Refresh();

            Debug.Log(
                $"AssetBundles v{version} built to {versionOutput} and copied to {GetStreamingBundlesAssetPath()} " +
                $"({EditorUserBuildSettings.activeBuildTarget}), {builds.Length} bundles");
        }

        private static void RenameIfExists(string directory, string oldName, string newName)
        {
            var oldPath = Path.Combine(directory, oldName);
            if (File.Exists(oldPath))
            {
                File.Move(oldPath, Path.Combine(directory, newName));
            }
        }

        /// <summary>全部 asset bundle 的哈希拼合（不含 manifest bundle 自身——它的文件名随版本目录变，参与会每次都变）。</summary>
        private static string ComputeContentHash(AssetBundleManifest manifest)
        {
            var names = manifest.GetAllAssetBundles();
            Array.Sort(names, StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var name in names)
            {
                sb.Append(name).Append(':').Append(manifest.GetAssetBundleHash(name)).Append('\n');
            }

            return sb.ToString();
        }

        private static string GetContentHashFilePath()
        {
            return Path.Combine(BundleVersionManager.GetBundlesRoot(), ContentHashFileName);
        }

        private static string ReadSavedContentHash()
        {
            var path = GetContentHashFilePath();
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static void WriteSavedContentHash(string contentHash)
        {
            File.WriteAllText(GetContentHashFilePath(), contentHash);
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

            // 清掉历史版本子目录：StreamingAssets 会被打进包体，旧版本留着只是膨胀。
            foreach (var dir in Directory.GetDirectories(dest))
            {
                if (BundleVersionManager.IsValidSemVer(Path.GetFileName(dir)))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }

            // 清掉历史构建平铺拷贝的版本号命名文件（1.0.7 / 1.0.7.manifest / .meta 等）：
            // 运行时只读固定名 "Bundles"，这些文件既冗余又会与 WebGL 的版本子目录撞名。
            foreach (var file in Directory.GetFiles(dest))
            {
                var baseName = Path.GetFileName(file);
                if (baseName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName.Substring(0, baseName.Length - ".meta".Length);
                }

                if (baseName.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName.Substring(0, baseName.Length - ".manifest".Length);
                }

                if (BundleVersionManager.IsValidSemVer(baseName))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
            }

            foreach (var file in Directory.GetFiles(versionOutput))
            {
                var name = Path.GetFileName(file);
                // 版本号命名的 manifest bundle 不再平铺拷贝：运行时只读 "Bundles"，
                // 且该文件会与 WebGL 的同名版本子目录冲突。
                if (string.IsNullOrEmpty(name) ||
                    name == version ||
                    name == version + ".manifest")
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

            // WebGL/微信端按 URL 路径里的版本号破缓存（query 参数会被 SDK 剥掉，无效）：
            // 运行时用 {root}/Bundles/{version}/{name} 下载，这里生成对应版本子目录。
            // 仅 WebGL 目标需要；Android 从包内平铺目录读，多一份子目录只会白占 APK 体积。
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                var versionDir = Path.Combine(dest, version);
                Directory.CreateDirectory(versionDir);
                foreach (var file in Directory.GetFiles(versionOutput))
                {
                    var name = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(name) ||
                        name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase) ||
                        name == version)
                    {
                        continue;
                    }

                    File.Copy(file, Path.Combine(versionDir, name), overwrite: true);
                }

                if (File.Exists(manifestBundle))
                {
                    File.Copy(manifestBundle, Path.Combine(versionDir, "Bundles"), overwrite: true);
                }
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
