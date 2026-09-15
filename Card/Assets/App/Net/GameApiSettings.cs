namespace App.Net
{
    /// <summary>局外 HTTP 短链地址。Editor / 本机默认连 Development 服务。</summary>
    public static class GameApiSettings
    {
        public const string DefaultBaseUrl = "http://localhost:5254";

        public static string BaseUrl { get; set; } = DefaultBaseUrl;
    }
}
