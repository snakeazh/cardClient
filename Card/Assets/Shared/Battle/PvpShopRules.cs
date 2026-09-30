using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>PvP 商店规则：与 PvE 同公式，操作对象为 PvpFighter，池 = 商店池 − 已持有 − 在架。</summary>
    public static class PvpShopRules
    {
        public const int OfferCount = 4;

        public static int RefreshCost(PvpFighter fighter, GameConst gameConst)
        {
            var first = gameConst.ShopRefreshFirst > 0 ? gameConst.ShopRefreshFirst : 5;
            var after = Math.Max(0, gameConst.ShopRefreshAfter);
            var maxUp = gameConst.ShopRefreshGoldUpNumMax > 0 ? gameConst.ShopRefreshGoldUpNumMax : int.MaxValue;
            var ups = Math.Min(Math.Max(0, fighter.ShopRefreshCount), maxUp);
            return first + ups * after;
        }

        public static void FillOffers(PvpFighter fighter, IGameTables tables, Random rng)
        {
            var offers = fighter.ShopOfferIds;
            while (offers.Count > OfferCount)
            {
                offers.RemoveAt(offers.Count - 1);
            }

            var pool = BuildPool(fighter, tables);
            while (offers.Count < OfferCount && pool.Count > 0)
            {
                var pick = PickWeighted(pool, tables, fighter.Combat.HeroId, rng);
                if (pick == null)
                {
                    break;
                }

                offers.Add(pick.Id);
                pool.Remove(pick);
            }
        }

        public static void RerollOffers(PvpFighter fighter, IGameTables tables, Random rng)
        {
            fighter.ShopOfferIds.Clear();
            FillOffers(fighter, tables, rng);
        }

        /// <summary>购买价：英雄 RelicPricePer 折扣（对齐 PVE HeroMechanics.BuyPrice），货架投影同源。</summary>
        public static int BuyPrice(IGameTables tables, int heroId, RelicConfig relic)
            => PvpHeroRuntime.BuyPrice(tables, heroId, relic);

        public static int SellPrice(RelicConfig relic) => Math.Max(0, relic.SellingPrice);

        private static List<RelicConfig> BuildPool(PvpFighter fighter, IGameTables tables)
        {
            var unlocked = new HashSet<int>(fighter.ShopPoolIds);
            var owned = new HashSet<int>(fighter.OwnedRelicIds);
            var onShelf = new HashSet<int>(fighter.ShopOfferIds);
            var pool = new List<RelicConfig>();
            for (var i = 0; i < tables.Relics.Count; i++)
            {
                var relic = tables.Relics[i];
                if (relic.Id <= 0 || relic.RefreshProbability <= 0f)
                {
                    continue;
                }

                if (!unlocked.Contains(relic.Id) || owned.Contains(relic.Id) || onShelf.Contains(relic.Id))
                {
                    continue;
                }

                // PVP 未实现其机制的圣物（如 PVE 专属消耗效果）不上架，防止卖出无效品。
                if (!PvpRelicRuntime.IsPvpSupported(tables, relic))
                {
                    continue;
                }

                pool.Add(relic);
            }

            return pool;
        }

        private static RelicConfig? PickWeighted(List<RelicConfig> pool, IGameTables tables, int heroId, Random rng)
        {
            if (pool.Count == 0)
            {
                return null;
            }

            // 权重经英雄词条（EpicLegendRelicProUp 等）修正，口径同 PVE HeroMechanics.ShopWeight。
            var total = 0f;
            for (var i = 0; i < pool.Count; i++)
            {
                total += PvpHeroRuntime.ShopWeight(tables, heroId, pool[i]);
            }

            if (total <= 0f)
            {
                return pool[rng.Next(pool.Count)];
            }

            var roll = (float)rng.NextDouble() * total;
            var acc = 0f;
            for (var i = 0; i < pool.Count; i++)
            {
                acc += PvpHeroRuntime.ShopWeight(tables, heroId, pool[i]);
                if (roll <= acc)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }
    }
}
