using Framework.UI.View;
using Framework.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Home page view. Nodes are resolved via UIReference / UIBind.
    /// </summary>
    [AutoScreen(AppScreenIds.Home, UILayer.Page)]
    public sealed class HomeView : ViewBase<HomeViewModel>
    {
        public const string ResourcesPath = App.Resources.ResResourcePaths.Home;

        protected override void OnBind()
        {
            Binding.BindText(UI.Get<TMP_Text>("Title"), ViewModel.Title);
            Binding.BindText(UI.Get<TMP_Text>("Status"), ViewModel.Status);
            Binding.BindCommand(UI.Get<Button>("DialogButton"), ViewModel.ShowDialogCommand);
        }
    }
}
