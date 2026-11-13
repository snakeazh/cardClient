using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace App.Game
{
    [ExecuteAlways]
    public sealed class PlayerCardTilt : MonoBehaviour
    {
        private RectTransform _frame;
        private Animator _animator;
        private PlayerCardPerspective[] _layers;
        private float _lastTilt = float.NaN;

        private void OnEnable()
        {
            _frame = transform.Find("cardFrame") as RectTransform;
            _animator = GetComponent<Animator>();
            if (_frame == null || _animator == null)
                return;

            var graphics = GetComponentsInChildren<Graphic>(true);
            _layers = new PlayerCardPerspective[graphics.Length];
            for (var i = 0; i < graphics.Length; i++)
            {
                var layer = graphics[i].gameObject.AddComponent<PlayerCardPerspective>();
                layer.hideFlags = HideFlags.DontSave;
                layer.Initialize(_frame);
                _layers[i] = layer;
            }
            _lastTilt = float.NaN;
#if UNITY_EDITOR
            EditorApplication.update += UpdatePreview;
#endif
            UpdateProjection();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= UpdatePreview;
#endif
            if (_layers == null)
                return;

            foreach (var layer in _layers)
            {
                if (layer == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(layer);
                else
                    DestroyImmediate(layer);
            }
            _layers = null;
        }

#if UNITY_EDITOR
        private void UpdatePreview()
        {
            if (!Application.isPlaying && this != null && isActiveAndEnabled)
                UpdateProjection();
        }
#endif

        private void LateUpdate()
        {
            if (Application.isPlaying)
                UpdateProjection();
        }

        private void UpdateProjection()
        {
            if (_layers == null || _frame == null)
                return;

            var scale = _frame.localScale;
            if (float.IsNaN(scale.z) || float.IsInfinity(scale.z) || Mathf.Abs(scale.z) < 0.001f)
            {
                scale.z = 1f;
                _frame.localScale = scale;
            }

            var tilt = (!Application.isPlaying || _animator.isActiveAndEnabled)
                ? Mathf.Clamp((1f - scale.z) * 100f, -80f, 80f)
                : 0f;
            if (Mathf.Approximately(_lastTilt, tilt))
                return;

            _lastTilt = tilt;
            var angle = tilt * Mathf.Deg2Rad;
            var inverseDepth = Mathf.Sin(angle) / Mathf.Max(1f, _frame.rect.height * 4.5f);
            var heightScale = Mathf.Cos(angle);
            foreach (var layer in _layers)
                if (layer != null)
                    layer.SetProjection(inverseDepth, heightScale);
        }
    }
}
