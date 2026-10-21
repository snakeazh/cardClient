namespace CardShare.Domain.Ads;

/// <summary>巨量广告点击归因：callback 随广告点击进入小游戏时由客户端上报，转化回传时使用。</summary>
public sealed class AdAttributionState
{
    /// <summary>巨量点击监测 callback，未知为空串。</summary>
    public string Callback { get; set; } = string.Empty;

    /// <summary>设备 OS：0=Android，1=iOS；-1=未知（不回传）。</summary>
    public int Os { get; set; } = -1;

    public DateTimeOffset ReceivedAtUtc { get; set; }

    public bool HasCallback => !string.IsNullOrWhiteSpace(Callback);

    /// <summary>复制快照，避免玩家档后续变更影响在途回传。</summary>
    public AdAttributionState Snapshot()
    {
        return new AdAttributionState
        {
            Callback = Callback,
            Os = Os,
            ReceivedAtUtc = ReceivedAtUtc
        };
    }
}
