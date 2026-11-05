#nullable enable

namespace CardShare.Contracts
{
    public sealed class TalentDrawResponse
    {
        public int TalentId { get; set; }

        public int Count { get; set; }

        public int DrawCount { get; set; }

        public int GoldSpent { get; set; }

        public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
    }

    public sealed class AdProofRequest
    {
        /// <summary>Reserved for a real ad SDK receipt. Ignored in this phase.</summary>
        public string? AdProof { get; set; }
    }

    public sealed class EnergyRefillResponse
    {
        public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
    }

    public sealed class AdShopClaimRequest
    {
        /// <summary>stamina or gold</summary>
        public string Kind { get; set; } = string.Empty;

        public string? AdProof { get; set; }
    }

    public sealed class AdShopClaimResponse
    {
        public PlayerProfileDto Profile { get; set; } = new PlayerProfileDto();
    }

    public sealed class BagMutateRequest
    {
        public int ItemId { get; set; }

        public int Amount { get; set; }
    }

    public sealed class GuideCompleteRequest
    {
        public int GroupId { get; set; }
    }

    public sealed class HeroUnlockRequest
    {
        public int HeroId { get; set; }
    }

    public sealed class DebugGrantGoldRequest
    {
        public int Amount { get; set; }
    }

    /// <summary>后台 GM 发放货币：指定目标玩家 + 数量。UserId 接受任意 GUID 文本格式（含无横线的 N 格式）。</summary>
    public sealed class AdminGrantRequest
    {
        public string UserId { get; set; } = string.Empty;

        public int Amount { get; set; }
    }

    /// <summary>后台 GM 无参数命令（解锁全部英雄/天赋/难度）：只需目标玩家。</summary>
    public sealed class AdminTargetRequest
    {
        public string UserId { get; set; } = string.Empty;
    }

    /// <summary>后台登录：单密码（即配置的 Admin:Token），校验通过原样返回，供后续请求放 X-Admin-Token。</summary>
    public sealed class AdminLoginRequest
    {
        public string Password { get; set; } = string.Empty;
    }

    public sealed class AdminLoginResponse
    {
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>客户端看完一次广告后的场景上报：服务端按场景计数，并对有 clickid 归因的玩家回传巨量关键行为。</summary>
    public sealed class AdWatchReportRequest
    {
        /// <summary>场景名：小写字母/数字/下划线，≤32 字符，如 revive / double_gold。</summary>
        public string Scene { get; set; } = string.Empty;
    }
}
