using System;
using System.Collections;
using System.Collections.Generic;

namespace Framework.UI.Core
{
    public enum CollectionChangeAction
    {
        Add,
        Remove,
        Replace,
        Reset,
        Move
    }

    public readonly struct CollectionChangedEventArgs
    {
        public CollectionChangedEventArgs(
            CollectionChangeAction action,
            int index = -1,
            int oldIndex = -1,
            object item = null,
            object oldItem = null)
        {
            Action = action;
            Index = index;
            OldIndex = oldIndex;
            Item = item;
            OldItem = oldItem;
        }

        public CollectionChangeAction Action { get; }
        public int Index { get; }
        public int OldIndex { get; }
        public object Item { get; }
        public object OldItem { get; }
    }

    public sealed class ObservableList<T> : IList<T>, IReadOnlyList<T>
    {
        private readonly List<T> _items = new List<T>();

        public event Action<CollectionChangedEventArgs> Changed;

        public int Count => _items.Count;
        public bool IsReadOnly => false;

        public T this[int index]
        {
            get => _items[index];
            set
            {
                var old = _items[index];
                _items[index] = value;
                Raise(new CollectionChangedEventArgs(CollectionChangeAction.Replace, index, item: value, oldItem: old));
            }
        }

        public void Add(T item)
        {
            _items.Add(item);
            Raise(new CollectionChangedEventArgs(CollectionChangeAction.Add, _items.Count - 1, item: item));
        }

        public void Insert(int index, T item)
        {
            _items.Insert(index, item);
            Raise(new CollectionChangedEventArgs(CollectionChangeAction.Add, index, item: item));
        }

        public bool Remove(T item)
        {
            var index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            var item = _items[index];
            _items.RemoveAt(index);
            Raise(new CollectionChangedEventArgs(CollectionChangeAction.Remove, index, item: item));
        }

        public void Clear()
        {
            _items.Clear();
            Raise(new CollectionChangedEventArgs(CollectionChangeAction.Reset));
        }

        public void Reset(IEnumerable<T> items)
        {
            _items.Clear();
            if (items != null)
            {
                _items.AddRange(items);
            }

            Raise(new CollectionChangedEventArgs(CollectionChangeAction.Reset));
        }

        public void Move(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex)
            {
                return;
            }

            var item = _items[oldIndex];
            _items.RemoveAt(oldIndex);
            _items.Insert(newIndex, item);
            Raise(new CollectionChangedEventArgs(CollectionChangeAction.Move, newIndex, oldIndex, item));
        }

        public int IndexOf(T item) => _items.IndexOf(item);
        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IDisposable Subscribe(Action<CollectionChangedEventArgs> handler, bool emitReset = true)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Changed += handler;
            if (emitReset)
            {
                handler(new CollectionChangedEventArgs(CollectionChangeAction.Reset));
            }

            return new ActionDisposable(() => Changed -= handler);
        }

        private void Raise(CollectionChangedEventArgs args)
        {
            Changed?.Invoke(args);
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action _dispose;

            public ActionDisposable(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}
