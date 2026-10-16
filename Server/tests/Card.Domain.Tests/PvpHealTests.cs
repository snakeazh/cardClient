using System;
using System.Linq;
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

    [Fact]
    public void BotPickHealConsumableOnlyWhenDamaged()
    {
        var tables = PvpTestTables.WithHealRelics();
        var fighter = new PvpFighter
        {
            Alive = true,
            Hp = 100,
            MaxHp = 100,
            IsBot = true
        };
        fighter.OwnedRelicIds.Add(119);
        fighter.OwnedRelicPower.Add(1);

        Assert.True(PvpRelicRuntime.ConsumableHeals(tables, tables.Relics.First(r => r.Id == 119)));
        Assert.Null(PvpBotConsumables.PickToUse(fighter, tables));

        fighter.Hp = 40;
        Assert.Equal(119, PvpBotConsumables.PickToUse(fighter, tables));
    }

    [Fact]
    public void BotUsesHealConsumableAtRoundStartWhenDamaged()
    {
        var tables = PvpTestTables.WithHealRelics();
        var bot = new PlayerPublic
        {
            UserId = Guid.NewGuid().ToString("N"),
            NickName = "机",
            IsBot = true
        };
        var pubs = new[]
        {
            new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = "你" },
            bot,
            new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = "乙", IsBot = true },
            new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = "丙", IsBot = true }
        };
        var match = Open(tables, pubs, hp: 100, shopPool: new[] { 1, 2 });
        var botFighter = match.Fighters.First(f => f.NickName == "机");

        // 第一轮先打完。受伤和治疗消耗品放在进店之后，避免这一轮的伤害覆盖掉脚本血量。
        EnterShop(match);
        botFighter.OwnedRelicIds.Add(119);
        botFighter.OwnedRelicPower.Add(1);
        botFighter.Hp = 40;
        foreach (var fighter in match.Fighters)
        {
            if (!fighter.ShopDone)
            {
                match.Act(fighter.UserId, PvpActions.ShopDone, 0, Array.Empty<int>());
            }
        }

        Assert.Equal(2, match.Round);
        Assert.DoesNotContain(119, botFighter.OwnedRelicIds);
        Assert.Equal(60, botFighter.Hp);
        Assert.Equal(1, botFighter.ConsumableUsesThisRun);
    }

    private static void EnterShop(PvpMatch match)
    {
        foreach (var fighter in match.Fighters)
        {
            if (match.Phase != PvpMatch.PhaseFight)
            {
                break;
            }

            match.Showdown(fighter.UserId);
        }

        if (match.Phase == PvpMatch.PhaseSettle)
        {
            match.ApplyTimeouts(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PvpTiming.SettleAnimMs + 1);
        }

        Assert.Equal(PvpMatch.PhaseShop, match.Phase);
    }

    private static PvpMatch Open(GameTables tables, int hp = 0, int[]? shopPool = null)
        => Open(tables, new[] { Pub("甲"), Pub("乙"), Pub("丙"), Pub("丁") }, hp, shopPool);

    private static PvpMatch Open(GameTables tables, PlayerPublic[] pubs, int hp = 0, int[]? shopPool = null)
    {
        var seats = new SeatSetup[pubs.Length];
        for (var i = 0; i < pubs.Length; i++)
        {
            seats[i] = CombatBonuses.BuildSeat(i, pubs[i].UserId, pubs[i].NickName, 1, Array.Empty<CombatTalentCount>(), tables);
            seats[i].IsHuman = !pubs[i].IsBot;
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
        => new() { UserId = Guid.NewGuid().ToString("N"), NickName = name };
}
