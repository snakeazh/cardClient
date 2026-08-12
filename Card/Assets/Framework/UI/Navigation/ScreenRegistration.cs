using System;
using UnityEngine;

namespace Framework.UI.Navigation
{
    public readonly struct ScreenId : IEquatable<ScreenId>
    {
        public ScreenId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Screen id cannot be empty.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(ScreenId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ScreenId other && Equals(other);

        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

        public override string ToString() => Value;

        public static implicit operator ScreenId(string value) => new ScreenId(value);

        public static bool operator ==(ScreenId left, ScreenId right) => left.Equals(right);
        public static bool operator !=(ScreenId left, ScreenId right) => !left.Equals(right);
    }

    public sealed class ScreenRegistration
    {
        public ScreenRegistration(
            ScreenId id,
            UILayer layer,
            GameObject prefab,
            Type viewModelType,
            Func<object> viewModelFactory = null)
            : this(id, layer, prefab, null, viewModelType, viewModelFactory)
        {
        }

        public ScreenRegistration(
            ScreenId id,
            UILayer layer,
            string assetKey,
            Type viewModelType,
            Func<object> viewModelFactory = null)
            : this(id, layer, null, assetKey, viewModelType, viewModelFactory)
        {
        }

        private ScreenRegistration(
            ScreenId id,
            UILayer layer,
            GameObject prefab,
            string assetKey,
            Type viewModelType,
            Func<object> viewModelFactory)
        {
            if (prefab == null && string.IsNullOrWhiteSpace(assetKey))
            {
                throw new ArgumentException("Either prefab or assetKey must be provided.");
            }

            Id = id;
            Layer = layer;
            Prefab = prefab;
            AssetKey = assetKey;
            ViewModelType = viewModelType ?? throw new ArgumentNullException(nameof(viewModelType));
            ViewModelFactory = viewModelFactory;
        }

        public ScreenId Id { get; }
        public UILayer Layer { get; }
        public GameObject Prefab { get; }
        public string AssetKey { get; }
        public Type ViewModelType { get; }
        public Func<object> ViewModelFactory { get; }
    }
}
