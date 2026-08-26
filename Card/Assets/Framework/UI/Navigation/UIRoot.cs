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
