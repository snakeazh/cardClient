using System.Collections.Generic;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 携带与合成。开局槽位、解锁金币、合成件数读 GameConst
    /// （PVPRelicInitialRelicNum / PVPRelicNumNeedGold / PvpRelicSynthesisNum）。
    /// 未配置时沿用开局 4 槽、40|60|80|100|120|150、三合一。
    /// </summary>
    public static class PvpRelicBag
    {
        public const int FuseCount = 3;
        public const int FreeSlots = 4;
        public const int MaxSlots = 10;
        public const int FusedPower = 3;

        /// <summary>未配置 PVPRelicNumNeedGold 时，解锁额外槽的金币。</summary>
        public static readonly int[] ExtraSlotGold = { 40, 60, 80, 100, 120, 150 };

        public static int SynthesisCount(GameConst gameConst)
        {
            var count = gameConst != null ? gameConst.PvpRelicSynthesisNum : 0;
            return count >= 2 ? count : FuseCount;
        }

        public static int InitialSlots(GameConst gameConst)
        {
            var slots = gameConst != null ? gameConst.PVPRelicInitialRelicNum : 0;
            return slots > 0 ? slots : FreeSlots;
        }

        public static int[] SlotGold(GameConst gameConst)
        {
            var gold = gameConst != null ? gameConst.PVPRelicNumNeedGold : null;
            return gold != null && gold.Length > 0 ? gold : ExtraSlotGold;
        }

        public static int MaxSlotsFor(GameConst gameConst)
            => InitialSlots(gameConst) + SlotGold(gameConst).Length;

        public static bool IsConsumable(RelicConfig relic)
            => relic.UseType == 1 || relic.UseType == 2;

        public static int PowerAt(PvpFighter fighter, int index)
        {
            if (index < 0 || index >= fighter.OwnedRelicPower.Count)
            {
                return 1;
            }

            var power = fighter.OwnedRelicPower[index];
            return power < 1 ? 1 : power;
        }

        public static int NextSlotCost(int currentSlots)
            => NextSlotCost(null!, currentSlots);

        public static int NextSlotCost(GameConst gameConst, int currentSlots)
        {
            if (currentSlots >= MaxSlotsFor(gameConst))
            {
                return 0;
            }

            var gold = SlotGold(gameConst);
            var index = currentSlots - InitialSlots(gameConst);
            if (index < 0)
            {
                index = 0;
            }

            if (index >= gold.Length)
            {
                return gold[gold.Length - 1];
            }

            return gold[index];
        }

        public static void Add(PvpFighter fighter, int relicId, GameConst gameConst = null!)
        {
            fighter.OwnedRelicIds.Add(relicId);
            fighter.OwnedRelicPower.Add(1);
            TryFuse(fighter, relicId, gameConst);
        }

        /// <summary>优先卸下一件未合成的。返回该件倍数（回池份数），没有则 0。</summary>
        public static int RemoveOne(PvpFighter fighter, int relicId)
        {
            var single = -1;
            var any = -1;
            var ids = fighter.OwnedRelicIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] != relicId)
                {
                    continue;
                }

                any = i;
                if (PowerAt(fighter, i) == 1)
                {
                    single = i;
                    break;
                }
            }

            var index = single >= 0 ? single : any;
            if (index < 0)
            {
                return 0;
            }

            var power = PowerAt(fighter, index);
            ids.RemoveAt(index);
            if (index < fighter.OwnedRelicPower.Count)
            {
                fighter.OwnedRelicPower.RemoveAt(index);
            }

            return power;
        }

        public static List<int> Expand(PvpFighter fighter)
        {
            var expanded = new List<int>();
            var ids = fighter.OwnedRelicIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var power = PowerAt(fighter, i);
                for (var n = 0; n < power; n++)
                {
                    expanded.Add(ids[i]);
                }
            }

            return expanded;
        }

        private static void TryFuse(PvpFighter fighter, int relicId, GameConst gameConst)
        {
            var fuseCount = SynthesisCount(gameConst);
            while (true)
            {
                var singles = new List<int>();
                var ids = fighter.OwnedRelicIds;
                for (var i = 0; i < ids.Count; i++)
                {
                    if (ids[i] == relicId && PowerAt(fighter, i) == 1)
                    {
                        singles.Add(i);
                    }
                }

                if (singles.Count < fuseCount)
                {
                    return;
                }

                for (var n = fuseCount - 1; n >= 0; n--)
                {
                    var index = singles[n];
                    ids.RemoveAt(index);
                    if (index < fighter.OwnedRelicPower.Count)
                    {
                        fighter.OwnedRelicPower.RemoveAt(index);
                    }
                }

                ids.Add(relicId);
                fighter.OwnedRelicPower.Add(fuseCount);
            }
        }
    }
}
