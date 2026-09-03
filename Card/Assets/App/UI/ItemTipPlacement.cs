using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// ItemTip 定位辅助：超出父节点时只平移贴边，不改大小。
    /// </summary>
    public static class ItemTipPlacement
    {
        public const float DefaultMargin = 24f;

        private static readonly Vector3[] TipCorners = new Vector3[4];

        /// <summary>
        /// 将 tip 平移回 parent 矩形内。已在范围内则不动。
        /// </summary>
        public static void ClampToParent(RectTransform tip, RectTransform parent, float margin = DefaultMargin)
        {
            if (tip == null || parent == null)
            {
                return;
            }

            tip.GetWorldCorners(TipCorners);
            var localMin = (Vector2)parent.InverseTransformPoint(TipCorners[0]);
            var localMax = localMin;
            for (var i = 1; i < 4; i++)
            {
                var p = (Vector2)parent.InverseTransformPoint(TipCorners[i]);
                localMin = Vector2.Min(localMin, p);
                localMax = Vector2.Max(localMax, p);
            }

            var rect = parent.rect;
            var minX = rect.xMin + margin;
            var maxX = rect.xMax - margin;
            var minY = rect.yMin + margin;
            var maxY = rect.yMax - margin;

            var dx = 0f;
            if (localMin.x < minX)
            {
                dx = minX - localMin.x;
            }
            else if (localMax.x > maxX)
            {
                dx = maxX - localMax.x;
            }

            var dy = 0f;
            if (localMin.y < minY)
            {
                dy = minY - localMin.y;
            }
            else if (localMax.y > maxY)
            {
                dy = maxY - localMax.y;
            }

            if (dx == 0f && dy == 0f)
            {
                return;
            }

            tip.anchoredPosition += new Vector2(dx, dy);
        }
    }
}
