#nullable enable

namespace CardShare.Contracts
{
    public sealed class LoginUserInfo
    {
        public string? NickName { get; set; }

        public string? AvatarUrl { get; set; }
    }

    public sealed class LoginRequest
    {
        public string Provider { get; set; } = string.Empty;

        /// <summary>WeChat/Douyin js code, or guest device fingerprint.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Optional social profile from the client SDK. Not used for identity.</summary>
        public LoginUserInfo? UserInfo { get; set; }
        /// <summary>True when running inside the Unity Editor. Only guest login honors it.</summary>
        public bool Editor { get; set; }
        /// <summary>Optional leftover settle from the client. Applied before any forfeit of an active run.</summary>
        public PveSettleRequest? PendingSettle { get; set; }

        /// <summary>巨量点击监测 callback（广告进入时的启动 query），用于服务端转化回传归因。</summary>
        public string? AdCallback { get; set; }

        /// <summary>设备 OS：0=Android，1=iOS；-1=未知。与 AdCallback 配套。</summary>
        public int AdOs { get; set; } = -1;
    }

    public sealed class LoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;

        public int ExpiresInSeconds { get; set; }

        public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
    }
}
