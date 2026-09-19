using System;
using System.Collections.Generic;
using System.Linq;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    public static class PvpSchedule
    {
        public const int DefaultModeId = 1;

        public static bool TryLoad(
            IGameTables tables,
            int modeId,
            out PvpModeConfig mode,
            out IReadOnlyList<PvpRoundConfig> rounds)
        {
            rounds = Array.Empty<PvpRoundConfig>();
            if (!tables.TryGetPvpMode(modeId, out mode!))
            {
                return false;
            }

            rounds = tables.GetPvpRounds(modeId);
            return rounds.Count > 0;
        }

        public static bool TryGetRound(IReadOnlyList<PvpRoundConfig> rounds, int round, out PvpRoundConfig row)
        {
            for (var i = 0; i < rounds.Count; i++)
            {
                if (rounds[i].Round == round)
                {
                    row = rounds[i];
                    return true;
                }
            }

            row = null!;
            return false;
        }

        public static int LastRound(IReadOnlyList<PvpRoundConfig> rounds)
            => rounds.Count == 0 ? 0 : rounds.Max(r => r.Round);

        public static PvpFightKind EffectiveKind(PvpModeConfig mode, PvpRoundConfig row, int aliveCount)
        {
            if (row.FightKind == PvpFightKind.Monster && mode.SkipMonsterWhenTwoLeft && aliveCount == 2)
            {
                return PvpFightKind.Pvp;
            }

            return row.FightKind;
        }
    }
}
