using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    public static class PvpSchedule
    {
        public const int DefaultModeId = 1;

        /// <summary>PvP 轮基础金币。野怪轮第 1 次 10，之后 20、25、30…</summary>
        public const int PvpGoldBase = 15;

        public const int FirstMonsterGroupId = 10011;
        public const int MonsterGroupStep = 30;

        public static bool TryLoad(IGameTables tables, int modeId, out PvpModeConfig mode)
            => tables.TryGetPvpMode(modeId, out mode!);

        /// <summary>第 1 轮野怪，之后每 3 场 PvP 插 1 轮野怪（1、5、9、13…）。没有轮次上限。</summary>
        public static bool IsMonsterRound(int round)
            => round >= 1 && (round - 1) % 4 == 0;

        public static PvpFightKind Kind(PvpModeConfig mode, int round, int aliveCount)
        {
            if (IsMonsterRound(round) && mode.SkipMonsterWhenTwoLeft && aliveCount == 2)
            {
                return PvpFightKind.Pvp;
            }

            return IsMonsterRound(round) ? PvpFightKind.Monster : PvpFightKind.Pvp;
        }

        public static int GoldBase(int round)
        {
            if (!IsMonsterRound(round))
            {
                return PvpGoldBase;
            }

            var index = (round - 1) / 4;
            return index == 0 ? 10 : 15 + 5 * index;
        }

        /// <summary>野怪组按 10011、10041、10071… 递增。表里没有更后面的组时，用已有的最高一组。</summary>
        public static int MonsterGroup(IGameTables tables, int round)
        {
            var index = round < 1 ? 0 : (round - 1) / 4;
            var id = FirstMonsterGroupId + MonsterGroupStep * index;
            while (id >= FirstMonsterGroupId)
            {
                if (tables.TryGetMonsterGroup(id, out _))
                {
                    return id;
                }

                id -= MonsterGroupStep;
            }

            return FirstMonsterGroupId;
        }
    }
}
