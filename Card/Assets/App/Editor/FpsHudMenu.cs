using App.UI;
using UnityEditor;
using UnityEngine;

namespace App.Editor
{
    /// <summary>
    /// 帧率 HUD 开关（Debug/性能）。Play 模式内立即生效，并写 PlayerPrefs 持久
    /// （AppBootstrap 启动时按 FpsHud.PrefKey 自动恢复）。注意：微信真机读不到
    /// Editor 的 PlayerPrefs，真机常驻需走配置表开关。
    /// </summary>
    public static class FpsHudMenu
    {
        private const string MenuPath = "Debug/性能/帧率HUD";

        [MenuItem(MenuPath, false, 0)]
        public static void Toggle()
        {
            if (FpsHud.IsShown)
            {
                FpsHud.Hide();
                PlayerPrefs.SetInt(FpsHud.PrefKey, 0);
            }
            else
            {
                FpsHud.Show();
                PlayerPrefs.SetInt(FpsHud.PrefKey, 1);
            }

            PlayerPrefs.Save();
        }

        [MenuItem(MenuPath, true)]
        public static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, FpsHud.IsShown);
            return Application.isPlaying;
        }
    }
}
