using Framework.UI.Navigation;
using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 局内且屏幕高于 16:9（比 1080×1920 更窄）时，按高度适配后内容会过宽。
    /// 挂到需要完整显示的物体上，按宽度差等比例缩小；局外/标准/宽屏保持原始缩放。
    /// 原始缩放写入序列化字段，避免重编译后把已缩小的 localScale 再乘一次。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class TallScreenFitScale : MonoBehaviour
    {
        [SerializeField] private float designWidth = 1080f;
        [SerializeField] private float designHeight = 1920f;
        [SerializeField] private Vector3 originalScale = Vector3.one;
        [SerializeField, HideInInspector] private bool originalScaleCaptured;

        private UIRoot _uiRoot;
        private float _appliedFactor = 1f;
        private int _lastWidth;
        private int _lastHeight;
        private bool _lastInBattle;

        private void Reset()
        {
            originalScale = transform.localScale;
            originalScaleCaptured = true;
        }

        private void OnEnable()
        {
            EnsureOriginalScale();
            Apply(true);
        }

        private void OnDisable()
        {
            transform.localScale = originalScale;
            _appliedFactor = 1f;
        }

        private void LateUpdate()
        {
            bool inBattle = IsInBattleFit();
            if (inBattle != _lastInBattle)
            {
                Apply(true);
                return;
            }

            Apply(false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (designWidth < 1f)
            {
                designWidth = 1f;
            }

            if (designHeight < 1f)
            {
                designHeight = 1f;
            }

            if (originalScale.x == 0f || originalScale.y == 0f || originalScale.z == 0f)
            {
                originalScale = Vector3.one;
            }

            if (isActiveAndEnabled && originalScaleCaptured)
            {
                Apply(true);
            }
        }
#endif

        [ContextMenu("Capture Original Scale")]
        private void CaptureOriginalScaleFromTransform()
        {
            float factor = ComputeFactor(Screen.width, Screen.height);
            var current = transform.localScale;
            originalScale = factor > 0.0001f ? current / factor : current;
            originalScaleCaptured = true;
            Apply(true);
        }

        private void EnsureOriginalScale()
        {
            if (originalScaleCaptured)
            {
                return;
            }

            float factor = ComputeFactor(Screen.width, Screen.height);
            Vector3 current = transform.localScale;

            // 旧逻辑会把已缩小的值再乘一次（factor 或 factor²）。升级时还原成 1。
            if (IsUniformScale(current, Vector3.one * factor) ||
                IsUniformScale(current, Vector3.one * factor * factor))
            {
                originalScale = Vector3.one;
            }
            else if (Application.isPlaying)
            {
                originalScale = current;
            }

            originalScaleCaptured = true;
        }

        private static bool IsUniformScale(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.0001f &&
                   Mathf.Abs(a.y - b.y) < 0.0001f &&
                   Mathf.Abs(a.z - b.z) < 0.0001f;
        }

        private void Apply(bool force)
        {
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0 || designWidth <= 0f || designHeight <= 0f)
            {
                return;
            }

            if (!force && width == _lastWidth && height == _lastHeight)
            {
                return;
            }

            _lastWidth = width;
            _lastHeight = height;

            float factor = ComputeFactor(width, height);
            _lastInBattle = IsInBattleFit();
            if (Mathf.Approximately(factor, _appliedFactor) && !force)
            {
                return;
            }

            _appliedFactor = factor;
            transform.localScale = originalScale * factor;
        }

        private float ComputeFactor(int width, int height)
        {
            if (width <= 0 || height <= 0 || designWidth <= 0f || designHeight <= 0f)
            {
                return 1f;
            }

            float designAspect = designWidth / designHeight;
            float currentAspect = (float)width / height;
            if (!IsInBattleFit() || currentAspect >= designAspect)
            {
                return 1f;
            }

            return currentAspect / designAspect;
        }

        private bool IsInBattleFit()
        {
            if (_uiRoot == null)
            {
                _uiRoot = FindObjectOfType<UIRoot>();
            }

            return _uiRoot != null && _uiRoot.InBattleFit;
        }
    }
}
