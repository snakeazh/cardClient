using System;
using UnityEngine;

namespace Framework.UI.Binding
{
    /// <summary>
    /// Marks a UI node for lookup. Pick which component on this GameObject is exposed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIBind : MonoBehaviour
    {
        [SerializeField] private string _key;
        [SerializeField] private Component _target;

        public string Key
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_key))
                {
                    return _key;
                }

                return gameObject != null ? gameObject.name : string.Empty;
            }
        }

        public Component Target
        {
            get
            {
                if (_target != null)
                {
                    return _target;
                }

                return this;
            }
        }

        public T Get<T>() where T : Component
        {
            var target = Target;
            if (target is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException(
                $"UIBind '{Key}' target is {target?.GetType().Name ?? "null"}, requested {typeof(T).Name}.");
        }

#if UNITY_EDITOR
        private void Reset()
        {
            if (string.IsNullOrWhiteSpace(_key))
            {
                _key = gameObject.name;
            }

            if (_target == null)
            {
                _target = PreferDefaultTarget();
            }
        }

        private void OnValidate()
        {
            if (_target != null && _target.gameObject != gameObject)
            {
                _target = null;
            }
        }

        private Component PreferDefaultTarget()
        {
            var components = GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform || component is UIBind)
                {
                    continue;
                }

                return component;
            }

            return this;
        }
#endif
    }
}
