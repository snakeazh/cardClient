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

        /// <summary>
        /// Whether this difficulty has been fully cleared (last stage of that difficulty was beaten).
        /// </summary>
        bool IsCleared(int difficulty);

        /// <summary>
        /// Report a cleared <see cref="LevelSnapshot.Id"/> (LevelConfig.Id).
        /// Writes difficulty progress only when that stage is the last of its difficulty.
        /// </summary>
        void MarkCleared(int levelId);

        IReadOnlyList<int> GetClearedDifficulties();

        void Clear();

        void Load();
    }
}
