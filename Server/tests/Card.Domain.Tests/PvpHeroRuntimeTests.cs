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
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));

        match.Showdown(a.UserId);
        match.Showdown(bId);

        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        var scaled = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 1));
        var expected = Math.Max(0, (int)Math.Round(scaled * (1f - 0.25f)));
        if (expected > 0)
        {
            expected = Math.Max(1, expected);
        }

        var loser = PvpBattleTable.SameUser(LoserId(duel, snap.Winners[0]), a.UserId) ? a : b;
        var winner = ReferenceEquals(loser, a) ? b : a;
        Assert.Equal(1000 - expected, loser.Hp);
        Assert.Equal(1000, winner.Hp);
        Assert.Equal(expected, match.ViewFor(a.UserId).DuelDamage);
    }

    [Fact]
    public void MonkDodgesFullDamageAndCountersAttacker()
    {
        var match = Open(PvpTestTables.WithHeroEntries(pvpOnly: true), heroId: 4, hp: 1000);
        var a = match.Fighters[0];
        var duel = match.Duels.First(d => d.Involves(a.UserId));
        var bId = PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? duel.RightUserId : duel.LeftUserId;
        var b = match.Fighters.First(f => PvpBattleTable.SameUser(f.UserId, bId));

        match.Showdown(a.UserId);
        match.Showdown(bId);

        Assert.True(duel.Resolved);
        var snap = duel.Engine.Snapshot;
        Assert.Single(snap.Winners);
        Assert.True(snap.Damages[snap.Winners[0]] > 0);

        // 100% 闪避：败方不掉血；反击 = round(防守方攻击 10 × 1 + 2) = 12 打给胜方。
        var loser = PvpBattleTable.SameUser(LoserId(duel, snap.Winners[0]), a.UserId) ? a : b;
        var winner = ReferenceEquals(loser, a) ? b : a;
        Assert.Equal(1000, loser.Hp);
        Assert.Equal(1000 - 12, winner.Hp);
        Assert.Equal(0, match.ViewFor(a.UserId).DuelDamage);
    }

    [Fact]
    public void BerserkerLifestealsByActualDamageOnWin()
    {
        var match = Open(PvpTestTables.WithHeroEntries(pvpOnly: true), heroId: 5, hp: 100);
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
        var scaled = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 1));
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
    public void HeroExtraAttackOneTimeStacksWithTalentChance()
    {
        var tables = PvpTestTables.WithHeroEntries();
        HandEvaluator.Tables = tables;
        var score = HandEvaluator.Evaluate(new[]
        {
            new PlayingCard(Suit.Heart, Rank.Two),
            new PlayingCard(Suit.Spade, Rank.Three),
            new PlayingCard(Suit.Club, Rank.Four)
        });

        var heroOnly = CombatBonuses.BuildSeat(0, "u", "n", 6, Array.Empty<CombatTalentCount>(), tables);
        var input = CombatBonuses.BuildPlayerInput(tables, heroOnly, score, new CombatSituation());
        Assert.Equal(0.35f, input.ExtraAttackChance, 4);

        var talents = new[] { new CombatTalentCount { TalentId = 500, Count = 1 } };
        var combined = CombatBonuses.BuildSeat(0, "u", "n", 6, talents, tables);
        input = CombatBonuses.BuildPlayerInput(tables, combined, score, new CombatSituation());
        Assert.Equal(0.25f + 0.35f, input.ExtraAttackChance, 4);

        var noHero = CombatBonuses.BuildSeat(0, "u", "n", 1, Array.Empty<CombatTalentCount>(), tables);
        input = CombatBonuses.BuildPlayerInput(tables, noHero, score, new CombatSituation());
        Assert.Equal(0f, input.ExtraAttackChance, 4);
    }

    [Fact]
    public void MerchantWinnerGoldScalesBaseGoldByGetGoldAfterLevel()
    {
        var match = Open(PvpTestTables.WithHeroEntries(pvpOnly: true), heroId: 3);
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
        var damage = (int)Math.Floor(snap.Damages[snap.Winners[0]] * (1f + 0.1f * 1));
        // 基础金 15 × (1 + 0.2) = 18，再 + damage/12 + 20×未用技能(3+1+1)。
        var winGold = 18 + damage / 12 + 20 * (3 + 1 + 1);
        var aWon = snap.Winners[0] == (PvpBattleTable.SameUser(duel.LeftUserId, a.UserId) ? 0 : 1);
        if (aWon)
        {
            Assert.Equal(goldA + winGold, a.Gold);
            Assert.Equal(goldB + winGold / 2, b.Gold);
        }
        else
        {
            Assert.Equal(goldB + winGold, b.Gold);
            Assert.Equal(goldA + winGold / 2, a.Gold);
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
