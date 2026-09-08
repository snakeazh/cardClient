using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    [AutoScreen(AppScreenIds.GameResource, UILayer.Resource, ResResourcePaths.GameResource)]
    public sealed class GameResourceView : ViewBase<GameResourceViewModel>
    {
        /// <summary>Common 目录不打图集，图标在 Inspector 手动引用。</summary>
        [SerializeField] private Sprite _goldIcon;

        public static GameResourceView FindOpen()
        {
            return FindObjectOfType<GameResourceView>();
        }

        protected override void OnBind()
        {
            ViewModel.SetIcon(_goldIcon);
            GameResourceBarBinder.Bind(
                Binding,
                transform,
                ViewModel.GoldSlot,
                showBack: true,
                ViewModel.BackCommand,
                ViewModel.ShowBackBtn);
        }
    }
}
