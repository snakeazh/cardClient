using System;
using System.Collections.Generic;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Framework.UI.Binding
{
    /// <summary>
    /// Collects <see cref="UIBind"/> nodes under this object and resolves them by key.
    /// Place on the View root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIReference : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public UnityObject Component;
            public UIBind Bind;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        private Dictionary<string, UnityObject> _map;
        private bool _built;

        public IReadOnlyList<Entry> Entries => _entries;

        public T Get<T>(string key) where T : UnityObject
        {
            var target = Get(key);
            if (target is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException(
                $"UIReference '{name}' key '{key}' is {target?.GetType().Name ?? "null"}, requested {typeof(T).Name}.");
        }

        public UnityObject Get(string key)
        {
            EnsureMap();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Bind key cannot be empty.", nameof(key));
            }

            if (!_map.TryGetValue(key, out var target) || target == null)
            {
                throw new KeyNotFoundException($"UIReference '{name}' missing bind key '{key}'.");
            }

            return target;
        }

        public bool TryGet<T>(string key, out T component) where T : UnityObject
        {
            EnsureMap();
            component = null;
            if (string.IsNullOrWhiteSpace(key) || !_map.TryGetValue(key, out var raw) || !(raw is T typed))
            {
                return false;
            }

            component = typed;
            return true;
        }

        public GameObject GetGameObject(string key)
        {
            var target = Get(key);
            if (target is GameObject go)
            {
                return go;
            }

            if (target is Component component)
            {
                return component.gameObject;
            }

            throw new InvalidOperationException(
                $"UIReference '{name}' key '{key}' cannot resolve GameObject from {target?.GetType().Name ?? "null"}.");
        }

        [ContextMenu("Collect UIBinds")]
        public void Collect()
        {
            _entries.Clear();
            var binds = new List<UIBind>();
            CollectBinds(transform, binds);
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < binds.Count; i++)
            {
                var bind = binds[i];
                if (bind == null)
                {
                    continue;
                }

                var key = bind.Key;
                if (string.IsNullOrWhiteSpace(key))
                {
                    Debug.LogWarning($"UIBind on '{bind.name}' has empty key.", bind);
                    continue;
                }

                if (!usedKeys.Add(key))
                {
                    Debug.LogError($"Duplicate UIBind key '{key}' under '{name}'.", bind);
                    continue;
                }

                _entries.Add(new Entry
                {
                    Key = key,
                    Component = bind.Target,
                    Bind = bind
                });
            }

            _map = null;
            _built = false;
        }

        private static void CollectBinds(Transform root, List<UIBind> binds)
        {
            if (root == null)
            {
                return;
            }

            var bind = root.GetComponent<UIBind>();
            if (bind != null)
            {
                binds.Add(bind);
            }

            for (var i = 0; i < root.childCount; i++)
            {
                CollectBinds(root.GetChild(i), binds);
            }
        }

        private void Awake()
        {
            EnsureMap();
        }

        private void EnsureMap()
        {
            if (_built && _map != null)
            {
                return;
            }

            _map = new Dictionary<string, UnityObject>(StringComparer.Ordinal);
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key) || entry.Component == null)
                {
                    continue;
                }

                _map[entry.Key] = entry.Component;
            }

            _built = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _map = null;
            _built = false;
        }
#endif
    }
}
