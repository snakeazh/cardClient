using System;

namespace Framework.UI.Navigation
{
    /// <summary>
    /// Marks a View as a UI screen so the framework can auto-register it via reflection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AutoScreenAttribute : Attribute
    {
        public AutoScreenAttribute(string screenId, UILayer layer, string assetKey)
        {
            if (string.IsNullOrWhiteSpace(screenId))
            {
                throw new ArgumentException("screenId cannot be empty.", nameof(screenId));
            }

            if (string.IsNullOrWhiteSpace(assetKey))
            {
                throw new ArgumentException("assetKey cannot be empty.", nameof(assetKey));
            }

            ScreenId = screenId;
            Layer = layer;
            AssetKey = assetKey;
        }

        public string ScreenId { get; }

        public UILayer Layer { get; }

        /// <summary>Resource key, e.g. values from ResResourcePaths.</summary>
        public string AssetKey { get; }
    }
}
