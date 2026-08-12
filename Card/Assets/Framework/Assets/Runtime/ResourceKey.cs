using System;

namespace Framework.Assets
{
    /// <summary>
    /// Strongly-typed resource key. Value is the backend address
    /// (e.g. path under Resources/ such as "UI/ConfirmDialog").
    /// </summary>
    public readonly struct ResourceKey : IEquatable<ResourceKey>
    {
        public ResourceKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Resource key cannot be empty.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(ResourceKey other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ResourceKey other && Equals(other);

        public override int GetHashCode() =>
            Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

        public override string ToString() => Value;

        public static implicit operator ResourceKey(string value) => new ResourceKey(value);

        public static implicit operator string(ResourceKey key) => key.Value;

        public static bool operator ==(ResourceKey left, ResourceKey right) => left.Equals(right);

        public static bool operator !=(ResourceKey left, ResourceKey right) => !left.Equals(right);
    }
}
