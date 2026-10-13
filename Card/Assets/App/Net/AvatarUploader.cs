using System;
using System.Threading.Tasks;
using Framework.Log;
using UnityEngine.Networking;

namespace App.Net
{
    /// <summary>
    /// 登录完成后把第三方平台头像转存到服务端 COS：主档里的头像若是平台 CDN 地址，
    /// 下载后经 POST /v1/player/avatar 换成自有 COS 永久链接。
    /// 已是自有 COS 地址（.myqcloud.com）直接跳过，天然幂等。失败只记日志，不影响登录。
    /// </summary>
    public static class AvatarUploader
    {
        /// <summary>后台触发一次同步（fire-and-forget）。传入登录后主档里的 avatarUrl。</summary>
        public static void KickOff(string profileAvatarUrl)
        {
            _ = SyncAvatarAsync(profileAvatarUrl);
        }

        private static async Task SyncAvatarAsync(string sourceUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceUrl))
                {
                    return;
                }

                if (sourceUrl.Contains(".myqcloud.com"))
                {
                    return;
                }

                byte[] data;
                string contentType;
                using (var get = UnityWebRequest.Get(sourceUrl))
                {
                    get.timeout = 15;
                    var op = get.SendWebRequest();
                    while (!op.isDone)
                    {
                        await Task.Yield();
                    }

                    if (get.result != UnityWebRequest.Result.Success)
                    {
                        AppLog.Warn(LogChannel.Net, "avatar download failed: " + get.error);
                        return;
                    }

                    data = get.downloadHandler != null ? get.downloadHandler.data : null;
                    contentType = get.GetResponseHeader("Content-Type") ?? string.Empty;
                }

                if (data == null || data.Length == 0)
                {
                    return;
                }

                var ext = contentType.Contains("png") ? "png" : "jpg";
                var profile = await GameApi.Client.UploadAvatarAsync(data, "avatar." + ext);
                if (profile != null)
                {
                    GameApi.ApplyProfile(profile);
                    AppLog.Info(LogChannel.Net, "avatar synced to cos: " + profile.AvatarUrl);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "avatar sync failed: " + ex.Message);
            }
        }
    }
}
