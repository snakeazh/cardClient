using App.Resources;
using App.UI;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;

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
            Binding.BindText(GetNode<TMP_Text>("Title"), ViewModel.Title);
            Binding.BindText(GetNode<TMP_Text>("Body"), ViewModel.Body);
            Binding.BindText(GetNode<TMP_Text>("Footer"), ViewModel.Footer);
        }

        private T GetNode<T>(string key) where T : UnityEngine.Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }
    }
}
