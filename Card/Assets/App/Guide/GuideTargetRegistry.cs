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

            return target.Ui != null ? target.Ui : null;
        }

        public bool TryGetOverlayHole(string id, RectTransform overlay, out Rect localRect)
        {
            localRect = default;
            if (string.IsNullOrEmpty(id) || overlay == null)
            {
                return false;
            }

            if (!_targets.TryGetValue(id, out var target))
            {
                return false;
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
            public readonly Transform World;
            public readonly Transform[] Group;

            private Target(RectTransform ui, Transform world, Transform[] group)
            {
                Ui = ui;
                World = world;
                Group = group;
            }

            public static Target FromUi(RectTransform rect) => new Target(rect, null, null);

            public static Target FromWorld(Transform t) => new Target(null, t, null);

            public static Target FromWorldGroup(Transform[] group) => new Target(null, null, group);

            public bool TryGetScreenRect(out Rect screen)
            {
                screen = default;
                if (Ui != null)
                {
                    return TryUiScreenRect(Ui, out screen);
                }

                if (World != null)
                {
                    return TryWorldScreenRect(World, out screen);
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
                    if (!TryWorldScreenRect(Group[i], out var part))
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

            private static bool TryUiScreenRect(RectTransform rect, out Rect screen)
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

            private static bool TryWorldScreenRect(Transform t, out Rect screen)
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
                var count = FillWorldCorners(t, corners);
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

            private static int FillWorldCorners(Transform t, Vector3[] dest)
            {
                var renderer = t.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    var b = renderer.bounds;
                    dest[0] = new Vector3(b.min.x, b.min.y, b.min.z);
                    dest[1] = new Vector3(b.min.x, b.min.y, b.max.z);
                    dest[2] = new Vector3(b.min.x, b.max.y, b.min.z);
                    dest[3] = new Vector3(b.min.x, b.max.y, b.max.z);
                    dest[4] = new Vector3(b.max.x, b.min.y, b.min.z);
                    dest[5] = new Vector3(b.max.x, b.min.y, b.max.z);
                    dest[6] = new Vector3(b.max.x, b.max.y, b.min.z);
                    dest[7] = new Vector3(b.max.x, b.max.y, b.max.z);
                    return 8;
                }

                var p = t.position;
                const float fallback = 0.4f;
                dest[0] = p + new Vector3(-fallback, -fallback, 0f);
                dest[1] = p + new Vector3(fallback, -fallback, 0f);
                dest[2] = p + new Vector3(-fallback, fallback, 0f);
                dest[3] = p + new Vector3(fallback, fallback, 0f);
                return 4;
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
                    Vector2 sp;
                    if (cam != null)
                    {
                        sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                    }
                    else
                    {
                        var camMain = Camera.main;
                        if (camMain == null)
                        {
                            return false;
                        }

                        var p = camMain.WorldToScreenPoint(corners[i]);
                        sp = new Vector2(p.x, p.y);
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

                screen = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                return !first && screen.width > 1f && screen.height > 1f;
            }
        }
    }
}
