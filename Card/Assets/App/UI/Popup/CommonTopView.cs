using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 局内退出确认。节点通过 UIReference 解析：tipContext / yes / no。
    /// UIBind Target 可能是 CanvasRenderer，统一走 GameObject 再取组件。
    /// </summary>
    [AutoScreen(AppScreenIds.CommonTop, UILayer.Popup, ResResourcePaths.CommonTop)]
    public sealed class CommonTopView : ViewBase<CommonTopViewModel>
    {
        protected override void OnBind()
        {
            var tip = GetNode<TMP_Text>("tipContext");
            if (tip != null)
            {
                Binding.BindText(tip, ViewModel.TipContext);
            }

            var yes = GetNode<Button>("yes");
            if (yes != null)
            {
                Binding.BindCommand(yes, ViewModel.YesCommand);
            }

            var no = GetNode<Button>("no");
            if (no != null)
            {
                Binding.BindCommand(no, ViewModel.NoCommand);
            }
        }

        private T GetNode<T>(string key) where T : Component
        {
            var go = UI.GetGameObject(key);
            var component = go.GetComponent<T>();
            return component != null ? component : go.GetComponentInChildren<T>(true);
        }
    }
}
