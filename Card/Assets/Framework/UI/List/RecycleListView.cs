using System;
using System.Collections.Generic;
using Framework.UI.Binding;
using Framework.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI.List
{
    public interface IListItemView
    {
        GameObject gameObject { get; }
        RectTransform RectTransform { get; }
        void BindItem(object item, int index);
        void UnbindItem();
    }

    public interface IListItemView<TItem> : IListItemView
    {
        void BindItem(TItem item, int index);
    }

    public abstract class ListItemViewBase<TItem> : MonoBehaviour, IListItemView<TItem>
    {
        private BindingContext _binding;

        public RectTransform RectTransform => (RectTransform)transform;
        protected BindingContext Binding => _binding;
        protected TItem Item { get; private set; }
        protected int Index { get; private set; }

        public void BindItem(object item, int index) => BindItem((TItem)item, index);

        public void BindItem(TItem item, int index)
        {
            UnbindItem();
            Item = item;
            Index = index;
            _binding = new BindingContext();
            OnBindItem(item, index);
        }

        public void UnbindItem()
        {
            if (_binding == null)
            {
                return;
            }

            OnUnbindItem();
            _binding.Dispose();
            _binding = null;
            Item = default;
            Index = -1;
        }

        protected abstract void OnBindItem(TItem item, int index);

        protected virtual void OnUnbindItem()
        {
        }
    }

    /// <summary>
    /// Vertical recycle list backed by ScrollRect + object pool.
    /// </summary>
    public sealed class RecycleListView : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField] private float _itemHeight = 80f;
        [SerializeField] private float _spacing;
        [SerializeField] private int _poolBuffer = 2;

        private readonly List<object> _items = new List<object>();
        private readonly Stack<IListItemView> _pool = new Stack<IListItemView>();
        private readonly Dictionary<int, IListItemView> _active = new Dictionary<int, IListItemView>();
        private GameObject _itemPrefab;
        private IDisposable _subscription;
        private bool _initialized;

        public float ItemHeight
        {
            get => _itemHeight;
            set => _itemHeight = Mathf.Max(1f, value);
        }

        public float Spacing
        {
            get => _spacing;
            set => _spacing = Mathf.Max(0f, value);
        }

        public void Initialize(ScrollRect scrollRect, GameObject itemPrefab, float itemHeight = 80f, float spacing = 0f)
        {
            _scrollRect = scrollRect;
            _itemPrefab = itemPrefab;
            _itemHeight = itemHeight;
            _spacing = spacing;
            _viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
            _content = scrollRect.content;
            _initialized = true;

            _scrollRect.onValueChanged.RemoveListener(OnScroll);
            _scrollRect.onValueChanged.AddListener(OnScroll);
        }

        public void Bind<TItem>(ObservableList<TItem> source)
        {
            EnsureInitialized();
            _subscription?.Dispose();
            _items.Clear();

            _subscription = source.Subscribe(args =>
            {
                switch (args.Action)
                {
                    case CollectionChangeAction.Add:
                        _items.Insert(args.Index, args.Item);
                        break;
                    case CollectionChangeAction.Remove:
                        _items.RemoveAt(args.Index);
                        break;
                    case CollectionChangeAction.Replace:
                        _items[args.Index] = args.Item;
                        break;
                    case CollectionChangeAction.Move:
                        var moved = _items[args.OldIndex];
                        _items.RemoveAt(args.OldIndex);
                        _items.Insert(args.Index, moved);
                        break;
                    case CollectionChangeAction.Reset:
                        _items.Clear();
                        foreach (var item in source)
                        {
                            _items.Add(item);
                        }
                        break;
                }

                Refresh();
            });
        }

        public void SetItems<TItem>(IReadOnlyList<TItem> items)
        {
            EnsureInitialized();
            _subscription?.Dispose();
            _subscription = null;
            _items.Clear();
            if (items != null)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    _items.Add(items[i]);
                }
            }

            Refresh();
        }

        public void Refresh()
        {
            EnsureInitialized();
            UpdateContentSize();
            RecycleAll();
            UpdateVisible();
        }

        private void OnScroll(Vector2 _)
        {
            UpdateVisible();
        }

        private void UpdateContentSize()
        {
            var height = _items.Count <= 0
                ? 0f
                : _items.Count * _itemHeight + Mathf.Max(0, _items.Count - 1) * _spacing;
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        private void UpdateVisible()
        {
            if (_items.Count == 0)
            {
                RecycleAll();
                return;
            }

            var viewportHeight = _viewport.rect.height;
            var scrollY = Mathf.Max(0f, _content.anchoredPosition.y);
            var stride = _itemHeight + _spacing;
            var first = Mathf.Max(0, Mathf.FloorToInt(scrollY / stride) - _poolBuffer);
            var visibleCount = Mathf.CeilToInt(viewportHeight / stride) + _poolBuffer * 2;
            var last = Mathf.Min(_items.Count - 1, first + visibleCount);

            var toRemove = new List<int>();
            foreach (var pair in _active)
            {
                if (pair.Key < first || pair.Key > last)
                {
                    toRemove.Add(pair.Key);
                }
            }

            for (var i = 0; i < toRemove.Count; i++)
            {
                Recycle(toRemove[i]);
            }

            for (var index = first; index <= last; index++)
            {
                if (_active.ContainsKey(index))
                {
                    continue;
                }

                var itemView = Rent();
                var rect = itemView.RectTransform;
                rect.SetParent(_content, false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, _itemHeight);
                rect.anchoredPosition = new Vector2(0f, -index * stride);
                itemView.gameObject.SetActive(true);
                itemView.BindItem(_items[index], index);
                _active[index] = itemView;
            }
        }

        private IListItemView Rent()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }

            var go = Instantiate(_itemPrefab, _content, false);
            var itemView = go.GetComponent<IListItemView>();
            if (itemView == null)
            {
                Destroy(go);
                throw new InvalidOperationException("Item prefab must implement IListItemView.");
            }

            return itemView;
        }

        private void Recycle(int index)
        {
            if (!_active.TryGetValue(index, out var itemView))
            {
                return;
            }

            _active.Remove(index);
            itemView.UnbindItem();
            itemView.gameObject.SetActive(false);
            _pool.Push(itemView);
        }

        private void RecycleAll()
        {
            var keys = new List<int>(_active.Keys);
            for (var i = 0; i < keys.Count; i++)
            {
                Recycle(keys[i]);
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized && _scrollRect != null && _content != null && _itemPrefab != null)
            {
                return;
            }

            throw new InvalidOperationException("RecycleListView.Initialize must be called first.");
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
            if (_scrollRect != null)
            {
                _scrollRect.onValueChanged.RemoveListener(OnScroll);
            }
        }
    }
}
