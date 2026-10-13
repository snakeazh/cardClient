using CardShare.Contracts;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 局内机器人商店决策：优先买货架上买得起的最高价圣物；
    /// 槽满则开槽；开完继续买。不刷新、不卖出。
    /// </summary>
    public static class PvpBotShop
    {
        /// <summary>货架上买得起的最高价圣物 Id；同价取 Id 较小者。没有则 null。</summary>
        public static int? PickHighestAffordable(PvpFighter fighter, IGameTables tables)
        {
            if (fighter == null || tables == null || fighter.OwnedRelicIds.Count >= fighter.RelicSlots)
            {
                return null;
            }

            int? bestId = null;
            var bestPrice = int.MinValue;
            var heroId = fighter.Combat != null ? fighter.Combat.HeroId : 0;
            for (var i = 0; i < fighter.ShopOfferIds.Count; i++)
            {
                var relicId = fighter.ShopOfferIds[i];
                if (!tables.TryGetRelic(relicId, out var relic))
                {
                    continue;
                }

                var price = PvpShopRules.BuyPrice(tables, heroId, relic);
                if (fighter.Gold < price)
                {
                    continue;
                }

                if (bestId == null || price > bestPrice || (price == bestPrice && relicId < bestId.Value))
                {
                    bestId = relicId;
                    bestPrice = price;
                }
            }

            return bestId;
        }

        /// <summary>槽已满且未达上限、金币够开下一槽。</summary>
        public static bool CanUnlockSlot(PvpFighter fighter, IGameTables tables)
        {
            if (fighter == null || tables == null)
            {
                return false;
            }

            if (fighter.OwnedRelicIds.Count < fighter.RelicSlots)
            {
                return false;
            }

            if (fighter.RelicSlots >= PvpRelicBag.MaxSlotsFor(tables.GameConst))
            {
                return false;
            }

            var cost = PvpRelicBag.NextSlotCost(tables.GameConst, fighter.RelicSlots);
            return cost > 0 && fighter.Gold >= cost;
        }
    }
}
