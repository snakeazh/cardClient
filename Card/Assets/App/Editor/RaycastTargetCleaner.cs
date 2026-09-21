using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.Editor
{
    /// <summary>
    /// Raycast Target 批量清理：把纯展示图的 raycastTarget 关掉，减少每次点击/悬停的射线遍历
    /// （GameBoardController 用 IsPointerOverGameObject 挡牌桌点击，UI 射线目标越少越快）。
    /// 保留规则（一律不关）：
    ///   1. 同节点挂 Selectable / EventTrigger / 其它 IEventSystemHandler 组件；
    ///   2. 被本预制体内任何 Selectable 引用为 targetGraphic；
    ///   3. 位于 ScrollRect 子树内（滚动拖拽面）。
    /// 注意：GameUI 的装饰图可能承担"挡住牌桌点击"的作用，关闭后点击会穿透到世界卡牌——
    /// 先跑扫描看清单，确认无穿透风险再应用。
    /// </summary>
    public static class RaycastTargetCleaner
    {
        [MenuItem("Tools/Raycast清理/扫描选中预制体")]
        private static void Scan()
        {
            Run(apply: false);
        }

        [MenuItem("Tools/Raycast清理/应用选中预制体")]
        private static void Apply()
        {
            Run(apply: true);
        }

        private static void Run(bool apply)
        {
            var targets = Selection.gameObjects;
            if (targets.Length == 0)
            {
                Debug.LogWarning("[Raycast清理] 请先在 Project 窗口选中要处理的预制体。");
                return;
            }

            var dirty = false;
            foreach (var go in targets)
            {
                var path = AssetDatabase.GetAssetPath(go);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
                {
                    Debug.LogWarning($"[Raycast清理] 跳过非预制体: {(string.IsNullOrEmpty(path) ? go.name : path)}");
                    continue;
                }

                dirty |= Process(go, path, apply);
            }

            if (apply && dirty)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Raycast清理] 已保存资产修改。");
            }
        }

        private static bool Process(GameObject root, string path, bool apply)
        {
            var targetGraphics = new HashSet<Graphic>();
            var selectables = root.GetComponentsInChildren<Selectable>(true);
            for (var i = 0; i < selectables.Length; i++)
            {
                if (selectables[i].targetGraphic != null)
                {
                    targetGraphics.Add(selectables[i].targetGraphic);
                }
            }

            var graphics = root.GetComponentsInChildren<Graphic>(true);
            var report = new StringBuilder();
            var offCount = 0;
            var keepCount = 0;
            var changed = false;
            for (var i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null || !graphic.raycastTarget)
                {
                    continue;
                }

                if (ShouldKeep(graphic, targetGraphics, out var reason))
                {
                    keepCount++;
                    report.AppendLine($"  保留: {GetPath(graphic.transform)}  ({reason})");
                    continue;
                }

                offCount++;
                report.AppendLine($"  关闭: {GetPath(graphic.transform)}  ({graphic.GetType().Name})");
                if (apply)
                {
                    graphic.raycastTarget = false;
                    EditorUtility.SetDirty(graphic);
                    changed = true;
                }
            }

            Debug.Log(
                $"[Raycast清理] {path}\n  建议关闭 {offCount} 个 / 保留 {keepCount} 个" +
                $"{(apply ? "(已应用并保存)" : "(仅扫描,未修改)")}\n{report}");
            return changed;
        }

        private static bool ShouldKeep(Graphic graphic, HashSet<Graphic> targetGraphics, out string reason)
        {
            if (graphic.GetComponent<Selectable>() != null)
            {
                reason = "同节点 Selectable";
                return true;
            }

            if (graphic.GetComponent<EventTrigger>() != null)
            {
                reason = "同节点 EventTrigger";
                return true;
            }

            // 自定义事件处理(拖拽/点击组件挂在同节点)
            var components = graphic.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] is IEventSystemHandler && !(components[i] is Graphic))
                {
                    reason = "同节点 " + components[i].GetType().Name;
                    return true;
                }
            }

            if (targetGraphics.Contains(graphic))
            {
                reason = "Selectable.targetGraphic";
                return true;
            }

            if (graphic.GetComponentInParent<ScrollRect>(true) != null)
            {
                reason = "ScrollRect 子树(拖拽面)";
                return true;
            }

            reason = null;
            return false;
        }

        private static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }

            return sb.ToString();
        }
    }
}
