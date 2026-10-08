using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 一局一份的圣物公共库存。非消耗品按品质给固定份数：普通 6 / 稀有 5 / 史诗 4 / 传说 3。
    /// 消耗品（UseType 1/2）不占库存。上架时预扣 1 份，卖掉、淘汰、自毁按携带倍数加回，且不超过基础份数。
    /// </summary>
    public sealed class PvpRelicStock
    {
        public const int OrdinaryStock = 6;
        public const int RareStock = 5;
        public const int EpicStock = 4;
        public const int LegendStock = 3;

        private readonly Dictionary<int, int> _stock = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _base = new Dictionary<int, int>();
        private readonly HashSet<int> _unlimited = new HashSet<int>();

        public static PvpRelicStock Create(IGameTables tables)
        {
            var stock = new PvpRelicStock();
            var relics = tables.Relics;
            for (var i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null || relic.Id <= 0 || relic.RefreshProbability <= 0f)
                {
                    continue;
                }

                if (!PvpRelicRuntime.IsPvpSupported(tables, relic))
                {
                    continue;
                }

                if (PvpRelicBag.IsConsumable(relic))
                {
                    stock._unlimited.Add(relic.Id);
                    continue;
                }

                var count = BaseCount(relic.Type);
                stock._base[relic.Id] = count;
                stock._stock[relic.Id] = count;
            }

            return stock;
        }

        public static int BaseCount(QualityType quality)
        {
            switch (quality)
            {
                case QualityType.Rare:
                    return RareStock;
                case QualityType.Epic:
                    return EpicStock;
                case QualityType.Legend:
                    return LegendStock;
                default:
                    return OrdinaryStock;
            }
        }

        public int Available(int relicId)
        {
            if (_unlimited.Contains(relicId))
            {
                return int.MaxValue;
            }

            return _stock.TryGetValue(relicId, out var count) ? count : 0;
        }

        public bool TryReserve(int relicId)
        {
            if (_unlimited.Contains(relicId))
            {
                return true;
            }

            if (!_stock.TryGetValue(relicId, out var count) || count <= 0)
            {
                return false;
            }

            _stock[relicId] = count - 1;
            return true;
        }

        /// <summary>加回份数，封顶在该圣物的基础库存。</summary>
        public void Return(int relicId, int copies)
        {
            if (copies <= 0 || _unlimited.Contains(relicId) || !_base.TryGetValue(relicId, out var cap))
            {
                return;
            }

            _stock.TryGetValue(relicId, out var current);
            var next = current + copies;
            _stock[relicId] = next > cap ? cap : next;
        }

        public void ReturnShelf(PvpFighter fighter)
        {
            var offers = fighter.ShopOfferIds;
            for (var i = 0; i < offers.Count; i++)
            {
                Return(offers[i], 1);
            }

            offers.Clear();
        }

        public void ReturnCarried(PvpFighter fighter)
        {
            var ids = fighter.OwnedRelicIds;
            for (var i = 0; i < ids.Count; i++)
            {
                Return(ids[i], PvpRelicBag.PowerAt(fighter, i));
            }

            ids.Clear();
            fighter.OwnedRelicPower.Clear();
        }
    }
}
