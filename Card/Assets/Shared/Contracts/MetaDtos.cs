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

    public sealed class DebugGrantGoldRequest
    {
        public int Amount { get; set; }
    }
}
