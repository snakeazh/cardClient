using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Home page view. Nodes are resolved via UIReference / UIBind.
    /// </summary>
    [AutoScreen(AppScreenIds.Home, UILayer.Page, ResResourcePaths.Home)]
    public sealed class HomeView : ViewBase<HomeViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindText(UI.GetGameObject("LastStageInfo").GetComponent<TMP_Text>(), ViewModel.LastStageInfo);
            Binding.BindCommand(UI.GetGameObject("startBtn").GetComponent<Button>(), ViewModel.StartCommand);
        }
    }
}
