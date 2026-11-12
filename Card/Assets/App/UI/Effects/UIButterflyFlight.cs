using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Effects
{
    /// <summary>首页蝴蝶在指定矩形范围内随机飞行并循环播放翅膀帧。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(Image))]
    public sealed class UIButterflyFlight : MonoBehaviour
    {
        [System.Serializable]
        private sealed class ColorVariant
        {
            public Sprite[] frames;
        }

        [SerializeField] private ColorVariant[] colorVariants;
        [SerializeField, Min(1f)] private float framesPerSecond = 10f;
        [Tooltip("相对父节点中心的飞行范围；蝴蝶中心始终保持在此范围内。")]
        [SerializeField] private Rect flightArea = new Rect(-300f, -50f, 600f, 450f);
        [SerializeField, Min(1f)] private float flightSpeed = 120f;

        private RectTransform _rect;
        private Image _image;
        private Sprite[] _frames;
        private Vector2 _start;
        private Vector2 _control;
        private Vector2 _target;
        private float _duration;
        private float _elapsed;
        private float _frameTime;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _image = GetComponent<Image>();
            _image.raycastTarget = false;
        }

        private void OnEnable()
        {
            _frames = colorVariants != null && colorVariants.Length > 0
                ? colorVariants[Random.Range(0, colorVariants.Length)]?.frames
                : null;
            if (_frames != null && _frames.Length > 0) _image.sprite = _frames[0];
            _rect.anchoredPosition = RandomPoint();
            _frameTime = 0f;
            _target = _rect.anchoredPosition;
            _control = _target;
            ChooseDestination();
        }

        private void Update()
        {
            var delta = Time.unscaledDeltaTime;
            if (_frames != null && _frames.Length > 0)
            {
                _frameTime += delta;
                _image.sprite = _frames[Mathf.FloorToInt(_frameTime * Mathf.Max(1f, framesPerSecond)) % _frames.Length];
            }

            _elapsed += delta;
            var t = Mathf.Clamp01(_elapsed / _duration);
            var a = Vector2.Lerp(_start, _control, t);
            var b = Vector2.Lerp(_control, _target, t);
            _rect.anchoredPosition = Vector2.Lerp(a, b, t);
            if (t >= 1f) ChooseDestination();
        }

        private void ChooseDestination()
        {
            // 沿上一段曲线的切线继续飞行，避免到达目标点时突然折返。
            var direction = (_target - _control).normalized;
            _start = _rect.anchoredPosition;
            _target = RandomPoint();
            if (direction.sqrMagnitude < 0.0001f)
                direction = (_target - _start).normalized;

            var handleLength = Vector2.Distance(_start, _target) * 0.4f;
            if (direction.x > 0f) handleLength = Mathf.Min(handleLength, (flightArea.xMax - _start.x) / direction.x);
            if (direction.x < 0f) handleLength = Mathf.Min(handleLength, (flightArea.xMin - _start.x) / direction.x);
            if (direction.y > 0f) handleLength = Mathf.Min(handleLength, (flightArea.yMax - _start.y) / direction.y);
            if (direction.y < 0f) handleLength = Mathf.Min(handleLength, (flightArea.yMin - _start.y) / direction.y);
            _control = _start + direction * Mathf.Max(0f, handleLength);
            var distance = Vector2.Distance(_start, _control) + Vector2.Distance(_control, _target);
            _duration = Mathf.Max(0.25f, distance / Mathf.Max(1f, flightSpeed));
            _elapsed = 0f;
        }

        private Vector2 RandomPoint()
        {
            // 目标点留在边界内侧，为转弯留出空间。
            var margin = flightArea.size * 0.15f;
            return new Vector2(Random.Range(flightArea.xMin + margin.x, flightArea.xMax - margin.x),
                Random.Range(flightArea.yMin + margin.y, flightArea.yMax - margin.y));
        }

        private void OnDrawGizmos()
        {
            var rect = transform as RectTransform;
            var parent = transform.parent as RectTransform;
            if (rect == null || parent == null) return;

            // anchoredPosition 相对于锚点参考位置，而不是蝴蝶当前的位置。
            var anchor = new Vector2(
                Mathf.Lerp(rect.anchorMin.x, rect.anchorMax.x, rect.pivot.x),
                Mathf.Lerp(rect.anchorMin.y, rect.anchorMax.y, rect.pivot.y));
            var origin = parent.rect.min + Vector2.Scale(parent.rect.size, anchor);
            var previousColor = Gizmos.color;
            var previousMatrix = Gizmos.matrix;
            Gizmos.color = Color.yellow;
            Gizmos.matrix = parent.localToWorldMatrix;
            Gizmos.DrawWireCube(origin + flightArea.center, flightArea.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
