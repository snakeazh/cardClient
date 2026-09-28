using System;

namespace App.Net
{
    /// <summary>局外 HTTP 短链地址。Editor / 本机默认连 Development 服务。</summary>
    public static class GameApiSettings
    {
        // 本地调试时换用 127.0.0.1（Server/start-local.bat 起内存模式服务）：
        // public const string DefaultBaseUrl = "http://127.0.0.1:5254";
        public const string DefaultBaseUrl = "http://121.43.167.98:5254";
        public static string BaseUrl { get; set; } = DefaultBaseUrl;

        /// <summary>
        /// 是否连接服务器。关闭后启动不登录、闯关不建服务端 run，走本地局。
        /// 由 AppBootstrap 序列化字段在启动时写入。
        /// </summary>
        public static bool Enabled { get; set; } = true;

        public static string WebSocketUrl
        {
            get
            {
                var http = (BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
                if (http.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return "wss://" + http.Substring("https://".Length) + "/v1/pvp/ws";
                }

                if (http.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    return "ws://" + http.Substring("http://".Length) + "/v1/pvp/ws";
                }

                return "ws://" + http + "/v1/pvp/ws";
            }
        }
    }
}
