using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using PlayingCard = CardShare.Battle.Card;
using Xunit;

namespace CardShare.Domain.Tests;

/// <summary>PVP 英雄词条钩子（PvpHeroRuntime）落点：初始金币、商店折扣、承伤减免、闪避反击、吸血、追击、过关金币。</summary>
public class PvpHeroRuntimeTests
{
    [Fact]
    public void MerchantStartsWithInitialFundsBonus()
    {
        var match = Open(PvpTestTables.WithHeroEntries(), heroId: 3);
        Assert.All(match.Fighters, f => Assert.Equal(150 + 80, f.Gold));
    }

    [Fact]
    public void MerchantBuysAtDiscountAndShelfShowsSamePrice()
    {
        var match = Open(PvpTestTables.WithHeroEntries(), shopPool: new[] { 1, 2 }, heroId: 3);
        foreach (var fighter in match.Fighters)
        {
            match.Showdown(fighter.UserId);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        match.ApplyTimeouts(now + PvpTiming.SettleAnimMs + 1);
        Assert.Equal(PvpMatch.PhaseShop, match.Phase);

        var a = match.Fighters[0];
        var shop = match.ViewFor(a.UserId).Shop!;
        Assert.True(shop.OfferIds.Length > 0);
        // 货架投影价 = 服务端扣款价：round(Price × (1 - 0.2))，10 → 8 / 20 → 16。
        var relicId = shop.OfferIds[0];
        var expected = relicId == 1 ? 8 : 16;
        Assert.Equal(expected, shop.OfferPrices[0]);

        var gold = a.Gold;
        match.Act(a.UserId, "buy", relicId, Array.Empty<int>());
        Assert.Equal(gold - expected, a.Gold);
        Assert.Contains(relicId, a.OwnedRelicIds);
    }

    [Fact]
    public void VampireMitigatesIncomingDamage()
    {
        var match = Open(PvpTestTables.WithHeroEntries(pvpOnly: true), heroId: 2, hp: 1000);
        PvpMatchTests.AdvanceToPvpFight(match);
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));
        var hpA = a.Hp;
        var hpB = b.Hp;

        match.Showdown(a.UserId);
        match.Showdown(bId);

        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        var scaled = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * match.Round));
        var expected = Math.Max(0, (int)Math.Round(scaled * (1f - 0.25f)));
        if (expected > 0)
        {
            expected = Math.Max(1, expected);
        }

        var loser = PvpBattleTable.SameUser(LoserId(duel, snap.Winners[0]), a.UserId) ? a : b;
        var winner = ReferenceEquals(loser, a) ? b : a;
        Assert.Equal((ReferenceEquals(loser, a) ? hpA : hpB) - expected, loser.Hp);
        Assert.Equal(ReferenceEquals(winner, a) ? hpA : hpB, winner.Hp);
        Assert.Equal(expected, match.ViewFor(a.UserId).DuelDamage);
    }

    [Fact]
    public void MonkDodgesFullDamageAndCountersAttacker()
    {
        var match = Open(PvpTestTables.WithHeroEntries(pvpOnly: true), heroId: 4, hp: 1000);
        PvpMatchTests.AdvanceToPvpFight(match);
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));
        var hpA = a.Hp;
        var hpB = b.Hp;

        match.Showdown(a.UserId);
        match.Showdown(bId);

        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        Assert.True(snap.Damages[snap.Winners[0]] > 0);

        // 100% 闪避：败方不掉血；反击 = round(防守方攻击 10 × 2 × 1) = 20 打给胜方。
        var loser = PvpBattleTable.SameUser(LoserId(duel, snap.Winners[0]), a.UserId) ? a : b;
        var winner = ReferenceEquals(loser, a) ? b : a;
        Assert.Equal(ReferenceEquals(loser, a) ? hpA : hpB, loser.Hp);
        Assert.Equal((ReferenceEquals(winner, a) ? hpA : hpB) - 20, winner.Hp);
        Assert.Equal(0, match.ViewFor(a.UserId).DuelDamage);
    }

    [Fact]
    public void BerserkerLifestealsByActualDamageOnWin()
    {
        var match = Open(PvpTestTables.WithHeroEntries(pvpOnly: true), heroId: 5, hp: 100);
        PvpMatchTests.AdvanceToPvpFight(match);
        foreach (var fighter in match.Fighters)
        {
            fighter.Hp = 50;
        }

        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));

        match.Showdown(a.UserId);
        match.Showdown(bId);

        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        var scaled = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * match.Round));
        var dealt = Math.Min(50, scaled);
        var heal = Math.Max(0, (int)Math.Round(dealt * 0.5f));

        var loser = PvpBattleTable.SameUser(LoserId(duel, snap.Winners[0]), a.UserId) ? a : b;
        var winner = ReferenceEquals(loser, a) ? b : a;
        // 扣到 0 及以下判负并夹到 0（RankDead）。
        Assert.Equal(Math.Max(0, 50 - scaled), loser.Hp);
        Assert.Equal(Math.Min(100, 50 + heal), winner.Hp);
        Assert.True(heal > 0);
    }

    [Fact]
    public void TalentExtraAttackStaysInDamageFormula_HeroExtraAttackIsSeparate()
    {
        var tables = PvpTestTables.WithHeroEntries();
        HandEvaluator.Tables = tables;
        var score = HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Heart, Rank.Two),
            new PlayingCard(Suit.Spade, Rank.Three),
            new PlayingCard(Suit.Club, Rank.Four)
        });

        // 英雄 ExtraAttackOneTime 不进公式通道（由 ApplyDuelDamage 打第二刀）。
        var heroOnly = CombatBonuses.BuildSeat(0, "u", "n", 6, Array.Empty<CombatTalentCount>(), tables);
        var input = CombatBonuses.BuildPlayerInput(tables, heroOnly, score, new CombatSituation());
        Assert.Equal(0f, input.ExtraAttackChance, 4);
        Assert.True(tables.TryGetHero(6, out var shooter));
        Assert.Equal(0.35f, PvpHeroRuntime.ExtraAttackChance(tables, shooter!), 4);

        // 天赋 ProOfExtraAttack 仍走公式内 +10% 追击加伤。
        var talents = new[] { new CombatTalentCount { TalentId = 500, Count = 1 } };
        var combined = CombatBonuses.BuildSeat(0, "u", "n", 6, talents, tables);
        input = CombatBonuses.BuildPlayerInput(tables, combined, score, new CombatSituation());
        Assert.Equal(0.25f, input.ExtraAttackChance, 4);

        var noHero = CombatBonuses.BuildSeat(0, "u", "n", 1, Array.Empty<CombatTalentCount>(), tables);
        input = CombatBonuses.BuildPlayerInput(tables, noHero, score, new CombatSituation());
        Assert.Equal(0f, input.ExtraAttackChance, 4);
    }

    [Fact]
    public void PaladinOpeningHolyLightBurnsOpponentAndRaisesCasts()
    {
        var match = Open(PvpTestTables.WithPaladin(), heroId: 8, hp: 1000);
        PvpMatchTests.AdvanceToPvpFight(match);
        var a = match.Fighters[0];
        // 开局圣光：攻击 30 × 0.5 = 15，双方互烧；进 fight 时 Casts 至少为 1。
        Assert.True(a.HolyLightCasts >= 1);
        Assert.True(a.Hp < a.MaxHp, "开局圣光应对本桌对手扣血");
    }

    [Fact]
    public void PaladinHolyLightRuntimeMatchesConfig()
    {
        var tables = PvpTestTables.WithPaladin();
        Assert.Equal(1, PvpHeroRuntime.HolyLightRoundCasts(tables, 8));
        Assert.True(PvpHeroRuntime.TryHolyLight(tables, 8, out var ratio, out var perCast));
        Assert.Equal(0.5f, ratio, 4);
        Assert.Equal(0.1f, perCast, 4);
        Assert.Equal(15, PvpHeroRuntime.HolyLightDamage(30, ratio, casts: 0, perCast));
        Assert.Equal(0.1f, PvpHeroRuntime.HolyLightOutgoingPercent(tables, 8, casts: 1), 4);
        Assert.True(PvpHeroRuntime.HasMechanism(tables, 8, MechanismType.UnableMissing));
        Assert.Equal(-0.25f, PvpHeroRuntime.SumValue(tables, 8, MechanismType.HeroTakeDamagePer), 4);
    }

    [Fact]
    public void MerchantWinnerGoldScalesBaseGoldByGetGoldAfterLevel()
    {
        var match = Open(PvpTestTables.WithHeroEntries(pvpOnly: true), heroId: 3);
        PvpMatchTests.AdvanceToPvpFight(match);
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));
        var goldA = a.Gold;
        var goldB = b.Gold;

        match.Showdown(a.UserId);
        match.Showdown(bId);

        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        // 基础金 15 × (1 + 0.2) = 18，只放大基础项。连胜 1 不加奖。利息按发奖前金币。
        var winBody = 18 + 20 * (3 + 1 + 1);
        var loseBody = winBody / 2;
        var aWon = snap.Winners[0] == (PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? 0 : 1);
        if (aWon)
        {
            Assert.Equal(goldA + winBody + PvpSettlementRules.InterestGold(goldA), a.Gold);
            Assert.Equal(goldB + loseBody + PvpSettlementRules.InterestGold(goldB), b.Gold);
        }
        else
        {
            Assert.Equal(goldB + winBody + PvpSettlementRules.InterestGold(goldB), b.Gold);
            Assert.Equal(goldA + loseBody + PvpSettlementRules.InterestGold(goldA), a.Gold);
        }
    }

    private static string LoserId(PvpDuelTable duel, int winnerSeat)
        => winnerSeat == 0 ? duel.RightUserId : duel.LeftUserId;

    private static PvpMatch Open(
        GameTables tables,
        int[]? shopPool = null,
        int heroId = 1,
        IReadOnlyList<CombatTalentCount>? talents = null,
        int hp = 0,
        int seed = 42)
    {
        var players = new[]
        {
            Pub("甲"), Pub("乙"), Pub("丙"), Pub("丁")
        };
        var seats = new SeatSetup[players.Length];
        for (var i = 0; i < players.Length; i++)
        {
            seats[i] = CombatBonuses.BuildSeat(i, players[i].UserId, players[i].NickName, heroId, talents ?? Array.Empty<CombatTalentCount>(), tables);
            seats[i].ShopPoolIds = shopPool ?? Array.Empty<int>();
            if (hp > 0)
            {
                seats[i].Hp = hp;
                seats[i].MaxHp = hp;
            }
        }

        return PvpMatch.Open(Guid.NewGuid(), seed, players, tables, seats, 1);
    }

    private static PlayerPublic Pub(string nick)
        => new PlayerPublic { UserId = Guid.NewGuid().ToString("N"), NickName = nick };
}
