using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.List
{
    /// <summary>
    /// 分区块卡片行的竖向虚拟化列表（纯 C#，由 View 持有并随 View 生命周期销毁）：
    /// 每个区块 = 一条横幅（常驻克隆）+ 若干卡片行，行只在视口范围（含上下缓冲行）内生成，
    /// 行节点与行内卡槽均池化复用（槽只增不销毁，空槽隐藏）。
    /// Content 上的布局组件（VerticalLayoutGroup / ContentSizeFitter / GridLayoutGroup）会被停用，
    /// 行位置与 Content 高度改由本组件计算；要求 Content 顶部锚、水平拉伸（标准 ScrollRect 结构）。
    /// 行按索引排 sibling 顺序，负间距重叠式 Grid「后行盖前行」与原单 Grid 行为一致。
    /// </summary>
    public sealed class CardRowRecycler<TItem> : IDisposable
    {
        private const int RowBuffer = 1;

        public sealed class Section
        {
            /// <summary>区块横幅模板（Content 下原节点；克隆后原节点隐藏）。null = 无横幅。</summary>
            public RectTransform BannerTemplate;

            /// <summary>区块 Grid（读布局参数后整体隐藏）。收藏页单区可传 Content 自身。</summary>
            public GridLayoutGroup GridTemplate;

            /// <summary>该区块条目（按顺序切分成行）。</summary>
            public List<TItem> Items = new List<TItem>();
        }

        private sealed class Row
        {
            public float Top;
            public int SectionIndex;
            public int Start;
            public int Count;
        }

        private sealed class RowView
        {
            public RectTransform Root;
            public readonly List<Component> Slots = new List<Component>();
        }

        private readonly List<Section> _sections = new List<Section>();
        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<int, RowView> _active = new Dictionary<int, RowView>();
        private readonly Stack<RowView> _pool = new Stack<RowView>();
        private readonly List<RectTransform> _banners = new List<RectTransform>();
        private readonly List<int> _recycleBuffer = new List<int>();

        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private Func<Transform, Component> _createSlot;
        private Action<Component, TItem> _bindSlot;
        private int _columns = 1;
        private Vector2 _cellSize = new Vector2(200f, 300f);
        private Vector2 _spacing = Vector2.zero;
        private TextAnchor _childAlignment = TextAnchor.MiddleCenter;
        private float _sectionGap;
        private float _gridPaddingTop;
        private float _gridPaddingBottom;
        private int _gridPaddingLeft;
        private int _gridPaddingRight;
        private float _contentPaddingTop;
        private float _contentPaddingBottom;
        private float _stride = 300f;
        private float _contentHeight;

        public void Initialize(
            ScrollRect scroll,
            GridLayoutGroup gridTemplate,
            VerticalLayoutGroup contentLayout,
            Func<Transform, Component> createSlot,
            Action<Component, TItem> bindSlot)
        {
            _scroll = scroll;
            _viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            _content = scroll.content;
            _createSlot = createSlot;
            _bindSlot = bindSlot;

            if (gridTemplate != null)
            {
                if (gridTemplate.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
                {
                    _columns = Mathf.Max(1, gridTemplate.constraintCount);
                }
                else
                {
                    // flexible 列约束按 Content 宽估算（与旧 ResolveColumnCount 口径一致）
                    var width = _content.rect.width;
                    if (width <= 1f && _viewport != null)
                    {
                        width = _viewport.rect.width;
                    }

                    var strideX = gridTemplate.cellSize.x + gridTemplate.spacing.x;
                    var inner = width - gridTemplate.padding.horizontal + gridTemplate.spacing.x;
                    _columns = strideX > 0f
                        ? Mathf.Max(1, Mathf.FloorToInt((inner + 0.001f) / strideX))
                        : 1;
                }

                _cellSize = gridTemplate.cellSize;
                _spacing = gridTemplate.spacing;
                _childAlignment = gridTemplate.childAlignment;
                _gridPaddingTop = gridTemplate.padding.top;
                _gridPaddingBottom = gridTemplate.padding.bottom;
                _gridPaddingLeft = gridTemplate.padding.left;
                _gridPaddingRight = gridTemplate.padding.right;
                _stride = _cellSize.y + _spacing.y;
            }

            if (contentLayout != null)
            {
                _sectionGap = contentLayout.spacing;
                _contentPaddingTop = contentLayout.padding.top;
                _contentPaddingBottom = contentLayout.padding.bottom;
                contentLayout.enabled = false;
            }

            var fitter = _content != null ? _content.GetComponent<ContentSizeFitter>() : null;
            if (fitter != null)
            {
                fitter.enabled = false;
            }

            // Content 自身挂 GridLayoutGroup 的单区页（收藏页）：布局交给行节点，原组件停用
            if (gridTemplate != null && gridTemplate.transform == _content)
            {
                gridTemplate.enabled = false;
            }

            _scroll.onValueChanged.RemoveListener(OnScroll);
            _scroll.onValueChanged.AddListener(OnScroll);
        }

        public void SetSections(IReadOnlyList<Section> sections)
        {
            RecycleAll();

            for (var i = 0; i < _banners.Count; i++)
            {
                if (_banners[i] != null)
                {
                    UnityEngine.Object.Destroy(_banners[i].gameObject);
                }
            }

            _banners.Clear();
            _sections.Clear();
            _rows.Clear();

            var top = _contentPaddingTop;
            var firstVisible = true;
            for (var s = 0; s < sections.Count; s++)
            {
                var section = sections[s];
                _sections.Add(section);
                if (section.GridTemplate != null && section.GridTemplate.transform != _content)
                {
                    section.GridTemplate.gameObject.SetActive(false);
                }

                if (section.Items == null || section.Items.Count == 0)
                {
                    continue; // 空区不出横幅（与旧分区口径一致）
                }

                if (!firstVisible)
                {
                    top += _sectionGap; // 区块间距只出现在实际相邻的可见区块之间
                }

                firstVisible = false;
                if (section.BannerTemplate != null)
                {
                    var banner = UnityEngine.Object.Instantiate(section.BannerTemplate, _content, false);
                    banner.name = section.BannerTemplate.name;
                    section.BannerTemplate.gameObject.SetActive(false);
                    var height = ResolveHeight(section.BannerTemplate);
                    StretchAt(banner, top, height);
                    _banners.Add(banner);
                    top += height + _sectionGap;
                }

                top += _gridPaddingTop;
                var rowCount = Mathf.CeilToInt(section.Items.Count / (float)_columns);
                for (var r = 0; r < rowCount; r++)
                {
                    _rows.Add(new Row
                    {
                        Top = top,
                        SectionIndex = _sections.Count - 1,
                        Start = r * _columns,
                        Count = Mathf.Min(_columns, section.Items.Count - r * _columns)
                    });
                    top += _stride;
                }

                if (rowCount > 0)
                {
                    top += _cellSize.y - _stride; // 末行步进从 stride 校正回整格高
                }

                top += _gridPaddingBottom;
            }

            top += _contentPaddingBottom;
            _contentHeight = top;
            if (_content != null)
            {
                _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, top));
            }

            UpdateVisible();
        }

        /// <summary>收集当前生成中的卡槽（View 据此刷新选中态等）。</summary>
        public void CollectActiveSlots(List<Component> output)
        {
            output.Clear();
            foreach (var pair in _active)
            {
                var slots = pair.Value.Slots;
                for (var i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot != null && slot.gameObject.activeSelf)
                    {
                        output.Add(slot);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_scroll != null)
            {
                _scroll.onValueChanged.RemoveListener(OnScroll);
            }
        }

        private void OnScroll(Vector2 _)
        {
            UpdateVisible();
        }

        private void UpdateVisible()
        {
            if (_scroll == null || _content == null)
            {
                return;
            }

            if (_rows.Count == 0)
            {
                RecycleAll();
                return;
            }

            var viewHeight = _viewport.rect.height;
            var scrollY = Mathf.Clamp(
                _content.anchoredPosition.y, 0f, Mathf.Max(0f, _contentHeight - viewHeight));
            var buffer = _stride * RowBuffer;
            var viewBottom = scrollY + viewHeight + buffer;

            var first = -1;
            var last = -1;
            for (var i = 0; i < _rows.Count; i++)
            {
                var rowTop = _rows[i].Top;
                if (rowTop > viewBottom)
                {
                    break; // Top 单调递增，越界即止
                }

                if (rowTop + _cellSize.y >= scrollY - buffer)
                {
                    if (first < 0)
                    {
                        first = i;
                    }

                    last = i;
                }
            }

            if (first < 0)
            {
                RecycleAll();
                return;
            }

            _recycleBuffer.Clear();
            foreach (var pair in _active)
            {
                if (pair.Key < first || pair.Key > last)
                {
                    _recycleBuffer.Add(pair.Key);
                }
            }

            for (var i = 0; i < _recycleBuffer.Count; i++)
            {
                RecycleRow(_recycleBuffer[i]);
            }

            for (var index = first; index <= last; index++)
            {
                if (_active.ContainsKey(index))
                {
                    continue;
                }

                var view = RentRow();
                _active[index] = view;
                BindRow(view, _rows[index]);
            }

            // 行按索引排 sibling，保证重叠式 Grid 的叠放方向与原单 Grid 一致（后行盖前行）
            _recycleBuffer.Clear();
            _recycleBuffer.AddRange(_active.Keys);
            _recycleBuffer.Sort();
            for (var i = 0; i < _recycleBuffer.Count; i++)
            {
                _active[_recycleBuffer[i]].Root.SetAsLastSibling();
            }
        }

        private RowView RentRow()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled != null && pooled.Root != null)
                {
                    pooled.Root.gameObject.SetActive(true);
                    return pooled;
                }
            }

            var go = new GameObject("CardRow", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_content, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, _cellSize.y);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = _cellSize;
            grid.spacing = _spacing;
            grid.childAlignment = _childAlignment;
            // 左右 padding 沿用分区模板；垂直 padding 已计入行 Top 偏移，行内不再加（避免重复）
            grid.padding = new RectOffset(_gridPaddingLeft, _gridPaddingRight, 0, 0);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = _columns;
            go.SetActive(true);
            return new RowView { Root = rect };
        }

        private void BindRow(RowView view, Row row)
        {
            view.Root.anchoredPosition = new Vector2(0f, -row.Top);
            var items = _sections[row.SectionIndex].Items;
            while (view.Slots.Count < row.Count)
            {
                var slot = _createSlot != null ? _createSlot(view.Root) : null;
                if (slot == null)
                {
                    break; // 槽预制体异常时保底：本行少卡，不阻断滚动
                }

                view.Slots.Add(slot);
            }

            for (var i = 0; i < view.Slots.Count; i++)
            {
                var slot = view.Slots[i];
                if (slot == null)
                {
                    continue;
                }

                var inRow = i < row.Count;
                if (slot.gameObject.activeSelf != inRow)
                {
                    slot.gameObject.SetActive(inRow);
                }

                if (inRow && _bindSlot != null)
                {
                    _bindSlot(slot, items[row.Start + i]);
                }
            }
        }

        private void RecycleRow(int index)
        {
            if (!_active.TryGetValue(index, out var view))
            {
                return;
            }

            _active.Remove(index);
            var slots = view.Slots;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(false);
                }
            }

            view.Root.gameObject.SetActive(false);
            _pool.Push(view);
        }

        private void RecycleAll()
        {
            _recycleBuffer.Clear();
            _recycleBuffer.AddRange(_active.Keys);
            for (var i = 0; i < _recycleBuffer.Count; i++)
            {
                RecycleRow(_recycleBuffer[i]);
            }
        }

        private static void StretchAt(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, -top);
        }

        /// <summary>横幅占位高：LayoutElement 优先（VLG 布局口径），否则当前 rect 高。</summary>
        private static float ResolveHeight(RectTransform rect)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element != null)
            {
                if (element.preferredHeight > 0f)
                {
                    return element.preferredHeight;
                }

                if (element.minHeight > 0f)
                {
                    return element.minHeight;
                }
            }

            return rect.rect.height;
        }
    }
}
