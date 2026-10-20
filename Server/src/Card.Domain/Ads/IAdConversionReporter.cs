namespace CardShare.Domain.Ads;

/// <summary>巨量转化回传：广告观看完成后按"付费"事件回传。fire-and-forget，实现内全 catch，永不影响发奖。</summary>
public interface IAdConversionReporter
{
    /// <param name="attribution">玩家归因快照（无 callback 时实现直接跳过）。</param>
    /// <param name="scene">回传场景，仅用于日志，如 energy_refill_ad / adshop_gold。</param>
    void ReportAdWatched(AdAttributionState attribution, string scene);
}
