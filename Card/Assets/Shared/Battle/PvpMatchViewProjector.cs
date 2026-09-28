using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>PvpMatchStateDto / PvpShopStateDto 投影：只读 match 状态拼装 DTO，不改状态。由 PvpMatch.ViewFor 在锁内调用。</summary>
    public static class PvpMatchViewProjector
    {
        public static PvpMatchStateDto Project(PvpMatch match, IGameTables tables, string userId)
        {
            PvpDuelTable? own = null;
            var duels = match.Duels;
            var summaries = new PvpDuelSummaryDto[duels.Count];
            for (var i = 0; i < duels.Count; i++)
            {
                var duel = duels[i];
                summaries[i] = new PvpDuelSummaryDto
                {
                    LeftUserId = duel.LeftUserId,
                    RightUserId = duel.RightUserId,
                    MonsterName = duel.MonsterName,
                    Resolved = duel.Resolved
                };
                if (duel.Involves(userId))
                {
                    own = duel;
                }
            }

            var fighters = match.Fighters;
            var roster = new PvpFighterDto[fighters.Count];
            for (var i = 0; i < fighters.Count; i++)
            {
                var f = fighters[i];
                roster[i] = new PvpFighterDto
                {
                    UserId = f.UserId,
                    NickName = f.NickName,
                    Hp = Math.Max(0, f.Hp),
                    MaxHp = f.MaxHp,
                    HeroId = f.Combat.HeroId,
                    Attack = f.Combat.Attack,
                    Gold = f.Gold,
                    Alive = f.Alive,
                    Rank = f.Rank,
                    RubLeft = f.RubLeft,
                    ReplaceLeft = f.ReplaceLeft,
                    PeekLeft = f.PeekLeft,
                    IsBot = f.IsBot,
                    Disconnected = f.Disconnected,
                    RewardGold = f.RewardGold,
                    RelicIds = f.OwnedRelicIds.ToArray(),
                    NullifyDamage = f.NullifyDamageNextHit
                };
            }

            var roomId = match.RoomId.ToString("N");
            return new PvpMatchStateDto
            {
                RoomId = roomId,
                Seed = match.Seed,
                ModeId = match.ModeId,
                ModeName = match.ModeName,
                Round = match.Round,
                Phase = match.Phase,
                FightKind = match.FightKind == PvpFightKind.Monster ? "monster" : "pvp",
                Players = roster,
                Duels = summaries,
                Duel = own == null ? null : own.ViewFor(userId, roomId),
                DuelDamage = own == null || !own.HpApplied ? 0 : own.AppliedDamage,
                PhaseDeadlineUtcMs = match.PhaseDeadlineUtcMs,
                ServerNowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                StateVersion = match.StateVersion,
                Events = CopyEvents(match.PendingEvents),
                Shop = ProjectShop(tables, match.Phase, FighterOf(match, userId))
            };
        }

        public static PvpShopStateDto? ProjectShop(IGameTables tables, string phase, PvpFighter? fighter)
        {
            if (phase != PvpPhases.Shop || fighter == null || !fighter.Alive)
            {
                return null;
            }

            var offerPrices = new int[fighter.ShopOfferIds.Count];
            for (var i = 0; i < offerPrices.Length; i++)
            {
                offerPrices[i] = tables.TryGetRelic(fighter.ShopOfferIds[i], out var relic)
                    ? PvpShopRules.BuyPrice(relic)
                    : 0;
            }

            var ownedSellPrices = new int[fighter.OwnedRelicIds.Count];
            for (var i = 0; i < ownedSellPrices.Length; i++)
            {
                ownedSellPrices[i] = tables.TryGetRelic(fighter.OwnedRelicIds[i], out var relic)
                    ? PvpShopRules.SellPrice(relic)
                    : 0;
            }

            return new PvpShopStateDto
            {
                OfferIds = fighter.ShopOfferIds.ToArray(),
                OfferPrices = offerPrices,
                RefreshCost = PvpShopRules.RefreshCost(fighter, tables.GameConst),
                FreeRefreshLeft = fighter.FreeShopRefreshLeft,
                OwnedRelicIds = fighter.OwnedRelicIds.ToArray(),
                OwnedSellPrices = ownedSellPrices,
                Done = fighter.ShopDone
            };
        }

        private static PvpFighter? FighterOf(PvpMatch match, string userId)
        {
            var seat = match.SeatOf(userId);
            return seat < 0 ? null : match.Fighters[seat];
        }

        private static PvpMatchEventDto[] CopyEvents(IReadOnlyList<PvpMatchEventDto> events)
        {
            if (events.Count == 0)
            {
                return Array.Empty<PvpMatchEventDto>();
            }

            var copy = new PvpMatchEventDto[events.Count];
            for (var i = 0; i < events.Count; i++)
            {
                copy[i] = events[i];
            }

            return copy;
        }
    }
}
