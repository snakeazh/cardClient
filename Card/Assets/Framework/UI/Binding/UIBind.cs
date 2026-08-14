using System;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Framework.UI.Binding
{
    /// <summary>
    /// Marks a UI node for lookup. Pick which component or GameObject on this node is exposed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIBind : MonoBehaviour
    {
        [SerializeField] private string _key;
        [SerializeField] private UnityObject _target;

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

        public UnityObject Target
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

        public T Get<T>() where T : UnityObject
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
            if (_target == null)
            {
                return;
            }

            var owner = ResolveGameObject(_target);
            if (owner != gameObject)
            {
                _target = null;
            }
        }

        private UnityObject PreferDefaultTarget()
        {
            var components = GetComponents<Component>();
            Component transform = null;
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is UIBind)
                {
                    continue;
                }

                if (component is Transform)
                {
                    transform = component;
                    continue;
                }

                return component;
            }

            if (transform != null)
            {
                return transform;
            }

            return gameObject;
        }

        private static GameObject ResolveGameObject(UnityObject target)
        {
            if (target is GameObject go)
            {
                return go;
            }

            if (target is Component component)
            {
                return component.gameObject;
            }

            return null;
        }
#endif
    }
}
