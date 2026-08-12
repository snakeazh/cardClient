using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Framework.Assets.Editor
{
    /// <summary>
    /// SemVer for AssetBundle output folders. Starts at 1.0.0 and bumps patch on each build.
    /// Independent from PlayerSettings.bundleVersion.
    /// </summary>
    public static class BundleVersionManager
    {
        public const string InitialVersion = "1.0.0";
        private const string VersionFileName = "version.txt";
        private static readonly Regex SemVerPattern = new Regex(
            @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$",
            RegexOptions.Compiled);

        public static string GetBundlesRoot()
        {
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Bundles");
        }

        public static string GetVersionFilePath()
        {
            return Path.Combine(GetBundlesRoot(), VersionFileName);
        }

        public static string GetOrInitializeVersion()
        {
            var saved = ReadSavedVersion();
            if (IsValidSemVer(saved))
            {
                return saved;
            }

            WriteSavedVersion(InitialVersion);
            return InitialVersion;
        }

        /// <summary>
        /// Returns the version for the next build and persists it.
        /// First build: 1.0.0. Subsequent builds: patch + 1.
        /// </summary>
        public static string BumpAndSaveNextBuildVersion()
        {
            var bundlesRoot = GetBundlesRoot();
            Directory.CreateDirectory(bundlesRoot);

            var next = string.IsNullOrWhiteSpace(ReadSavedVersion())
                ? InitialVersion
                : BumpPatch(ReadSavedVersion());

            WriteSavedVersion(next);
            return next;
        }

        public static string ReadSavedVersion()
        {
            var path = GetVersionFilePath();
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllText(path).Trim();
        }

        public static string GetVersionOutputPath(string version)
        {
            return Path.Combine(GetBundlesRoot(), version);
        }

        public static bool TryGetLatestBuiltVersion(out string version)
        {
            version = ReadSavedVersion();
            if (!string.IsNullOrWhiteSpace(version) && IsValidSemVer(version))
            {
                return true;
            }

            version = null;
            var bundlesRoot = GetBundlesRoot();
            if (!Directory.Exists(bundlesRoot))
            {
                return false;
            }

            string best = null;
            foreach (var dir in Directory.GetDirectories(bundlesRoot))
            {
                var name = Path.GetFileName(dir);
                if (!IsValidSemVer(name))
                {
                    continue;
                }

                if (best == null || CompareSemVer(name, best) > 0)
                {
                    best = name;
                }
            }

            version = best;
            return best != null;
        }

        public static string BumpPatch(string version)
        {
            if (!TryParseSemVer(version, out var major, out var minor, out var patch))
            {
                return InitialVersion;
            }

            return $"{major}.{minor}.{patch + 1}";
        }

        public static bool IsValidSemVer(string version) => TryParseSemVer(version, out _, out _, out _);

        private static void WriteSavedVersion(string version)
        {
            File.WriteAllText(GetVersionFilePath(), version);
        }

        private static bool TryParseSemVer(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var match = SemVerPattern.Match(version.Trim());
            if (!match.Success)
            {
                return false;
            }

            major = int.Parse(match.Groups["major"].Value);
            minor = int.Parse(match.Groups["minor"].Value);
            patch = int.Parse(match.Groups["patch"].Value);
            return true;
        }

        private static int CompareSemVer(string left, string right)
        {
            TryParseSemVer(left, out var lm, out var ln, out var lp);
            TryParseSemVer(right, out var rm, out var rn, out var rp);

            if (lm != rm)
            {
                return lm.CompareTo(rm);
            }

            if (ln != rn)
            {
                return ln.CompareTo(rn);
            }

            return lp.CompareTo(rp);
        }
    }
}
