#nullable enable

namespace CardShare.Contracts
{
    public sealed class PlayerPublic
    {
        public string UserId { get; set; } = string.Empty;

        public string NickName { get; set; } = string.Empty;

        public string AvatarUrl { get; set; } = string.Empty;

        public bool IsBot { get; set; }

        /// <summary>配表 PvpBotConfig.Id；非机器人或未知为 0。</summary>
        public int BotConfigId { get; set; }
    }
}
