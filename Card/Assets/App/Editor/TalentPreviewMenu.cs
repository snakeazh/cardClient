using App.Bootstrap;
using App.UI;
using UnityEditor;
using UnityEngine;

namespace App.Editor
{
    /// <summary>Play 模式下快捷键直达天赋页，供 UI 走查（Ctrl+Alt+T）。</summary>
    internal static class TalentPreviewMenu
    {
        [MenuItem("Tools/打开天赋页 %&t")]
        public static void OpenTalent()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[TalentPreview] 需在 Play 模式下使用");
                return;
            }

            if (!AppServices.IsReady)
            {
                Debug.LogWarning("[TalentPreview] AppServices 未就绪");
                return;
            }

            AppServices.Resolve<NavigationViewModel>().ShowTalent();
        }
    }
}
