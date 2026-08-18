using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 正交相机按宽度适配。设计分辨率 1080×1920 时保持水平可视范围不变，高度随屏幕比例伸缩。
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
            _camera.orthographicSize = designOrthographicSize * (designAspect / currentAspect);
        }
    }
}
