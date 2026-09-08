using Framework.UI.Navigation;
using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 正交相机按 9:16（1080×1920）适配。
    /// 局外按宽度适配（高屏扩大可视高度）；局内锁定设计高度，过宽由 <see cref="TallScreenFitScale"/> 缩小。
    /// 宽于 9:16 时始终锁高度，左右由 UIRoot 黑边遮挡。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class CameraAdaptatoon : MonoBehaviour
    {
        [SerializeField] private float designWidth = 1080f;
        [SerializeField] private float designHeight = 1920f;
        [SerializeField] private float designOrthographicSize = 9.6f;

        private Camera _camera;
        private UIRoot _uiRoot;
        private int _lastWidth;
        private int _lastHeight;
        private bool _lastInBattle;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Apply();
        }

        private void LateUpdate()
        {
            bool inBattle = IsInBattleFit();
            if (Screen.width != _lastWidth || Screen.height != _lastHeight || inBattle != _lastInBattle)
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
            bool inBattle = IsInBattleFit();
            _lastInBattle = inBattle;

            bool useHeightFit = inBattle || currentAspect > designAspect;
            _camera.orthographicSize = useHeightFit
                ? designOrthographicSize
                : designOrthographicSize * (designAspect / currentAspect);
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
