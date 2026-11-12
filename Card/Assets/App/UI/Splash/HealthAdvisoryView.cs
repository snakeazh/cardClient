using App.Resources;
using App.UI;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine.UI;

namespace App.UI.Splash
{
    /// <summary>
    /// 微信小游戏冷启动健康游戏忠告页。
    /// </summary>
    [AutoScreen(AppScreenIds.HealthAdvisory, UILayer.TopMost, ResResourcePaths.HealthAdvisory)]
    public sealed class HealthAdvisoryView : ViewBase<HealthAdvisoryViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindCommand(UI.GetGameObject("PlayBtn").GetComponent<Button>(), ViewModel.PlayCommand);
        }
    }
}
