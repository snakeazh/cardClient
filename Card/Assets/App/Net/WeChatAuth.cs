using System;
using System.Threading.Tasks;
using CardShare.Contracts;
using Framework.Log;

namespace App.Net
{
    /// <summary>
    /// 微信平台登录封装：WX.Login 换 code；WX.GetUserInfo 拿昵称/头像上报。
    /// 新版微信基础库下 GetUserInfo 可能只返回默认灰头像和"微信用户"昵称，属预期；
    /// 用户拒绝授权时不阻塞登录（服务器保留旧资料）。
    /// </summary>
    public static class WeChatAuth
    {
        public const string LoginFailedCode = "wechat_login_failed";

        public static bool IsWeChatPlatform
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static Task<string> LoginAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<string>();
            WeChatWASM.WX.Login(new WeChatWASM.LoginOption
            {
                success = res =>
                {
                    if (res != null && !string.IsNullOrEmpty(res.code))
                    {
                        tcs.TrySetResult(res.code);
                    }
                    else
                    {
                        tcs.TrySetException(new GameApiException(LoginFailedCode, "微信登录失败：未取得 code"));
                    }
                },
                fail = res => tcs.TrySetException(new GameApiException(
                    LoginFailedCode,
                    "微信登录失败: " + (res != null ? res.errMsg : string.Empty)))
            });
            return tcs.Task;
#else
            return Task.FromException<string>(
                new GameApiException(LoginFailedCode, "当前平台不是微信小游戏"));
#endif
        }

        public static async Task<LoginUserInfo> GetUserInfoAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var info = await TryGetUserInfoAsync();
            if (info != null)
            {
                return info;
            }

            if (!await AuthorizeUserInfoAsync())
            {
                AppLog.Warn(LogChannel.Net, "wechat userInfo authorize denied, login without profile");
                return new LoginUserInfo();
            }

            info = await TryGetUserInfoAsync();
            if (info == null)
            {
                AppLog.Warn(LogChannel.Net, "wechat getUserInfo failed after authorize, login without profile");
                return new LoginUserInfo();
            }

            return info;
#else
            await Task.Yield();
            return new LoginUserInfo();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static Task<LoginUserInfo> TryGetUserInfoAsync()
        {
            var tcs = new TaskCompletionSource<LoginUserInfo>();
            WeChatWASM.WX.GetUserInfo(new WeChatWASM.GetUserInfoOption
            {
                success = res =>
                {
                    var user = res != null ? res.userInfo : null;
                    tcs.TrySetResult(new LoginUserInfo
                    {
                        NickName = user != null ? user.nickName ?? string.Empty : string.Empty,
                        AvatarUrl = user != null ? user.avatarUrl ?? string.Empty : string.Empty
                    });
                },
                fail = _ => tcs.TrySetResult(null)
            });
            return tcs.Task;
        }

        private static Task<bool> AuthorizeUserInfoAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            WeChatWASM.WX.Authorize(new WeChatWASM.AuthorizeOption
            {
                scope = "scope.userInfo",
                success = _ => tcs.TrySetResult(true),
                fail = _ => tcs.TrySetResult(false)
            });
            return tcs.Task;
        }
#endif
    }
}
