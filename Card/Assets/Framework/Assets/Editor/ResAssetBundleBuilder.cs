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

        private static void BuildInternal(bool bumpVersion)
        {
            if (!AssetDatabase.IsValidFolder(ResRoot))
            {
                Debug.LogError($"Res folder not found: {ResRoot}");
                return;
            }

            AssignBundleNames();

            var version = bumpVersion
                ? BundleVersionManager.BumpAndSaveNextBuildVersion()
                : BundleVersionManager.GetOrInitializeVersion();
            var versionOutput = BundleVersionManager.GetVersionOutputPath(version);
            Directory.CreateDirectory(versionOutput);

            var manifest = BuildPipeline.BuildAssetBundles(
                versionOutput,
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
                $"({EditorUserBuildSettings.activeBuildTarget})");
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

        private static void AssignBundleNames()
        {
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { ResRoot });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || Directory.Exists(path))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    continue;
                }

                var relative = path.Substring(ResRoot.Length + 1).Replace('\\', '/');
                var slash = relative.IndexOf('/');
                var bundleName = slash > 0
                    ? relative.Substring(0, slash).ToLowerInvariant()
                    : Path.GetFileNameWithoutExtension(relative).ToLowerInvariant();

                importer.assetBundleName = bundleName;
            }

            AssetDatabase.RemoveUnusedAssetBundleNames();
        }
    }
}
