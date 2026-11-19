#nullable enable

using System;

namespace CardShare.Contracts
{
    public sealed class PvpLeaderboardResponse
    {
        public PvpLeaderboardEntry[] Entries { get; set; } = Array.Empty<PvpLeaderboardEntry>();

        public PvpLeaderboardEntry? Self { get; set; }
    }

    public sealed class PvpLeaderboardEntry
    {
        /// <summary>全服名次，从 1 开始，未上榜时为空。</summary>
        public long? Place { get; set; }

        public string NickName { get; set; } = string.Empty;

        public string AvatarUrl { get; set; } = string.Empty;

        public int Score { get; set; }
    }
}
