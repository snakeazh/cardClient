using App.Bootstrap;
using App.UI.Popup;
using Framework.UI;
using UnityEditor;
using UnityEngine;

namespace App.UI.Editor
{
    /// <summary>
    /// 临时调试入口：底栏接入前用于验收天赋弹窗，MainInterfaceBottom 接好后删除。
    /// </summary>
    public static class TalentPopupDebugMenu
    {
        [MenuItem("Debug/UI/打开天赋弹窗", false, 0)]
        public static void OpenTalentPopup()
        {
            if (!Application.isPlaying || !AppServices.IsReady)
            {
                Debug.LogWarning("需要在 Play 模式且启动流程完成后使用");
                return;
            }

            var ui = AppServices.Resolve<IUIManager>();
            var registration = ui.Registry.GetByViewModelType(typeof(TalentPopupViewModel));
            var vm = (TalentPopupViewModel)ui.Registry.CreateViewModel(registration);
            _ = ui.Open(vm);
        }
    }
}
