using CardShare.Contracts;
using CardShare.Contracts.Config;

namespace CardShare.Domain.Pve;

public static class PveProgressRules
{
    public const int MaxStageScore = 1_000_000;

    public static int ClampScore(int score)
    {
        if (score < 0)
        {
            return 0;
        }

        return score > MaxStageScore ? MaxStageScore : score;
    }

    public static int? NextLevelId(IGameTables tables, int levelId)
    {
        if (tables == null || !tables.TryGetLevel(levelId, out var current))
        {
            return null;
        }

        foreach (var row in tables.Levels)
        {
            if (row != null && row.Difficulty == current.Difficulty && row.Level == current.Level + 1)
            {
                return row.Id;
            }
        }

        return null;
    }
}
