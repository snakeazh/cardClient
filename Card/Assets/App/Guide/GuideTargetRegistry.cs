using System;
using System.Collections.Generic;
using UnityEngine;

namespace App.Guide
{
    /// <summary>把 TargetId 登记为 UI 或世界物体，供 Overlay 计算打洞矩形。</summary>
    public sealed class GuideTargetRegistry
    {
        private readonly Dictionary<string, Target> _targets =
            new Dictionary<string, Target>(StringComparer.Ordinal);

        // 引导期间 Overlay 每帧取洞矩形:角点缓冲静态复用避免每帧分配。
        // 单线程顺序消费、即取即用;Group 目标逐个顺序调用同一缓冲亦安全。
        private static readonly Vector3[] UiCorners = new Vector3[4];
        private static readonly Vector3[] WorldCorners = new Vector3[8];

        public event Action Changed;

        public void RegisterUi(string id, RectTransform rect)
        {
            if (string.IsNullOrEmpty(id) || rect == null)
            {
                return;
            }

            _targets[id] = Target.FromUi(rect);
            Changed?.Invoke();
        }

        /// <summary>多个 UI 矩形取并集挖孔（如货架卡面 + 下方价格）。</summary>
        public void RegisterUiGroup(string id, IReadOnlyList<RectTransform> rects)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (rects == null || rects.Count == 0)
            {
                Unregister(id);
                return;
            }

            var copy = new RectTransform[rects.Count];
            var n = 0;
            for (var i = 0; i < rects.Count; i++)
            {
                if (rects[i] != null)
                {
                    copy[n++] = rects[i];
                }
            }

            if (n == 0)
            {
                Unregister(id);
                return;
            }

            if (n != copy.Length)
            {
                System.Array.Resize(ref copy, n);
            }

            _targets[id] = Target.FromUiGroup(copy);
            Changed?.Invoke();
        }

        public void RegisterWorld(string id, Transform world)
        {
            if (string.IsNullOrEmpty(id) || world == null)
            {
                return;
            }

            _targets[id] = Target.FromWorld(world);
            Changed?.Invoke();
        }

        public void RegisterWorldGroup(string id, IReadOnlyList<Transform> worlds)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (worlds == null || worlds.Count == 0)
            {
                Unregister(id);
                return;
            }

            var copy = new Transform[worlds.Count];
            for (var i = 0; i < worlds.Count; i++)
            {
                copy[i] = worlds[i];
            }

            _targets[id] = Target.FromWorldGroup(copy);
            Changed?.Invoke();
        }

        public void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (_targets.Remove(id))
            {
                Changed?.Invoke();
            }
        }

        public void UnregisterAll(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            var removed = false;
            foreach (var id in ids)
            {
                if (!string.IsNullOrEmpty(id) && _targets.Remove(id))
                {
                    removed = true;
                }
            }

            if (removed)
            {
                Changed?.Invoke();
            }
        }

        public RectTransform GetUi(string id)
        {
            if (string.IsNullOrEmpty(id) || !_targets.TryGetValue(id, out var target))
            {
                return null;
            }

            if (target.Ui != null)
            {
                return target.Ui;
            }

            // UI 组取首个，供 Click 绑定；挖孔仍走并集。
            if (target.UiGroup != null && target.UiGroup.Length > 0)
            {
                return target.UiGroup[0];
            }

            return null;
        }

        public bool HasTarget(string id)
        {
            return !string.IsNullOrEmpty(id) && _targets.ContainsKey(id);
        }

        public bool TryGetOverlayHole(string id, RectTransform overlay, out Rect localRect)
        {
            return TryGetOverlayHole(id, overlay, out localRect, out _);
        }

        /// <summary>
        /// 取挖孔矩形。isUi=true 为 UGUI；false 为场景 2D Sprite。
        /// UI 与 Guide 同属 UIRoot 世界空间：四角 InverseTransform 到 Overlay，避免不同 Z 经屏幕往返放缩失真。
        /// Sprite 仍走主相机屏幕坐标再映到 Overlay。
        /// </summary>
        public bool TryGetOverlayHole(string id, RectTransform overlay, out Rect localRect, out bool isUi)
        {
            localRect = default;
            isUi = false;
            if (string.IsNullOrEmpty(id) || overlay == null)
            {
                return false;
            }

            if (!_targets.TryGetValue(id, out var target))
            {
                return false;
            }

            isUi = target.IsUi;
            if (isUi)
            {
                return target.TryGetUiOverlayRect(overlay, out localRect);
            }

            if (!target.TryGetScreenRect(out var screen))
            {
                return false;
            }

            return ScreenToOverlay(overlay, screen, out localRect);
        }

        private static bool ScreenToOverlay(RectTransform overlay, Rect screen, out Rect local)
        {
            local = default;
            var canvas = overlay.GetComponentInParent<Canvas>();
            var cam = EventCamera(canvas);
            var min = new Vector2(screen.xMin, screen.yMin);
            var max = new Vector2(screen.xMax, screen.yMax);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, min, cam, out var localMin))
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, max, cam, out var localMax))
            {
                return false;
            }

            var xMin = Mathf.Min(localMin.x, localMax.x);
            var xMax = Mathf.Max(localMin.x, localMax.x);
            var yMin = Mathf.Min(localMin.y, localMax.y);
            var yMax = Mathf.Max(localMin.y, localMax.y);
            local = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return local.width > 1f && local.height > 1f;
        }

        private static Camera EventCamera(Canvas canvas)
        {
            if (canvas == null)
            {
                return Camera.main;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            if (canvas.worldCamera != null)
            {
                return canvas.worldCamera;
            }

            var root = canvas.rootCanvas;
            return root != null && root.worldCamera != null ? root.worldCamera : Camera.main;
        }

        private readonly struct Target
        {
            public readonly RectTransform Ui;
            public readonly RectTransform[] UiGroup;
            public readonly Transform World;
            public readonly Transform[] Group;

            private Target(RectTransform ui, RectTransform[] uiGroup, Transform world, Transform[] group)
            {
                Ui = ui;
                UiGroup = uiGroup;
                World = world;
                Group = group;
            }

            public bool IsUi => Ui != null || (UiGroup != null && UiGroup.Length > 0);

            public static Target FromUi(RectTransform rect) => new Target(rect, null, null, null);

            public static Target FromUiGroup(RectTransform[] group) => new Target(null, group, null, null);

            public static Target FromWorld(Transform t) => new Target(null, null, t, null);

            public static Target FromWorldGroup(Transform[] group) => new Target(null, null, null, group);

            public bool TryGetUiOverlayRect(RectTransform overlay, out Rect local)
            {
                local = default;
                if (overlay == null)
                {
                    return false;
                }

                if (Ui != null)
                {
                    return WorldCornersToOverlay(Ui, overlay, out local);
                }

                if (UiGroup == null || UiGroup.Length == 0)
                {
                    return false;
                }

                var has = false;
                var xMin = 0f;
                var xMax = 0f;
                var yMin = 0f;
                var yMax = 0f;
                for (var i = 0; i < UiGroup.Length; i++)
                {
                    if (!WorldCornersToOverlay(UiGroup[i], overlay, out var part))
                    {
                        continue;
                    }

                    if (!has)
                    {
                        xMin = part.xMin;
                        xMax = part.xMax;
                        yMin = part.yMin;
                        yMax = part.yMax;
                        has = true;
                    }
                    else
                    {
                        xMin = Mathf.Min(xMin, part.xMin);
                        xMax = Mathf.Max(xMax, part.xMax);
                        yMin = Mathf.Min(yMin, part.yMin);
                        yMax = Mathf.Max(yMax, part.yMax);
                    }
                }

                if (!has)
                {
                    return false;
                }

                local = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                return local.width > 1f && local.height > 1f;
            }

            public bool TryGetScreenRect(out Rect screen)
            {
                screen = default;
                if (Ui != null)
                {
                    // 仅兼容旧路径；挖孔请走 TryGetUiOverlayRect。
                    return WorldCornersToScreenViaEventCamera(Ui, out screen);
                }

                if (World != null)
                {
                    return TrySpriteScreenRect(World, out screen);
                }

                if (Group == null || Group.Length == 0)
                {
                    return false;
                }

                var has = false;
                var xMin = 0f;
                var xMax = 0f;
                var yMin = 0f;
                var yMax = 0f;
                for (var i = 0; i < Group.Length; i++)
                {
                    if (!TrySpriteScreenRect(Group[i], out var part))
                    {
                        continue;
                    }

                    if (!has)
                    {
                        xMin = part.xMin;
                        xMax = part.xMax;
                        yMin = part.yMin;
                        yMax = part.yMax;
                        has = true;
                    }
                    else
                    {
                        xMin = Mathf.Min(xMin, part.xMin);
                        xMax = Mathf.Max(xMax, part.xMax);
                        yMin = Mathf.Min(yMin, part.yMin);
                        yMax = Mathf.Max(yMax, part.yMax);
                    }
                }

                if (!has)
                {
                    return false;
                }

                screen = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                return true;
            }

            /// <summary>
            /// UGUI → Overlay：同 UIRoot 世界空间直接 InverseTransform，不受 Guide/目标层 Z 差影响。
            /// </summary>
            private static bool WorldCornersToOverlay(RectTransform target, RectTransform overlay, out Rect local)
            {
                local = default;
                if (target == null || overlay == null)
                {
                    return false;
                }

                var corners = UiCorners;
                target.GetWorldCorners(corners);
                var xMin = 0f;
                var xMax = 0f;
                var yMin = 0f;
                var yMax = 0f;
                for (var i = 0; i < 4; i++)
                {
                    var p = overlay.InverseTransformPoint(corners[i]);
                    if (i == 0)
                    {
                        xMin = xMax = p.x;
                        yMin = yMax = p.y;
                    }
                    else
                    {
                        xMin = Mathf.Min(xMin, p.x);
                        xMax = Mathf.Max(xMax, p.x);
                        yMin = Mathf.Min(yMin, p.y);
                        yMax = Mathf.Max(yMax, p.y);
                    }
                }

                local = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                return local.width > 1f && local.height > 1f;
            }

            /// <summary>UGUI 四角 → 屏幕（仅调试/兼容；事件相机优先 rootCanvas）。</summary>
            private static bool WorldCornersToScreenViaEventCamera(RectTransform rect, out Rect screen)
            {
                screen = default;
                if (rect == null)
                {
                    return false;
                }

                var corners = UiCorners;
                rect.GetWorldCorners(corners);
                var canvas = rect.GetComponentInParent<Canvas>();
                var cam = EventCamera(canvas);
                return CornersToScreen(corners, cam, out screen);
            }

            /// <summary>
            /// 场景 2D 牌：用 SpriteRenderer.sprite 本地四角，避免 Renderer.bounds AABB /
            /// 子节点特效把挖孔撑大。
            /// </summary>
            private static bool TrySpriteScreenRect(Transform t, out Rect screen)
            {
                screen = default;
                if (t == null)
                {
                    return false;
                }

                var cam = Camera.main;
                if (cam == null)
                {
                    return false;
                }

                var corners = WorldCorners;
                var count = FillSpriteCorners(t, corners);
                var first = true;
                var xMin = 0f;
                var xMax = 0f;
                var yMin = 0f;
                var yMax = 0f;
                for (var i = 0; i < count; i++)
                {
                    var sp = cam.WorldToScreenPoint(corners[i]);
                    if (sp.z < 0f)
                    {
                        continue;
                    }

                    if (first)
                    {
                        xMin = xMax = sp.x;
                        yMin = yMax = sp.y;
                        first = false;
                    }
                    else
                    {
                        xMin = Mathf.Min(xMin, sp.x);
                        xMax = Mathf.Max(xMax, sp.x);
                        yMin = Mathf.Min(yMin, sp.y);
                        yMax = Mathf.Max(yMax, sp.y);
                    }
                }

                if (first)
                {
                    return false;
                }

                screen = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                return screen.width > 1f && screen.height > 1f;
            }

            private static int FillSpriteCorners(Transform t, Vector3[] dest)
            {
                var sr = ResolveSpriteRenderer(t);
                if (sr != null && sr.sprite != null)
                {
                    // sprite.bounds 是 SpriteRenderer 本地空间，四角再 TransformPoint，适配旋转/缩放。
                    var b = sr.sprite.bounds;
                    var tf = sr.transform;
                    dest[0] = tf.TransformPoint(new Vector3(b.min.x, b.min.y, b.center.z));
                    dest[1] = tf.TransformPoint(new Vector3(b.min.x, b.max.y, b.center.z));
                    dest[2] = tf.TransformPoint(new Vector3(b.max.x, b.min.y, b.center.z));
                    dest[3] = tf.TransformPoint(new Vector3(b.max.x, b.max.y, b.center.z));
                    return 4;
                }

                var p = t.position;
                const float fallback = 0.4f;
                dest[0] = p + new Vector3(-fallback, -fallback, 0f);
                dest[1] = p + new Vector3(fallback, -fallback, 0f);
                dest[2] = p + new Vector3(-fallback, fallback, 0f);
                dest[3] = p + new Vector3(fallback, fallback, 0f);
                return 4;
            }

            private static SpriteRenderer ResolveSpriteRenderer(Transform t)
            {
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr != null && sr.enabled && sr.sprite != null)
                {
                    return sr;
                }

                // CardItem 根节点：优先当前牌面，避开特效上的 Renderer。
                var card = t.GetComponent<App.Game.CardItem>();
                if (card != null && card.CurrentRenderer != null && card.CurrentRenderer.sprite != null)
                {
                    return card.CurrentRenderer;
                }

                var children = t.GetComponentsInChildren<SpriteRenderer>(false);
                SpriteRenderer best = null;
                var bestArea = -1f;
                for (var i = 0; i < children.Length; i++)
                {
                    var c = children[i];
                    if (c == null || !c.enabled || c.sprite == null || !c.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    // 忽略明显是特效的超大/粒子挂件：取牌面尺寸量级的主 Sprite。
                    var size = c.sprite.bounds.size;
                    var area = size.x * size.y;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = c;
                    }
                }

                return best != null ? best : sr;
            }

            private static bool CornersToScreen(Vector3[] corners, Camera cam, out Rect screen)
            {
                screen = default;
                var first = true;
                var xMin = 0f;
                var xMax = 0f;
                var yMin = 0f;
                var yMax = 0f;
                for (var i = 0; i < corners.Length; i++)
                {
                    // Overlay 模式 cam==null 时必须传 null，不能用 Camera.main（会把 UI 当场景物体投影放大）。
                    var sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);

                    if (first)
                    {
                        xMin = xMax = sp.x;
                        yMin = yMax = sp.y;
                        first = false;
                    }
                    else
                    {
                        xMin = Mathf.Min(xMin, sp.x);
                        xMax = Mathf.Max(xMax, sp.x);
                        yMin = Mathf.Min(yMin, sp.y);
                        yMax = Mathf.Max(yMax, sp.y);
                    }
                }

                screen = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                return !first && screen.width > 1f && screen.height > 1f;
            }
        }
    }
}
