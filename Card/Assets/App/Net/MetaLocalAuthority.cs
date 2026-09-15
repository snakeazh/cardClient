using Framework.Log;

namespace App.Net
{
    /// <summary>
    /// 局外主档联网后，本地服务不得自行改写权威字段；只允许 ReplaceFromServer / 软偏好（如 Last*）。
    /// </summary>
    public static class MetaLocalAuthority
    {
        public static bool AllowsLocalMutation(string service)
        {
            if (!GameApi.IsReady)
            {
                return true;
            }

            AppLog.Warn(LogChannel.Net, service + " refused local mutation while server session is ready.");
            return false;
        }
    }
}
