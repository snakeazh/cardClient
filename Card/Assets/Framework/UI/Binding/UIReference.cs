using System;
using System.Collections.Generic;
using UnityEngine;

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
            public Component Component;
            public UIBind Bind;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        private Dictionary<string, Component> _map;
        private bool _built;

        public IReadOnlyList<Entry> Entries => _entries;

        public T Get<T>(string key) where T : Component
        {
            var component = Get(key);
            if (component is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException(
                $"UIReference '{name}' key '{key}' is {component?.GetType().Name ?? "null"}, requested {typeof(T).Name}.");
        }

        public Component Get(string key)
        {
            EnsureMap();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Bind key cannot be empty.", nameof(key));
            }

            if (!_map.TryGetValue(key, out var component) || component == null)
            {
                throw new KeyNotFoundException($"UIReference '{name}' missing bind key '{key}'.");
            }

            return component;
        }

        public bool TryGet<T>(string key, out T component) where T : Component
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
            return Get(key).gameObject;
        }

        [ContextMenu("Collect UIBinds")]
        public void Collect()
        {
            _entries.Clear();
            var binds = GetComponentsInChildren<UIBind>(true);
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < binds.Length; i++)
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

            _map = new Dictionary<string, Component>(StringComparer.Ordinal);
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
