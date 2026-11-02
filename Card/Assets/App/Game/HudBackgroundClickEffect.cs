using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace App.Game
{
    /// <summary>GameHud 背景点击特效；粒子播放结束后回收，池随本次 GameHud 销毁。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class HudBackgroundClickEffect : MonoBehaviour
    {
        [SerializeField] private GameObject effectPrefab;
        [SerializeField, Min(0)] private int maxIdleCount = 8;

        private sealed class Effect
        {
            public GameObject Root;
            public ParticleSystem[] Particles;
        }

        private SpriteRenderer _background;
        private Transform _poolRoot;
        private readonly Stack<Effect> _idle = new Stack<Effect>();
        private readonly List<Effect> _active = new List<Effect>();
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        private void Awake()
        {
            _background = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var effect = _active[i];
                var alive = false;
                foreach (var particle in effect.Particles)
                {
                    if (particle != null && particle.IsAlive(false))
                    {
                        alive = true;
                        break;
                    }
                }
                if (!alive)
                {
                    _active.RemoveAt(i);
                    Return(effect);
                }
            }

            if (Input.touchCount > 0)
            {
                for (var i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began) TryPlay(touch.position);
                }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                TryPlay(Input.mousePosition);
            }
        }

        private void TryPlay(Vector2 screenPosition)
        {
            if (effectPrefab == null || _background == null || !_background.enabled || _background.sprite == null)
                return;

            // RaycastAll 同时适用于鼠标和触摸，弹窗、人物按钮和开场遮罩都应挡住背景点击。
            if (EventSystem.current != null)
            {
                var pointer = new PointerEventData(EventSystem.current) { position = screenPosition };
                _uiHits.Clear();
                EventSystem.current.RaycastAll(pointer, _uiHits);
                if (_uiHits.Count > 0) return;
            }

            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out var distance)) return;
            var position = ray.GetPoint(distance);
            var localPosition = transform.InverseTransformPoint(position);
            var bounds = _background.sprite.bounds;
            localPosition.z = bounds.center.z;
            if (!bounds.Contains(localPosition)) return;
            // 卡牌的世界碰撞体接收自己的点击，不在牌面上叠加背景特效。
            if (Physics2D.OverlapPoint(position) != null) return;

            var effect = Rent();
            effect.Root.transform.position = position;
            effect.Root.SetActive(true);
            foreach (var particle in effect.Particles)
            {
                if (particle.gameObject.activeInHierarchy) particle.Play(false);
            }
            _active.Add(effect);
        }

        private Effect Rent()
        {
            if (_idle.Count > 0) return _idle.Pop();
            if (_poolRoot == null)
            {
                var root = new GameObject("BackgroundClickEffects");
                root.layer = gameObject.layer;
                // 与 bg 同级，避免背景铺满屏幕的缩放影响特效大小。
                root.transform.SetParent(transform.parent, false);
                _poolRoot = root.transform;
            }

            var instance = Instantiate(effectPrefab, _poolRoot, false);
            instance.name = effectPrefab.name;
            instance.SetActive(false);
            // 原素材属于 UI 层，会被 UI 相机按另一套坐标渲染。
            // 点击位置使用主相机换算，所有子节点也必须与背景同层，由同一相机绘制。
            foreach (var node in instance.GetComponentsInChildren<Transform>(true))
            {
                node.gameObject.layer = gameObject.layer;
            }
            var effect = new Effect { Root = instance, Particles = instance.GetComponentsInChildren<ParticleSystem>(true) };
            foreach (var particle in effect.Particles)
            {
                var main = particle.main;
                main.loop = false;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.useUnscaledTime = true;
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            var renderers = instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.sortingLayerID = _background.sortingLayerID;
                renderer.sortingOrder += _background.sortingOrder + 1;
            }
            return effect;
        }

        private void Return(Effect effect)
        {
            foreach (var particle in effect.Particles)
            {
                if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            effect.Root.SetActive(false);
            if (_idle.Count < maxIdleCount) _idle.Push(effect);
            else Destroy(effect.Root);
        }

        private void OnDisable()
        {
            foreach (var effect in _active) Return(effect);
            _active.Clear();
        }

        private void OnDestroy()
        {
            if (_poolRoot != null) Destroy(_poolRoot.gameObject);
            _idle.Clear();
        }
    }
}
