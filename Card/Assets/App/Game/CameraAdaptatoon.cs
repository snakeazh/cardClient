using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 正交相机按 9:16（1080×1920）适配。窄屏按宽度适配（上下多出可视区域）；
    /// 宽于 9:16 时锁定设计高度，左右由 UIRoot 黑边遮挡，可见区域与设计分辨率一致。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class CameraAdaptatoon : MonoBehaviour
    {
        [SerializeField] private float designWidth = 1080f;
        [SerializeField] private float designHeight = 1920f;
        [SerializeField] private float designOrthographicSize = 9.6f;

        private Camera _camera;
        private int _lastWidth;
        private int _lastHeight;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Apply();
        }

        private void LateUpdate()
        {
            if (Screen.width != _lastWidth || Screen.height != _lastHeight)
            {
                Apply();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            Apply();
        }
#endif

        private void Apply()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            int width = Screen.width;
            int height = Screen.height;
            if (!_camera.orthographic || width <= 0 || height <= 0 || designWidth <= 0f || designHeight <= 0f)
            {
                return;
            }

            _lastWidth = width;
            _lastHeight = height;

            float designAspect = designWidth / designHeight;
            float currentAspect = (float)width / height;
            _camera.orthographicSize = designOrthographicSize * Mathf.Max(1f, designAspect / currentAspect);
        }
    }
}
