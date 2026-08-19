using System.Collections.Generic;

namespace App.Level
{
    /// <summary>
    /// Resolves LevelConfig / MonsterGroupConfig / MonsterConfig into snapshots.
    /// One run plays a single difficulty; TryGetNext false means that difficulty is complete.
    /// GameSession 按 Current 上场怪物与血量。
    /// </summary>
    public interface ILevelService
    {
        int DefaultDifficulty { get; }

        /// <summary>
        /// Currently selected <see cref="LevelSnapshot.Id"/> (LevelConfig.Id). 0 if none.
        /// </summary>
        int CurrentLevelId { get; }

        /// <summary>
        /// Snapshot for <see cref="CurrentLevelId"/>, or null if none / unknown.
        /// </summary>
        LevelSnapshot Current { get; }

        /// <summary>
        /// All difficulties present in LevelConfig, ascending.
        /// </summary>
        IReadOnlyList<int> GetDifficulties();

        int GetMaxLevel(int difficulty);

        bool TryGet(int difficulty, int level, out LevelSnapshot snapshot);

        LevelSnapshot Get(int difficulty, int level);

        bool TryGetNext(int difficulty, int level, out LevelSnapshot next);

        bool TryGetById(int levelId, out LevelSnapshot snapshot);

        LevelSnapshot GetById(int levelId);

        /// <summary>
        /// Selects by LevelConfig.Id. Unknown id: Warning, keeps previous, returns false.
        /// </summary>
        bool TrySelect(int levelId);

        /// <summary>
        /// Selects Difficulty + Level. Missing: Warning, keeps previous, returns false.
        /// </summary>
        bool TrySelect(int difficulty, int level);

        /// <summary>
        /// 配置中升序的下一个难度。没有则 false。
        /// </summary>
        bool TryGetNextDifficulty(int difficulty, out int next);

        bool TryGetMonster(int monsterId, int monsterLevel, out LevelMonster monster);
    }
}
