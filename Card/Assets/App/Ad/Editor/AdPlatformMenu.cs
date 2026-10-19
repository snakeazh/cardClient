using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace App.Ad.Editor
{
    /// <summary>
    /// 一键切换 WebGL 出包平台宏（Build/平台）：抖音 DOUYINMINIGAME / 微信 WEIXINMINIGAME。
    /// 只改 WebGL 的 Scripting Define Symbols，平台宏互斥替换，其余宏（DOTWEEN、ENABLE_APP_LOG 等）保持不变；
    /// 切换后触发重编译，AdServiceFactory 按宏选择广告实现。
    /// </summary>
    public static class AdPlatformMenu
    {
        private const string DouyinDefine = "DOUYINMINIGAME";
        private const string WeChatDefine = "WEIXINMINIGAME";
        private const string WeChatLegacyDefine = "PLATFORM_WEIXINMINIGAME";
        private const string DouyinMenuPath = "Build/平台/抖音 (DOUYINMINIGAME)";
        private const string WeChatMenuPath = "Build/平台/微信 (WEIXINMINIGAME)";

        [MenuItem(DouyinMenuPath, false, 10)]
        private static void UseDouyin()
        {
            SwitchTo(DouyinDefine, "抖音真实激励视频，需先在 AdSettings 填广告位 ID");
        }

        [MenuItem(DouyinMenuPath, true)]
        private static bool UseDouyinValidate()
        {
            Menu.SetChecked(DouyinMenuPath, HasDefine(DouyinDefine));
            return true;
        }

        [MenuItem(WeChatMenuPath, false, 11)]
        private static void UseWeChat()
        {
            SwitchTo(WeChatDefine, "微信，广告暂未接入、走本地模拟");
        }

        [MenuItem(WeChatMenuPath, true)]
        private static bool UseWeChatValidate()
        {
            Menu.SetChecked(WeChatMenuPath, HasDefine(WeChatDefine) || HasDefine(WeChatLegacyDefine));
            return true;
        }

        private static void SwitchTo(string define, string note)
        {
            var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL);
            var kept = new List<string>();
            var hasTarget = false;
            var hasOtherPlatform = false;
            foreach (var raw in symbols.Split(';'))
            {
                var symbol = raw.Trim();
                if (symbol.Length == 0)
                {
                    continue;
                }

                if (symbol == define)
                {
                    hasTarget = true;
                    continue;
                }

                if (symbol == DouyinDefine || symbol == WeChatDefine || symbol == WeChatLegacyDefine)
                {
                    hasOtherPlatform = true;
                    continue;
                }

                kept.Add(symbol);
            }

            if (hasTarget && !hasOtherPlatform)
            {
                Debug.Log($"[AdPlatform] WebGL 平台宏已是 {define}，无需切换");
                return;
            }

            kept.Add(define);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL, string.Join(";", kept));
            Debug.Log($"[AdPlatform] WebGL 平台宏已切换为 {define}（{note}）");
        }

        private static bool HasDefine(string define)
        {
            var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL);
            foreach (var raw in symbols.Split(';'))
            {
                if (raw.Trim() == define)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
