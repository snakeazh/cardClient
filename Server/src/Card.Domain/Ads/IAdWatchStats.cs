namespace CardShare.Domain.Ads;

/// <summary>广告观看按场景计数（自建统计，与巨量回传独立）。fire-and-forget，实现内全 catch。</summary>
public interface IAdWatchStats
{
    /// <summary>指定场景计数 +1。</summary>
    void RecordWatch(string scene);
}
