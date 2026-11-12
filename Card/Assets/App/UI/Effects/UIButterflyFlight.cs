using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Effects
{
    /// <summary>首页蝴蝶沿巡逻点循环飞行并循环播放翅膀帧。</summary>
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
        [Tooltip("首尾相连的巡逻点，坐标相对蝴蝶的锚点，可在 Scene 中拖动。")]
        [SerializeField] private Vector2[] patrolPoints;
        [InspectorName("巡逻点停留时间（秒）")]
        [Tooltip("到达巡逻点后，在 X（最短）和 Y（最长）之间随机停留；两者设为相同值可固定时间。")]
        [SerializeField] private Vector2 pauseSeconds = new Vector2(0.4f, 1.2f);
        [SerializeField, Min(0f)] private float flutterAmplitude = 8f;
        [SerializeField, Min(0f)] private float flutterFrequency = 2.2f;
        [SerializeField, Min(1f)] private float flightSpeed = 120f;

        private RectTransform _rect;
        private Image _image;
        private Sprite[] _frames;
        private int _fromIndex;
        private int _toIndex;
        private int _direction;
        private float _pauseRemaining;
        private float _flutterPhase;
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
            _frameTime = Random.value / Mathf.Max(1f, framesPerSecond);
            _flutterPhase = Random.Range(0f, Mathf.PI * 2f);
            if ((patrolPoints?.Length ?? 0) == 0) return;

            _fromIndex = Random.Range(0, (patrolPoints?.Length ?? 0));
            _rect.anchoredPosition = patrolPoints[_fromIndex];
            _direction = Random.value < 0.5f ? -1 : 1;
            _pauseRemaining = Random.Range(0f, Mathf.Max(0f, pauseSeconds.y));
            if ((patrolPoints?.Length ?? 0) > 1) ChooseDestination();
        }

        private void Update()
        {
            var delta = Time.unscaledDeltaTime;
            if (_frames != null && _frames.Length > 0)
            {
                _frameTime += delta;
                _image.sprite = _frames[Mathf.FloorToInt(_frameTime * Mathf.Max(1f, framesPerSecond)) % _frames.Length];
            }

            if ((patrolPoints?.Length ?? 0) < 2) return;
            if (_pauseRemaining > 0f)
            {
                _pauseRemaining -= delta;
                _rect.anchoredPosition = patrolPoints[_fromIndex];
                return;
            }

            _elapsed += delta;
            var t = Mathf.Clamp01(_elapsed / _duration);
            var smooth = t * t * (3f - 2f * t);
            var from = patrolPoints[_fromIndex];
            var to = patrolPoints[_toIndex];
            var flutter = Mathf.Sin(_flutterPhase + _elapsed * flutterFrequency * Mathf.PI * 2f)
                * flutterAmplitude * Mathf.Sin(Mathf.PI * t);
            _rect.anchoredPosition = Vector2.Lerp(from, to, smooth) + Vector2.up * flutter;
            if (t < 1f) return;

            _fromIndex = _toIndex;
            _rect.anchoredPosition = to;
            _pauseRemaining = Random.Range(Mathf.Max(0f, pauseSeconds.x),
                Mathf.Max(pauseSeconds.x, pauseSeconds.y));
            ChooseDestination();
        }

        private void ChooseDestination()
        {
            _toIndex = (_fromIndex + _direction + patrolPoints.Length) % patrolPoints.Length;
            var distance = Vector2.Distance(patrolPoints[_fromIndex], patrolPoints[_toIndex]);
            _duration = Mathf.Max(0.25f, distance / Mathf.Max(1f, flightSpeed));
            _elapsed = 0f;
            _flutterPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void OnDrawGizmos()
        {
            var rect = transform as RectTransform;
            var parent = transform.parent as RectTransform;
            if (patrolPoints == null || rect == null || parent == null) return;

            var anchor = new Vector2(
                Mathf.Lerp(rect.anchorMin.x, rect.anchorMax.x, rect.pivot.x),
                Mathf.Lerp(rect.anchorMin.y, rect.anchorMax.y, rect.pivot.y));
            var origin = parent.rect.min + Vector2.Scale(parent.rect.size, anchor);
            var previousColor = Gizmos.color;
            var previousMatrix = Gizmos.matrix;
            Gizmos.color = Color.yellow;
            Gizmos.matrix = parent.localToWorldMatrix;
            for (var i = 0; i < patrolPoints.Length; i++)
            {
                Gizmos.DrawWireSphere(origin + patrolPoints[i], 10f);
                if (i > 0) Gizmos.DrawLine(origin + patrolPoints[i - 1], origin + patrolPoints[i]);
            }
            if (patrolPoints.Length > 2)
                Gizmos.DrawLine(origin + patrolPoints[patrolPoints.Length - 1], origin + patrolPoints[0]);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
