using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 将 GameHud.bg 等比缩放到铺满正交相机可视范围（cover：取宽高缩放的较大值）。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class HudBackgroundFit : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private float _lastOrthoSize;
        private int _lastWidth;
        private int _lastHeight;

        private void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            Apply(true);
        }

        private void LateUpdate()
        {
            Apply(false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            Apply(true);
        }
#endif

        private void Apply(bool force)
        {
            var camera = Camera.main;
            if (camera == null || !camera.orthographic)
            {
                return;
            }

            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            if (_renderer == null || _renderer.sprite == null)
            {
                return;
            }

            Vector2 spriteSize = _renderer.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            {
                return;
            }

            if (!force &&
                Mathf.Approximately(camera.orthographicSize, _lastOrthoSize) &&
                Screen.width == _lastWidth &&
                Screen.height == _lastHeight)
            {
                return;
            }

            _lastOrthoSize = camera.orthographicSize;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;

            float viewHeight = camera.orthographicSize * 2f;
            float viewWidth = viewHeight * camera.aspect;
            float uniform = Mathf.Max(viewWidth / spriteSize.x, viewHeight / spriteSize.y);
            var scale = transform.localScale;
            scale.x = uniform;
            scale.y = uniform;
            transform.localScale = scale;
        }
    }
}
