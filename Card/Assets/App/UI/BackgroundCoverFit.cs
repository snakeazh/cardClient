using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 全屏背景按设计尺寸（默认 1080×1920）等比放大铺满父节点（cover），不拉伸。
    /// 高屏（比 16:9 更长，如 1080×2340/2400）时整体放大并裁掉左右两侧，标准 16:9 保持 1:1 不变。
    /// 使用前提：挂在背景 Image 所在节点，该节点底部锚定（anchorMin = anchorMax = (0.5, 0)）、pivot 在底部中点。
    /// 只放大不缩小，宽屏（比 16:9 更宽）时由 UIRoot 的高度适配 + 黑边负责，这里保持 1。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BackgroundCoverFit : MonoBehaviour
    {
        [SerializeField] private float designWidth = 1080f;
        [SerializeField] private float designHeight = 1920f;

        private RectTransform _rect;
        private Vector2 _lastSize = new Vector2(-1f, -1f);
        private float _appliedScale = 1f;

        private void OnEnable()
        {
            _rect = transform as RectTransform;
            Apply(true);
        }

        private void LateUpdate()
        {
            Apply(false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _rect = transform as RectTransform;
            if (isActiveAndEnabled)
            {
                Apply(true);
            }
        }
#endif

        private void Apply(bool force)
        {
            if (_rect == null)
            {
                _rect = transform as RectTransform;
            }

            var parent = _rect != null ? _rect.parent as RectTransform : null;
            if (parent == null || designWidth <= 0f || designHeight <= 0f)
            {
                return;
            }

            var size = parent.rect.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                return;
            }

            if (!force &&
                Mathf.Approximately(size.x, _lastSize.x) &&
                Mathf.Approximately(size.y, _lastSize.y))
            {
                return;
            }

            _lastSize = size;

            var scale = Mathf.Max(size.x / designWidth, size.y / designHeight);
            if (scale < 1f)
            {
                scale = 1f;
            }

            _appliedScale = scale;

            // 值没变就不写 Transform：编辑器里 OnValidate/OnEnable 也会走到这里，写相同值会把预制体标脏。
            var current = _rect.localScale;
            if (Mathf.Approximately(current.x, scale) && Mathf.Approximately(current.y, scale))
            {
                return;
            }

            _rect.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
