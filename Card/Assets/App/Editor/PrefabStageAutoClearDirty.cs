#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace App.Editor
{
    /// <summary>
    /// 规避 Unity 的已知问题：Prefab Mode 打开时，ContentSizeFitter / LayoutGroup 会回写被驱动的
    /// RectTransform 和 CanvasRenderer（内容其实没变，保存也不产生任何 diff），把预制体标脏，
    /// 于是每次打开都弹「是否保存」。本项目 12 个 UI 预制体存在 ContentSizeFitter + LayoutGroup 组合，
    /// 打开即脏，见 issuetracker「Prefab Mode Scene is marked dirty when no changes are made」。
    ///
    /// 这里在窗口刚打开的短暂时间内把这种"空脏"清掉；窗口之后（用户真正手动改）不会再清。
    /// 如果后续 Unity 版本修了这个问题，或团队不希望有这层兜底，删掉本文件即可。
    /// </summary>
    [InitializeOnLoad]
    public static class PrefabStageAutoClearDirty
    {
        /// <summary>只清理打开后这段时间内产生的脏，超出视为用户真实修改。</summary>
        private const double OpenWindowSeconds = 1.0;

        /// <summary>同一窗口内最多清理几次（布局可能分几帧跑完）。</summary>
        private const int MaxClears = 3;

        private static PrefabStage _stage;
        private static double _openedAt = -100;
        private static int _clearCount;

        static PrefabStageAutoClearDirty()
        {
            PrefabStage.prefabStageOpened += OnOpened;
            PrefabStage.prefabStageDirtied += OnDirtied;
            PrefabStage.prefabStageClosing += OnClosing;
        }

        private static void OnOpened(PrefabStage stage)
        {
            _stage = stage;
            _openedAt = EditorApplication.timeSinceStartup;
            _clearCount = 0;
            EditorApplication.delayCall += () => TryClear(stage);
        }

        private static void OnClosing(PrefabStage stage)
        {
            if (stage == _stage)
            {
                _stage = null;
            }
        }

        private static void OnDirtied(PrefabStage stage)
        {
            if (stage != _stage)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _openedAt > OpenWindowSeconds)
            {
                return;
            }

            EditorApplication.delayCall += () => TryClear(stage);
        }

        private static void TryClear(PrefabStage stage)
        {
            if (stage == null || stage != _stage || _clearCount >= MaxClears)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _openedAt > OpenWindowSeconds)
            {
                return;
            }

            _clearCount++;
            stage.ClearDirtiness();
        }
    }
}
#endif
