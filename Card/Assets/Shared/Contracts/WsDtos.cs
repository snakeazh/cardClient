#nullable enable

namespace CardShare.Contracts
{
    public sealed class WsEnvelope
    {
        public string T { get; set; } = string.Empty;

        public long Seq { get; set; }

        public object? Payload { get; set; }
    }

    public static class WsMessageTypes
    {
        public const string Hello = "hello";
        public const string Auth = "auth";
        public const string Authed = "authed";
        public const string Queue = "queue";
        public const string Queued = "queued";
        public const string QueueUpdate = "queue_update";
        public const string Cancel = "cancel";
        public const string RoomReady = "room_ready";
        public const string Ping = "ping";
        public const string Pong = "pong";
        public const string Leave = "leave";
        public const string Error = "error";
        public const string Battle = "battle";
        public const string BattleUpdate = "battle_update";
        public const string MatchUpdate = "match_update";
        public const string MatchEvent = "match_event";
        public const string Sync = "sync";
        public const string QueueTimeout = "queue_timeout";
    }

    public sealed class WsAuthPayload
    {
        public string AccessToken { get; set; } = string.Empty;
    }

    public sealed class WsQueueRosterPayload
    {
        public PlayerPublic[] Players { get; set; } = System.Array.Empty<PlayerPublic>();

        /// <summary>排队超时时长（毫秒），仅 queued 回包带；0 = 不超时。</summary>
        public long TimeoutMs { get; set; }
    }

    public sealed class WsSyncPayload
    {
        /// <summary>要同步的房间 Id；空则按 userId 查当前对局。</summary>
        public string RoomId { get; set; } = string.Empty;
    }

    public sealed class WsRoomReadyPayload
    {
        public string RoomId { get; set; } = string.Empty;

        public int Seed { get; set; }

        public int ModeId { get; set; }

        public PlayerPublic[] Players { get; set; } = System.Array.Empty<PlayerPublic>();
    }
}
