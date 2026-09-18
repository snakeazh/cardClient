using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 局内启动链路诊断。用 LogWarning，微信开发者工具默认能看到黄字。
    /// 搜前缀 [BattleTrace]。默认关闭；需要排查时在 PlayerSettings 的
    /// Scripting Define Symbols 里加 ENABLE_BATTLE_TRACE 即可重新打开。
    /// </summary>
    public static class BattleTrace
    {
        [System.Diagnostics.Conditional("ENABLE_BATTLE_TRACE")]
        public static void Log(string step)
        {
            Debug.LogWarning("[BattleTrace] " + step);
        }
    }
}
