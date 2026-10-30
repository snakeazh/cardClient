using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Effects
{
    /// <summary>主页 UGUI 兔子：沿相邻 Waypoint 随机巡逻，到指定点吃草。</summary>
    [DisallowMultipleComponent]
    public sealed class UIRabbitPatrol : MonoBehaviour
    {
        [SerializeField] private RectTransform rabbit;
        [SerializeField] private Image visual;
        [SerializeField] private UIRabbitWaypoint startPoint;
        [SerializeField] private Sprite[] moveFrames;
        [Tooltip("现有吃草素材为 2×2 序列图，运行时拆成四帧。")]
        [SerializeField] private Texture2D eatSheet;
        [SerializeField, Min(1f)] private float moveSpeed = 160f;
        [SerializeField, Min(1f)] private float framesPerSecond = 7.5f;
        [SerializeField] private Vector2 waitSeconds = new Vector2(1.5f, 3f);
        [SerializeField] private Vector2 eatSeconds = new Vector2(3f, 5f);

        private Sprite[] _eatFrames;
        private Coroutine _patrol;
        private readonly List<UIRabbitWaypoint> _choices = new List<UIRabbitWaypoint>();
        private float _frameTime;
        private bool _moving;
        private bool _eating;

        private void OnEnable()
        {
            if (rabbit == null || visual == null || startPoint == null) return;
            visual.raycastTarget = false;
            CreateEatFrames();
            _patrol = StartCoroutine(Patrol());
        }

        private void OnDisable()
        {
            if (_patrol != null) StopCoroutine(_patrol);
            _patrol = null;
            _moving = _eating = false;
        }

        private void OnDestroy()
        {
            if (_eatFrames == null) return;
            foreach (var sprite in _eatFrames)
            {
                if (sprite != null) Destroy(sprite);
            }
        }

        private void Update()
        {
            if (visual == null) return;
            _frameTime += Time.unscaledDeltaTime;
            var frames = _eating ? _eatFrames : moveFrames;
            if (frames == null || frames.Length == 0) return;
            var index = _moving || _eating
                ? Mathf.FloorToInt(_frameTime * Mathf.Max(1f, framesPerSecond)) % frames.Length
                : 0;
            visual.sprite = frames[index];
        }

        private IEnumerator Patrol()
        {
            var current = startPoint;
            UIRabbitWaypoint previous = null;
            rabbit.localPosition = LocalPoint(current);
            while (true)
            {
                _moving = false;
                _eating = current.EatHere;
                _frameTime = 0f;
                var range = _eating ? eatSeconds : waitSeconds;
                yield return new WaitForSecondsRealtime(Random.Range(Mathf.Max(0.1f, range.x), Mathf.Max(range.x, range.y)));
                var next = ChooseNext(current, previous);
                if (next == null)
                {
                    _eating = false;
                    yield break;
                }

                _eating = false;
                _moving = true;
                _frameTime = 0f;
                var target = LocalPoint(next);
                var direction = target.x - rabbit.localPosition.x;
                var scale = visual.rectTransform.localScale;
                if (Mathf.Abs(direction) > 0.01f) scale.x = Mathf.Abs(scale.x) * (direction > 0f ? 1f : -1f);
                visual.rectTransform.localScale = scale;
                while ((rabbit.localPosition - target).sqrMagnitude > 0.01f)
                {
                    rabbit.localPosition = Vector3.MoveTowards(rabbit.localPosition, target,
                        Mathf.Max(1f, moveSpeed) * Time.unscaledDeltaTime);
                    yield return null;
                    target = LocalPoint(next);
                }
                rabbit.localPosition = target;
                previous = current;
                current = next;
            }
        }

        private Vector3 LocalPoint(UIRabbitWaypoint point)
        {
            return rabbit.parent.InverseTransformPoint(point.transform.position);
        }

        private UIRabbitWaypoint ChooseNext(UIRabbitWaypoint current, UIRabbitWaypoint previous)
        {
            _choices.Clear();
            var neighbours = current.Neighbours;
            if (neighbours == null) return null;
            foreach (var point in neighbours)
            {
                if (point != null && point != current && point.isActiveAndEnabled && point != previous)
                    _choices.Add(point);
            }
            if (_choices.Count > 0) return _choices[Random.Range(0, _choices.Count)];
            return previous != null && previous.isActiveAndEnabled ? previous : null;
        }

        private void CreateEatFrames()
        {
            if (_eatFrames != null || eatSheet == null) return;
            _eatFrames = new Sprite[4];
            var width = eatSheet.width / 2;
            var height = eatSheet.height / 2;
            for (var i = 0; i < _eatFrames.Length; i++)
            {
                _eatFrames[i] = Sprite.Create(eatSheet,
                    new Rect(i % 2 * width, (1 - i / 2) * height, width, height),
                    new Vector2(0.5f, 0.5f), 100f);
                _eatFrames[i].name = "RabbitEat_" + i;
            }
        }
    }
}
