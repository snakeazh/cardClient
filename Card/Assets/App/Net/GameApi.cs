using App.Bootstrap;
using CardShare.Contracts;
using Framework.Log;

namespace App.Net
{
    /// <summary>局外 HTTP 与灌档的静态入口。</summary>
    public static class GameApi
    {
        public static GameApiClient Client { get; private set; }

        public static bool IsReady => Client != null && Client.HasSession;

        /// <summary>最近一次服务器下发的昵称/头像（暂存，供后续头像 UI 消费）。</summary>
        public static string NickName { get; private set; } = string.Empty;

        public static string AvatarUrl { get; private set; } = string.Empty;

        public static void Bind(GameApiClient client)
        {
            Client = client;
        }

        public static void ApplyProfile(PlayerProfileDto profile)
        {
            NickName = profile != null ? profile.NickName ?? string.Empty : string.Empty;
            AvatarUrl = profile != null ? profile.AvatarUrl ?? string.Empty : string.Empty;
            ServerProfileApplier.Apply(profile);
        }

        public static string Describe(GameApiException ex)
        {
            if (ex == null)
            {
                return "请求失败";
            }

            switch (ex.Code)
            {
                case ErrorCodes.InsufficientEnergy:
                    return "体力不足";
                case ErrorCodes.InsufficientGold:
                    return "金币不足";
                case ErrorCodes.AdLimitReached:
                    return "今日次数已用完";
                case ErrorCodes.TalentPoolEmpty:
                    return "天赋已全部满级";
                case ErrorCodes.LevelLocked:
                    return "关卡未解锁";
                case ErrorCodes.HeroLocked:
                    return "英雄未解锁";
                case ErrorCodes.RunNotFound:
                    return "对局已失效";
                case PveSessionGate.ActiveRunExists:
                    return "已有未结束的闯关";
                case "connection_error":
                    return "无法连接服务器";
                default:
                    return string.IsNullOrEmpty(ex.Message) ? "请求失败" : ex.Message;
            }
        }

        public static void Warn(string message)
        {
            AppLog.Warn(LogChannel.Net, message);
        }
    }
}
