using UnityEngine;

namespace App.Build
{
    /// <summary>
    /// 防止 WebGL Strip Engine Code 裁掉 AB 里会用到的引擎类型（如 AudioListener=81）。
    /// 主场景 Camera 已挂 AudioListener；此处再做静态引用兜底。
    /// </summary>
    internal static class EngineCodePreserve
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Preserve()
        {
            _ = typeof(AudioListener);
            _ = typeof(AudioSource);
            _ = typeof(AudioClip);
        }
    }
}
