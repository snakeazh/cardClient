using System;
using Framework.Log;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using WeChatWASM;
#endif

namespace App.Ad
{
    /// <summary>
    /// 巨量点击归因采集：广告点击进入小游戏时启动 query 携带 callback，
    /// 首次访问时捕获一次，登录时随 LoginRequest.AdCallback/AdOs 上报，服务端据此回传转化。
    /// </summary>
    public static class AdAttribution
    {
        /// <summary>设备 OS：Android=0，iOS=1，未知=-1（未知时服务端不回传）。</summary>
        public const int OsUnknown = -1;

        private static bool _captured;
        private static string _callback = string.Empty;
        private static int _os = OsUnknown;

        public static string Callback
        {
            get
            {
                EnsureCaptured();
                return _callback;
            }
        }

        public static int Os
        {
            get
            {
                EnsureCaptured();
                return _os;
            }
        }

        public static bool HasCallback => !string.IsNullOrEmpty(Callback);

        private static void EnsureCaptured()
        {
            if (_captured)
            {
                return;
            }

            _captured = true;
            _os = CurrentOs();
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                var options = WX.GetLaunchOptionsSync();
                var query = options != null ? options.query : null;
                if (query != null && query.TryGetValue("callback", out var callback) && !string.IsNullOrWhiteSpace(callback))
                {
                    _callback = callback.Trim();
                    AppLog.Info(LogChannel.Net, "Ad attribution callback captured.");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "Ad attribution capture failed: " + ex.Message);
            }
#endif
        }

        private static int CurrentOs()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                return 0;
            }

            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                return 1;
            }

            return OsUnknown;
        }
    }
}
