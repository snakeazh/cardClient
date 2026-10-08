using System.Collections.Generic;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 携带与合成。每名玩家开局 4 个槽，金币解锁到最多 10 个。
    /// 同一圣物凑满 3 件未合成拷贝时合成 1 件，倍数为 3，只占 1 槽。
    /// </summary>
    public static class PvpRelicBag
    {
        public const int FuseCount = 3;
        public const int FreeSlots = 4;
        public const int MaxSlots = 10;
        public const int FusedPower = 3;

        /// <summary>解锁第 5~10 槽的金币。</summary>
        public static readonly int[] ExtraSlotGold = { 40, 60, 80, 100, 120, 150 };

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
        {
            if (currentSlots >= MaxSlots)
            {
                return 0;
            }

            var index = currentSlots - FreeSlots;
            if (index < 0)
            {
                index = 0;
            }

            if (index >= ExtraSlotGold.Length)
            {
                return ExtraSlotGold[ExtraSlotGold.Length - 1];
            }

            return ExtraSlotGold[index];
        }

        public static void Add(PvpFighter fighter, int relicId)
        {
            fighter.OwnedRelicIds.Add(relicId);
            fighter.OwnedRelicPower.Add(1);
            TryFuse(fighter, relicId);
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

        private static void TryFuse(PvpFighter fighter, int relicId)
        {
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

                if (singles.Count < FuseCount)
                {
                    return;
                }

                for (var n = FuseCount - 1; n >= 0; n--)
                {
                    var index = singles[n];
                    ids.RemoveAt(index);
                    if (index < fighter.OwnedRelicPower.Count)
                    {
                        fighter.OwnedRelicPower.RemoveAt(index);
                    }
                }

                ids.Add(relicId);
                fighter.OwnedRelicPower.Add(FusedPower);
            }
        }
    }
}
