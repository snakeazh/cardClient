using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Framework.UI.Navigation
{
    public sealed class UIRoot : MonoBehaviour
    {
        public const string ResourcesPath = "UI/UIRoot";

        private readonly Dictionary<UILayer, RectTransform> _layers = new Dictionary<UILayer, RectTransform>();

        [SerializeField] private RectTransform _hudLayer;
        [SerializeField] private RectTransform _pageLayer;
        [SerializeField] private RectTransform _navigationLayer;
        [SerializeField] private RectTransform _popupLayer;
        [SerializeField] private RectTransform _loadingLayer;
        [SerializeField] private RectTransform _topMostLayer;
        [SerializeField] private RectTransform _resourceLayer;
        [SerializeField] private RectTransform _guideLayer;
        [SerializeField] private RectTransform _toastLayer;
        [SerializeField] private RectTransform _blackLeft;
        [SerializeField] private RectTransform _blackRight;

        private CanvasScaler _scaler;
        private int _lastWidth;
        private int _lastHeight;

        public Canvas RootCanvas { get; private set; }

        /// <summary>
        /// Instantiates UIRoot from a prefab asset (loaded via IResourceService in UIFramework).
        /// </summary>
        public static UIRoot Instantiate(GameObject prefab, Transform parent = null)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            var root = prefab.GetComponent<UIRoot>();
            if (root == null)
            {
                throw new InvalidOperationException($"Prefab '{prefab.name}' must have a UIRoot component.");
            }

            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = "UIRoot";
            DontDestroyOnLoad(instance);

            var uiRoot = instance.GetComponent<UIRoot>();
            uiRoot.EnsureInitialized();
            EnsureEventSystem();
            return uiRoot;
        }

        public RectTransform GetLayer(UILayer layer)
        {
            EnsureInitialized();
            if (!_layers.TryGetValue(layer, out var rect))
            {
                throw new InvalidOperationException($"UI layer not found: {layer}");
            }

            return rect;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            ApplyWidescreenFit(true);
        }

        private void LateUpdate()
        {
            ApplyWidescreenFit(false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyWidescreenFit(true);
        }
#endif

        /// <summary>
        /// 宽于 9:16 时按高度适配（与 GameHud 锁定设计高度一致），左右用黑边遮挡；
        /// 否则保持按宽度适配，黑边宽度为 0。
        /// </summary>
        private void ApplyWidescreenFit(bool force)
        {
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            EnsureInitialized();

            if (!force && width == _lastWidth && height == _lastHeight)
            {
                return;
            }

            _lastWidth = width;
            _lastHeight = height;

            if (_scaler == null)
            {
                _scaler = GetComponent<CanvasScaler>();
            }

            Vector2 reference = _scaler != null
                ? _scaler.referenceResolution
                : new Vector2(1080f, 1920f);
            float designWidth = reference.x;
            float designHeight = reference.y;
            if (designWidth <= 0f || designHeight <= 0f)
            {
                return;
            }

            float designAspect = designWidth / designHeight;
            float currentAspect = (float)width / height;
            float barWidth = 0f;
            if (currentAspect > designAspect)
            {
                if (_scaler != null)
                {
                    _scaler.matchWidthOrHeight = 1f;
                }

                barWidth = (designHeight * currentAspect - designWidth) * 0.5f;
            }
            else if (_scaler != null)
            {
                _scaler.matchWidthOrHeight = 0f;
            }

            SetBarWidth(_blackLeft, barWidth);
            SetBarWidth(_blackRight, barWidth);
            InsetLayers(barWidth);
        }

        private static void SetBarWidth(RectTransform bar, float width)
        {
            if (bar == null)
            {
                return;
            }

            var size = bar.sizeDelta;
            size.x = width;
            bar.sizeDelta = size;
        }

        private void InsetLayers(float barWidth)
        {
            float sizeX = -barWidth * 2f;
            foreach (var pair in _layers)
            {
                var rect = pair.Value;
                if (rect == null)
                {
                    continue;
                }

                var size = rect.sizeDelta;
                size.x = sizeX;
                rect.sizeDelta = size;
            }
        }

        private void EnsureInitialized()
        {
            if (_layers.Count > 0)
            {
                return;
            }

            RootCanvas = GetComponent<Canvas>();
            if (RootCanvas == null)
            {
                throw new InvalidOperationException("UIRoot prefab requires a Canvas on the root object.");
            }

            RegisterLayer(UILayer.Hud, _hudLayer);
            RegisterLayer(UILayer.Page, _pageLayer);
            RegisterLayer(UILayer.Navigation, _navigationLayer);
            RegisterLayer(UILayer.Popup, _popupLayer);
            RegisterLayer(UILayer.Resource, _resourceLayer);
            RegisterLayer(UILayer.Loading, _loadingLayer);
            RegisterLayer(UILayer.TopMost, _topMostLayer);
            RegisterLayer(UILayer.Toast, _toastLayer);
            RegisterLayer(UILayer.Guide, _guideLayer);
        }

        private void RegisterLayer(UILayer layer, RectTransform rect)
        {
            if (rect == null)
            {
                throw new InvalidOperationException(
                    $"UIRoot layer '{layer}' is not assigned. Regenerate the UIRoot prefab or assign it in the Inspector.");
            }

            _layers[layer] = rect;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }
}
