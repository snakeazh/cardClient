using System.Diagnostics;
using System.Text.RegularExpressions;
using Framework.Log;

namespace App.Net
{
    /// <summary>
    /// 网络报文日志：HTTP 与 WebSocket 共用一套格式（<c>http → / ←</c>、<c>ws → / ←</c>），
    /// 统一给 accessToken 打码、超长响应截断。只经 AppLog.Info 输出，Release 包整段编译掉。
    /// </summary>
    internal static class NetLog
    {
        /// <summary>单条报文的打印上限：够看清字段，又不至于把整包快照刷屏。</summary>
        public const int BodyLimit = 1500;

        private static readonly Regex AccessTokenPattern = new Regex(
            "(\"accessToken\"\\s*:\\s*\")[^\"]*(\")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ---- HTTP ----

        /// <summary>发请求前打印：方法 + 完整 URL + 入参。</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_APP_LOG")]
        public static void HttpRequest(string method, string url, string body)
        {
            AppLog.Info(LogChannel.Net, string.IsNullOrEmpty(body)
                ? $"http → {method} {url}"
                : $"http → {method} {url} body={Clip(body)}");
        }

        /// <summary>收到响应后打印：状态码 + 耗时 + 响应体（失败附 error）。</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_APP_LOG")]
        public static void HttpResponse(string method, string url, int status, long elapsedMs, string body, string error)
        {
            var line = $"http ← {status} {method} {url} ({elapsedMs}ms)";
            if (!string.IsNullOrWhiteSpace(body))
            {
                line += " " + Clip(body);
            }

            if (!string.IsNullOrEmpty(error))
            {
                line += " error=" + error;
            }

            AppLog.Info(LogChannel.Net, line);
        }

        // ---- WebSocket ----

        /// <summary>连接生命周期：connecting / connected / closed / error，带 ws 地址。</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_APP_LOG")]
        public static void WsEvent(string message)
        {
            AppLog.Info(LogChannel.Net, "ws " + message);
        }

        /// <summary>下发帧：按消息类型 <paramref name="type"/>（即服务端路由的 t）打头，后接原报文。</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_APP_LOG")]
        public static void WsSend(string type, string json)
        {
            AppLog.Info(LogChannel.Net, $"ws → {Label(type)} {Clip(json)}");
        }

        /// <summary>收到帧：同 <see cref="WsSend"/>，解析不出类型时标 (unknown)，报文照打。</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_APP_LOG")]
        public static void WsReceive(string type, string json)
        {
            AppLog.Info(LogChannel.Net, $"ws ← {Label(type)} {Clip(json)}");
        }

        private static string Label(string type)
        {
            return string.IsNullOrEmpty(type) ? "(unknown)" : type;
        }

        /// <summary>token 打码 + 超长截断。</summary>
        private static string Clip(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var masked = AccessTokenPattern.Replace(text, "$1***$2");
            return masked.Length <= BodyLimit
                ? masked
                : masked.Substring(0, BodyLimit) + $"...(共 {masked.Length} 字符)";
        }
    }
}
