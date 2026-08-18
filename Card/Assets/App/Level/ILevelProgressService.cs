using System.Collections.Generic;
using Framework.Save;

namespace App.Level
{
    /// <summary>
    /// Persists which difficulties have been cleared (one run = one difficulty).
    /// </summary>
    public interface ILevelProgressService : ISaveFlushable
    {
        bool IsDirty { get; }

        int LastHeroId { get; }

        int LastLevelId { get; }

        int HighestClearedLevel { get; }

        /// <summary>
        /// Whether this difficulty has been fully cleared (last stage of that difficulty was beaten).
        /// </summary>
        bool IsCleared(int difficulty);

        /// <summary>
        /// Report a cleared <see cref="LevelSnapshot.Id"/> (LevelConfig.Id).
        /// Writes difficulty progress only when that stage is the last of its difficulty.
        /// </summary>
        void MarkCleared(int levelId);

        void SetLastHero(int heroId);

        void SetLastLevel(int levelId);

        bool IsHeroUnlocked(int heroId);

        bool TryUnlockHero(int heroId);

        /// <summary>
        /// 第 1 关默认解锁；之后必须先通关上一关。
        /// </summary>
        bool IsLevelUnlocked(int levelId);

        IReadOnlyList<int> GetClearedDifficulties();

        void Clear();

        void Load();
    }
}
