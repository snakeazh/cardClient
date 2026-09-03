using App.Bootstrap;
using App.UI;
using App.UI.Popup;
using Framework.UI;
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

        /// <summary>Play 模式下快捷键直达图鉴页，供 UI 走查（Ctrl+Alt+B）。</summary>
        [MenuItem("Tools/打开图鉴页 %&b")]
        public static void OpenIllustratedBook()
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

            AppServices.Resolve<NavigationViewModel>().ShowIllustratedBook();
        }

        /// <summary>Play 模式下快捷键打开关卡结算弹窗，供 UI 走查（Ctrl+Alt+S）。</summary>
        [MenuItem("Tools/打开结算弹窗 %&s")]
        public static void OpenBattleSettleUp()
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

            var ui = AppServices.Resolve<IUIManager>();
            var registration = ui.Registry.GetByViewModelType(typeof(BattleSettleUpPopViewModel));
            var vm = (BattleSettleUpPopViewModel)ui.Registry.CreateViewModel(registration);
            _ = ui.Open(vm);
        }

        [MenuItem("Tools/打开天赋规则 %&r")]
        public static void OpenTalentRules()
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

            var ui = AppServices.Resolve<IUIManager>();
            var registration = ui.Registry.GetByViewModelType(typeof(TalentRulesPopViewModel));
            var vm = (TalentRulesPopViewModel)ui.Registry.CreateViewModel(registration);
            _ = ui.Open(vm);
        }

        /// <summary>模拟点击天赋页 DetailBtn（Ctrl+Alt+D），验证规则弹窗入口。</summary>
        [MenuItem("Tools/模拟点DetailBtn %&d")]
        public static void PressDetailBtn()
        {
            PressNamedButton("DetailBtn");
        }

        /// <summary>模拟点击规则弹窗 Mask（Ctrl+Alt+M），验证点击关闭。</summary>
        [MenuItem("Tools/模拟点Mask %&m")]
        public static void PressMask()
        {
            PressNamedButton("Mask");
        }

        private static void PressNamedButton(string buttonName)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[TalentPreview] 需在 Play 模式下使用");
                return;
            }

            var buttons = Object.FindObjectsOfType<UnityEngine.UI.Button>();
            foreach (var button in buttons)
            {
                if (button.gameObject.name != buttonName)
                {
                    continue;
                }

                Debug.Log($"[TalentPreview] invoke {buttonName}");
                button.onClick.Invoke();
                return;
            }

            Debug.LogWarning($"[TalentPreview] 场景中未找到 {buttonName}");
        }
    }
}
