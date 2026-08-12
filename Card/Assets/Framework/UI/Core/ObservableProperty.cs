using System;
using System.Collections.Generic;

namespace Framework.UI.Core
{
    public sealed class ObservableProperty<T>
    {
        private T _value;
        private readonly IEqualityComparer<T> _comparer;

        public event Action<T> Changed;

        public ObservableProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            _value = initialValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public T Value
        {
            get => _value;
            set
            {
                if (_comparer.Equals(_value, value))
                {
                    return;
                }

                _value = value;
                Changed?.Invoke(_value);
            }
        }

        public void ForceNotify()
        {
            Changed?.Invoke(_value);
        }

        public IDisposable Subscribe(Action<T> handler, bool emitCurrent = true)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Changed += handler;
            if (emitCurrent)
            {
                handler(_value);
            }

            return new Subscription(() => Changed -= handler);
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;

            public Subscription(Action dispose)
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
