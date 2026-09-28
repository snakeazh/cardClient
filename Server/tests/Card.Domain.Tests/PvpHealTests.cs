using CardShare.Battle;
using CardShare.Contracts;
using Xunit;

namespace CardShare.Domain.Tests;

/// <summary>PVP 治疗结算：消耗品 use、回合回血、胜利回血、免伤与商店过滤。
/// 口径对齐 PVE GameSession（HealPlayer / ApplyRelicMaxHpDelta）。</summary>
public class PvpHealTests
{
    [Fact]
    public void UseHealPotionHealsPercentAndConsumes()
    {
        var tables = PvpTestTables.WithHealRelics();
        var match = Open(tables, hp: 100);
        var a = match.Fighters[0];

        match.Act(a.UserId, PvpActions.DebugGrant, 119, Array.Empty<int>());
        a.Hp = 40;

        match.Act(a.UserId, PvpActions.Use, 119, Array.Empty<int>());

        Assert.Equal(60, a.Hp);
        Assert.DoesNotContain(119, a.OwnedRelicIds);
        Assert.Equal(1, a.ConsumableUsesThisRun);
    }

    [Fact]
    public void RoundStartHealsBandageAndGrowsEternalHeart()
    {
        var tables = PvpTestTables.WithHealRelics();
        var match = Open(tables, hp: 100);
        var a = match.Fighters[0];

        match.Act(a.UserId, PvpActions.DebugGrant, 113, Array.Empty<int>());
        match.Act(a.UserId, PvpActions.DebugGrant, 222, Array.Empty<int>());
        a.Hp = 50;

        PvpRelicRuntime.ApplyRoundStart(tables, a);

        // 绷带 +2；永恒之心（EveryRoundGetHpMax +1）上限与当前血同步 +1。
        Assert.Equal(53, a.Hp);
        Assert.Equal(101, a.MaxHp);
    }

    [Fact]
    public void WinRewardsHealMoonshineAndGrowBadge()
    {
        var tables = PvpTestTables.WithHealRelics();
        var match = Open(tables, hp: 100);
        var a = match.Fighters[0];

        match.Act(a.UserId, PvpActions.DebugGrant, 229, Array.Empty<int>());
        match.Act(a.UserId, PvpActions.DebugGrant, 220, Array.Empty<int>());
        a.Hp = 50;

        PvpRelicRuntime.ApplyWinRewards(tables, a);

        // 月光酒 +3；激励徽章（DefeatGetHpMax +3）上限与当前血同步 +3。
        Assert.Equal(56, a.Hp);
        Assert.Equal(103, a.MaxHp);
    }

    [Fact]
    public void PhoenixFeatherNullifiesOnceAndClearsOnSettle()
    {
        var tables = PvpTestTables.WithHealRelics();
        var match = Open(tables, hp: 100);
        var a = match.Fighters[0];

        match.Act(a.UserId, PvpActions.DebugGrant, 425, Array.Empty<int>());
        a.Hp = 50;
        match.Act(a.UserId, PvpActions.Use, 425, Array.Empty<int>());

        Assert.True(a.NullifyDamageNextHit);
        Assert.Equal(80, a.Hp);
        Assert.True(match.ViewFor(a.UserId).Players[0].NullifyDamage);

        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        // 本桌比牌结算即消费：无论胜负，标记清掉。
        Assert.False(a.NullifyDamageNextHit);
    }

    [Fact]
    public void BuyMaxHpRelicRaisesMaxHpAndHp()
    {
        var tables = PvpTestTables.WithHealRelics();
        var match = Open(tables, shopPool: new[] { 210 }, hp: 100);
        var a = match.Fighters[0];

        EnterShop(match);
        var hpBefore = a.Hp;
        var maxBefore = a.MaxHp;

        match.Act(a.UserId, PvpActions.Buy, 210, Array.Empty<int>());

        Assert.Equal(maxBefore + 30, a.MaxHp);
        Assert.Equal(hpBefore + 30, a.Hp);
        Assert.Contains(210, a.OwnedRelicIds);
    }

    [Fact]
    public void ShopSkipsPveOnlyRelics()
    {
        var tables = PvpTestTables.WithHealRelics();
        var match = Open(tables, shopPool: new[] { 999, 113 });

        EnterShop(match);
        var a = match.Fighters[0];

        Assert.Contains(113, a.ShopOfferIds);
        Assert.DoesNotContain(999, a.ShopOfferIds);
    }

    [Fact]
    public void UseRejectsNotOwnedAndUnsupported()
    {
        var tables = PvpTestTables.WithHealRelics();
        var match = Open(tables, hp: 100);
        var a = match.Fighters[0];

        Assert.Throws<InvalidOperationException>(() => match.Act(a.UserId, PvpActions.Use, 119, Array.Empty<int>()));

        match.Act(a.UserId, PvpActions.DebugGrant, 999, Array.Empty<int>());
        Assert.Throws<InvalidOperationException>(() => match.Act(a.UserId, PvpActions.Use, 999, Array.Empty<int>()));
        Assert.Contains(999, a.OwnedRelicIds);
    }

    private static void EnterShop(PvpMatch match)
    {
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        match.ApplyTimeouts(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);
    }

    private static PvpMatch Open(GameTables tables, int hp = 0, int[]? shopPool = null)
    {
        var pubs = new[] { Pub("甲"), Pub("乙"), Pub("丙"), Pub("丁") };
        var seats = new SeatSetup[pubs.Length];
        for (var i = 0; i < pubs.Length; i++)
        {
            seats[i] = CombatBonuses.BuildSeat(i, pubs[i].UserId, pubs[i].NickName, 1, Array.Empty<CombatTalentCount>(), tables);
            seats[i].ShopPoolIds = shopPool ?? Array.Empty<int>();
            if (hp > 0)
            {
                seats[i].Hp = hp;
                seats[i].MaxHp = hp;
            }
        }

        return PvpMatch.Open(Guid.NewGuid(), 42, pubs, tables, seats, 1);
    }

    private static PlayerPublic Pub(string name)
        => new() { UserId = Guid.NewGuid().ToString(), NickName = name };
}
