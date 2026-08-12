using System;
using UnityEngine;

namespace Framework.UI.Navigation
{
    /// <summary>
    /// Marks a View as a UI screen so the framework can auto-register it via reflection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AutoScreenAttribute : Attribute
    {
        public AutoScreenAttribute(string screenId, UILayer layer)
        {
            if (string.IsNullOrWhiteSpace(screenId))
            {
                throw new ArgumentException("screenId cannot be empty.", nameof(screenId));
            }

            ScreenId = screenId;
            Layer = layer;
        }

        public string ScreenId { get; }

        public UILayer Layer { get; }
    }
}

