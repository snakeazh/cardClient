using Framework.UI.Core;
using UnityEngine;

namespace App.UI
{
    public enum ResourceKind
    {
        Gold = 0,
        Energy = 1
    }

    public sealed class ResourceSlot
    {
        public ResourceSlot(ResourceKind kind)
        {
            Kind = kind;
            Amount = new ObservableProperty<string>("0");
            Icon = new ObservableProperty<Sprite>();
            Visible = new ObservableProperty<bool>(true);
        }

        public ResourceKind Kind { get; }

        public ObservableProperty<string> Amount { get; }

        public ObservableProperty<Sprite> Icon { get; }

        public ObservableProperty<bool> Visible { get; }
    }
}
