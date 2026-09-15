using System.Collections.Generic;
using Framework.Save;

namespace App.Level
{
    /// <summary>
    /// 按难度记录通关进度。打完某难度全部关卡后该难度完成，并解锁下一难度。
    /// </summary>
    public interface ILevelProgressService : ISaveFlushable
    {
        bool IsDirty { get; }

        int LastHeroId { get; }

        int LastLevelId { get; }

        int LastDifficulty { get; }

        /// <summary>
        /// 该难度是否已打完全部关卡。
        /// </summary>
        bool IsCleared(int difficulty);

        /// <summary>
        /// 该难度是否已解锁。最低难度默认解锁；上一难度打完后解锁下一难度。
        /// </summary>
        bool IsDifficultyUnlocked(int difficulty);

        /// <summary>
        /// 该难度已通关的最高关卡号。未通关任何关为 0。
        /// </summary>
        int GetHighestClearedLevel(int difficulty);

        /// <summary>
        /// 报刚打完的关卡 Id。更新该难度最高关卡；若是最后一关则该难度完成。
        /// </summary>
        void MarkCleared(int levelId);

        void SetLastHero(int heroId);

        void SetLastLevel(int levelId);

        void SetLastDifficulty(int difficulty);

        bool IsHeroUnlocked(int heroId);

        bool TryUnlockHero(int heroId);

        /// <summary>
        /// 难度已解锁，且第 1 关默认开；之后必须先通关同一难度的上一关。
        /// </summary>
        bool IsLevelUnlocked(int levelId);

        IReadOnlyList<int> GetClearedDifficulties();

        void Clear();

        /// <summary>用服务器主档覆盖关卡进度。仅 <c>ApplyServerProfile</c> 调用。</summary>
        void ReplaceFromServer(
            IReadOnlyList<DifficultyProgressEntry> difficultyProgress,
            int lastHeroId,
            int lastLevelId,
            int lastDifficulty,
            IReadOnlyList<int> unlockedHeroIds);

        void Load();
    }
}
