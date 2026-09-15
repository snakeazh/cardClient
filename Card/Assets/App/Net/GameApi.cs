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

        public static void Bind(GameApiClient client)
        {
            Client = client;
        }

        public static void ApplyProfile(PlayerProfileDto profile)
        {
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
