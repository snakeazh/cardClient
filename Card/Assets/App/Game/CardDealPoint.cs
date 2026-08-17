using System.Collections.Generic;
using UnityEngine;

namespace App.Game
{
    /// <summary>
    /// 发牌点叠牌。子牌按自定义 X/Y 偏移重叠，后入的在最上面。
    /// 挂在 GameHud 的 dealpoint 节点上。
    /// </summary>
    public class CardDealPoint : MonoBehaviour
    {
        [SerializeField] private float offsetX = 0.002f;
        [SerializeField] private float offsetY = 0.005f;
        [SerializeField] private int sortingOrderStart = 1;
        [SerializeField] private int sortingOrderStep = 1;

        private readonly List<CardItem> _stack = new List<CardItem>();

        public float OffsetX
        {
            get => offsetX;
            set
            {
                offsetX = value;
                Relayout();
            }
        }

        public float OffsetY
        {
            get => offsetY;
            set
            {
                offsetY = value;
                Relayout();
            }
        }

        public int Count => _stack.Count;

        public Vector3 GetLocalPosition(int index)
        {
            return new Vector3(offsetX * index, offsetY * index, 0f);
        }

        public Vector3 GetWorldPosition(int index)
        {
            return transform.TransformPoint(GetLocalPosition(index));
        }

        public int GetSortingOrder(int index)
        {
            return sortingOrderStart + index * sortingOrderStep;
        }

        public void SetOffset(float x, float y)
        {
            offsetX = x;
            offsetY = y;
            Relayout();
        }

        public void Attach(CardItem item)
        {
            if (item == null)
            {
                return;
            }

            _stack.Remove(item);
            _stack.Add(item);
            ApplyLayout(item, _stack.Count - 1);
        }

        public CardItem Peek()
        {
            return _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        }

        public CardItem GetCard(int index)
        {
            if (index < 0 || index >= _stack.Count)
            {
                return null;
            }

            return _stack[index];
        }

        public CardItem Pop()
        {
            if (_stack.Count == 0)
            {
                return null;
            }

            var last = _stack.Count - 1;
            var item = _stack[last];
            _stack.RemoveAt(last);
            if (item != null)
            {
                item.transform.SetParent(null, true);
            }

            return item;
        }

        public void Relayout()
        {
            for (var i = 0; i < _stack.Count; i++)
            {
                ApplyLayout(_stack[i], i);
            }
        }

        public void Clear()
        {
            for (var i = 0; i < _stack.Count; i++)
            {
                if (_stack[i] != null)
                {
                    Destroy(_stack[i].gameObject);
                }
            }

            _stack.Clear();
        }

        private void ApplyLayout(CardItem item, int index)
        {
            if (item == null)
            {
                return;
            }

            var t = item.transform;
            t.SetParent(transform, false);
            t.localPosition = GetLocalPosition(index);
            t.localScale = Vector3.one;
            item.SetFace(item.FaceState);

            var sr = item.CurrentRenderer;
            if (sr != null)
            {
                sr.sortingOrder = GetSortingOrder(index);
            }
        }

        private void OnDestroy()
        {
            _stack.Clear();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                Relayout();
                return;
            }

            var index = 0;
            for (var i = 0; i < transform.childCount; i++)
            {
                var item = transform.GetChild(i).GetComponent<CardItem>();
                if (item == null)
                {
                    continue;
                }

                item.transform.localPosition = GetLocalPosition(index);
                var sr = item.CurrentRenderer != null
                    ? item.CurrentRenderer
                    : item.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null)
                {
                    sr.sortingOrder = GetSortingOrder(index);
                }

                index++;
            }
        }
#endif
    }
}
