using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace App.Build.Editor
{
    /// <summary>
    /// Android 打包设置：Keystore 手写 + 是否在打包时更新版本号。
    /// 密码只存本机 EditorPrefs，不进仓库。
    /// </summary>
    public sealed class AndroidKeystoreSettingsWindow : EditorWindow
    {
        private const string PrefsUseCustom = "App.Build.Android.UseCustomKeystore";
        private const string PrefsPath = "App.Build.Android.KeystorePath";
        private const string PrefsAlias = "App.Build.Android.KeyAlias";
        private const string PrefsStorePass = "App.Build.Android.KeystorePass";
        private const string PrefsKeyPass = "App.Build.Android.KeyPass";
        private const string PrefsBumpVersionName = "App.Build.Android.BumpVersionName";
        private const string PrefsBumpVersionCode = "App.Build.Android.BumpVersionCode";

        private static readonly Regex SemVerPattern = new Regex(
            @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$",
            RegexOptions.Compiled);

        private bool _useCustomKeystore = true;
        private string _keystorePath = "user.keystore";
        private string _keyAlias = "man";
        private string _keystorePass = string.Empty;
        private string _keyPass = string.Empty;

        private const string PrefsRebuildBundles = "App.Build.Android.RebuildBundles";

        private bool _bumpVersionName;
        private bool _bumpVersionCode = true;
        private string _versionName = "1.0.0";
        private int _versionCode = 1;
        private bool _rebuildBundles;
        private Vector2 _scroll;

        [MenuItem("Build/Android/Build Settings", false, 50)]
        public static void Open()
        {
            var window = GetWindow<AndroidKeystoreSettingsWindow>("Android Build");
            window.minSize = new Vector2(460, 560);
            window.Show();
        }

        private void OnEnable()
        {
            LoadFromPrefs();
            RefreshVersionFieldsFromPlayerSettings();
        }

        private void OnFocus()
        {
            RefreshVersionFieldsFromPlayerSettings();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawBuildSection();
            EditorGUILayout.Space(10);
            DrawVersionSection();
            EditorGUILayout.Space(10);
            DrawKeystoreSection();
            EditorGUILayout.Space(12);
            DrawActions();
            EditorGUILayout.Space(8);
            DrawResolvedPathHint();

            EditorGUILayout.EndScrollView();
        }

        private void DrawBuildSection()
        {
            EditorGUILayout.LabelField("打包", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "先保存下方版本 / Keystore 设置，再点击打包。产物输出到 Card/Builds/Android/。",
                MessageType.Info);

            _rebuildBundles = EditorGUILayout.ToggleLeft(
                "打包前重建 AssetBundles（推荐真机包）",
                _rebuildBundles);

            var canBuild = !EditorApplication.isCompiling &&
                           !EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUI.DisabledScope(!canBuild))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Build APK", GUILayout.Height(36)))
                {
                    StartBuild(appBundle: false);
                }

                if (GUILayout.Button("Build AAB", GUILayout.Height(36)))
                {
                    StartBuild(appBundle: true);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开输出目录"))
            {
                AndroidBuildMenu.OpenOutputFolder();
            }

            if (GUILayout.Button("仅重建 AssetBundles"))
            {
                SaveAllBeforeBuild();
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Debug.LogError("[AndroidBuild] 切换 Android 目标失败。");
                }
                else
                {
                    Framework.Assets.Editor.ResAssetBundleBuilder.RebuildCurrentVersion();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!canBuild)
            {
                EditorGUILayout.HelpBox("编译中或 Play 模式下无法打包。", MessageType.Warning);
            }
        }

        private void StartBuild(bool appBundle)
        {
            SaveAllBeforeBuild();
            AndroidBuildMenu.BuildAndroid(appBundle, _rebuildBundles);
            RefreshVersionFieldsFromPlayerSettings();
        }

        private void SaveAllBeforeBuild()
        {
            SaveToPrefs();
            ApplyVersionToPlayerSettings(_versionName, _versionCode);
            ApplySavedSettings(requirePasswords: false);
        }

        private void DrawVersionSection()
        {
            EditorGUILayout.LabelField("版本号", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "勾选后，每次点击 Build APK/AAB 时自动递增。\n" +
                "Version Name = PlayerSettings.bundleVersion（如 1.0.0）\n" +
                "Version Code = AndroidBundleVersionCode（上架必须递增）",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _versionName = EditorGUILayout.TextField("Version Name", _versionName);
            _versionCode = EditorGUILayout.IntField("Version Code", _versionCode);
            if (EditorGUI.EndChangeCheck())
            {
                _versionCode = Mathf.Max(1, _versionCode);
            }

            _bumpVersionName = EditorGUILayout.ToggleLeft(
                "打包时更新 Version Name（patch +1，如 1.0.0 → 1.0.1）",
                _bumpVersionName);
            _bumpVersionCode = EditorGUILayout.ToggleLeft(
                "打包时更新 Version Code（+1）",
                _bumpVersionCode);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("写入当前版本到 Player Settings"))
            {
                ApplyVersionToPlayerSettings(_versionName, _versionCode);
                Debug.Log($"[AndroidBuild] 版本已写入: {_versionName} ({_versionCode})");
            }

            if (GUILayout.Button("从 Player Settings 刷新"))
            {
                RefreshVersionFieldsFromPlayerSettings();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawKeystoreSection()
        {
            EditorGUILayout.LabelField("自定义 Keystore（手写）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "路径相对 Unity 工程根目录（Card/）。密码仅保存在本机 EditorPrefs，不会提交到 Git。",
                MessageType.Info);

            _useCustomKeystore = EditorGUILayout.Toggle("使用自定义 Keystore", _useCustomKeystore);

            using (new EditorGUI.DisabledScope(!_useCustomKeystore))
            {
                EditorGUILayout.BeginHorizontal();
                _keystorePath = EditorGUILayout.TextField("Keystore 路径", _keystorePath);
                if (GUILayout.Button("浏览…", GUILayout.Width(60)))
                {
                    BrowseKeystore();
                }

                EditorGUILayout.EndHorizontal();

                _keyAlias = EditorGUILayout.TextField("Key Alias", _keyAlias);
                _keystorePass = EditorGUILayout.PasswordField("Keystore Password", _keystorePass);
                _keyPass = EditorGUILayout.PasswordField("Key Password", _keyPass);
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存全部设置", GUILayout.Height(28)))
            {
                SaveToPrefs();
                ApplyVersionToPlayerSettings(_versionName, _versionCode);
                ApplySavedSettings(requirePasswords: false);
                Debug.Log("[AndroidBuild] 打包设置已保存。");
            }

            if (GUILayout.Button("仅应用 Keystore", GUILayout.Height(28)))
            {
                SaveToPrefs();
                ApplySavedSettings(requirePasswords: false);
                Debug.Log("[AndroidBuild] Keystore 已应用到 Player Settings。");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void BrowseKeystore()
        {
            var projectRoot = GetProjectRoot();
            var startDir = Directory.Exists(projectRoot) ? projectRoot : Application.dataPath;
            var picked = EditorUtility.OpenFilePanel("选择 Keystore", startDir, "keystore");
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            _keystorePath = MakeProjectRelative(picked);
            Repaint();
        }

        private void DrawResolvedPathHint()
        {
            if (!_useCustomKeystore)
            {
                return;
            }

            var full = ResolveKeystoreFullPath(_keystorePath);
            var exists = File.Exists(full);
            EditorGUILayout.HelpBox(
                exists
                    ? $"Keystore 文件存在: {full}"
                    : $"Keystore 文件不存在: {full}",
                exists ? MessageType.None : MessageType.Warning);
        }

        private void RefreshVersionFieldsFromPlayerSettings()
        {
            _versionName = PlayerSettings.bundleVersion;
            _versionCode = PlayerSettings.Android.bundleVersionCode;
            Repaint();
        }

        private void LoadFromPrefs()
        {
            _useCustomKeystore = EditorPrefs.GetBool(PrefsUseCustom, true);
            _keystorePath = EditorPrefs.GetString(PrefsPath, "user.keystore");
            _keyAlias = EditorPrefs.GetString(PrefsAlias, "man");
            _keystorePass = EditorPrefs.GetString(PrefsStorePass, string.Empty);
            _keyPass = EditorPrefs.GetString(PrefsKeyPass, string.Empty);
            _bumpVersionName = EditorPrefs.GetBool(PrefsBumpVersionName, false);
            _bumpVersionCode = EditorPrefs.GetBool(PrefsBumpVersionCode, true);
            _rebuildBundles = EditorPrefs.GetBool(PrefsRebuildBundles, false);
        }

        private void SaveToPrefs()
        {
            EditorPrefs.SetBool(PrefsUseCustom, _useCustomKeystore);
            EditorPrefs.SetString(PrefsPath, _keystorePath ?? string.Empty);
            EditorPrefs.SetString(PrefsAlias, _keyAlias ?? string.Empty);
            EditorPrefs.SetString(PrefsStorePass, _keystorePass ?? string.Empty);
            EditorPrefs.SetString(PrefsKeyPass, _keyPass ?? string.Empty);
            EditorPrefs.SetBool(PrefsBumpVersionName, _bumpVersionName);
            EditorPrefs.SetBool(PrefsBumpVersionCode, _bumpVersionCode);
            EditorPrefs.SetBool(PrefsRebuildBundles, _rebuildBundles);
        }

        /// <summary>
        /// 打包前：按设置决定是否递增版本号。
        /// </summary>
        public static void ApplyVersionBumpIfNeeded()
        {
            var bumpName = EditorPrefs.GetBool(PrefsBumpVersionName, false);
            var bumpCode = EditorPrefs.GetBool(PrefsBumpVersionCode, true);

            var oldName = PlayerSettings.bundleVersion;
            var oldCode = PlayerSettings.Android.bundleVersionCode;
            var newName = oldName;
            var newCode = oldCode;

            if (bumpName)
            {
                newName = BumpPatchVersion(oldName);
                PlayerSettings.bundleVersion = newName;
            }

            if (bumpCode)
            {
                newCode = oldCode + 1;
                PlayerSettings.Android.bundleVersionCode = newCode;
            }

            if (bumpName || bumpCode)
            {
                Debug.Log(
                    $"[AndroidBuild] 版本已更新: {oldName} ({oldCode}) → {newName} ({newCode})");
            }
            else
            {
                Debug.Log($"[AndroidBuild] 保持版本不变: {oldName} ({oldCode})");
            }
        }

        /// <summary>
        /// 打包前调用：把本机保存的签名信息写进 PlayerSettings。
        /// </summary>
        public static bool ApplySavedSettings(bool requirePasswords)
        {
            var useCustom = EditorPrefs.GetBool(PrefsUseCustom, true);
            PlayerSettings.Android.useCustomKeystore = useCustom;
            if (!useCustom)
            {
                return true;
            }

            var relativePath = EditorPrefs.GetString(PrefsPath, "user.keystore");
            var alias = EditorPrefs.GetString(PrefsAlias, "man");
            var storePass = EditorPrefs.GetString(PrefsStorePass, string.Empty);
            var keyPass = EditorPrefs.GetString(PrefsKeyPass, string.Empty);

            var fullPath = ResolveKeystoreFullPath(relativePath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError(
                    $"[AndroidBuild] Keystore 不存在: {fullPath}\n" +
                    "请打开 Build/Android/Build Settings 手写路径。");
                return false;
            }

            if (requirePasswords &&
                (string.IsNullOrEmpty(storePass) || string.IsNullOrEmpty(keyPass)))
            {
                Debug.LogError(
                    "[AndroidBuild] Keystore / Key 密码未填写。\n" +
                    "请打开 Build/Android/Build Settings 手写密码。");
                Open();
                return false;
            }

            PlayerSettings.Android.keystoreName = fullPath;
            PlayerSettings.Android.keyaliasName = alias ?? string.Empty;
            PlayerSettings.Android.keystorePass = storePass ?? string.Empty;
            PlayerSettings.Android.keyaliasPass = keyPass ?? string.Empty;
            return true;
        }

        private static void ApplyVersionToPlayerSettings(string versionName, int versionCode)
        {
            if (!string.IsNullOrWhiteSpace(versionName))
            {
                PlayerSettings.bundleVersion = versionName.Trim();
            }

            PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, versionCode);
        }

        private static string BumpPatchVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return "1.0.1";
            }

            var match = SemVerPattern.Match(version.Trim());
            if (!match.Success)
            {
                Debug.LogWarning(
                    $"[AndroidBuild] Version Name「{version}」不是 x.y.z，无法自动 patch+1，保持原值。");
                return version.Trim();
            }

            var major = int.Parse(match.Groups["major"].Value);
            var minor = int.Parse(match.Groups["minor"].Value);
            var patch = int.Parse(match.Groups["patch"].Value);
            return $"{major}.{minor}.{patch + 1}";
        }

        public static string ResolveKeystoreFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(GetProjectRoot(), path));
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        }

        private static string MakeProjectRelative(string absolutePath)
        {
            var root = GetProjectRoot();
            var full = Path.GetFullPath(absolutePath);
            var rootFull = Path.GetFullPath(root);
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                var relative = full.Substring(rootFull.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relative.Replace('\\', '/');
            }

            return full;
        }
    }
}
