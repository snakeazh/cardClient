using System;
using Framework.Log;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using WeChatWASM;
#endif

namespace App.Ad
{
    /// <summary>
    /// 巨量点击归因采集：广告点击进入小游戏时启动 query 携带 clickid（新版 clickid 归因链路，
    /// 兼容老监测链接的 callback），首次访问时捕获一次，登录时随 LoginRequest.AdCallback/AdOs 上报，
    /// 服务端据此回传转化。官方文档：https://event-manager.oceanengine.com/docs/8650/5bltdv1c
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
                if (query != null && TryReadClickId(query, out var clickId))
                {
                    _callback = clickId.Trim();
                    AppLog.Info(LogChannel.Net, "Ad attribution clickid captured.");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "Ad attribution capture failed: " + ex.Message);
            }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>clickid 归因链路优先读 clickid，兼容老监测链接下发的 callback。</summary>
        private static bool TryReadClickId(System.Collections.Generic.IDictionary<string, string> query, out string clickId)
        {
            if (query.TryGetValue("clickid", out clickId) && !string.IsNullOrWhiteSpace(clickId))
            {
                return true;
            }

            if (query.TryGetValue("callback", out clickId) && !string.IsNullOrWhiteSpace(clickId))
            {
                return true;
            }

            clickId = string.Empty;
            return false;
        }
#endif

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
