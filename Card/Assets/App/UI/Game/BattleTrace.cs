using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 局内启动链路诊断。用 LogWarning，微信开发者工具默认能看到黄字。
    /// 搜前缀 [BattleTrace]。
    /// </summary>
    public static class BattleTrace
    {
        public static void Log(string step)
        {
            Debug.LogWarning("[BattleTrace] " + step);
        }
    }
}
