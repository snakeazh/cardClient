using System;
using System.Collections.Generic;
using App.Config;

namespace App.Level
{
    /// <summary>
    /// Resolved level: one Difficulty + Level, with monsters in seat order.
    /// </summary>
    public sealed class LevelSnapshot
    {
        public LevelSnapshot(
            int id,
            int difficulty,
            int level,
            int getGold,
            int levelEntryNum,
            int monsterCardHandScoreLevelLimit,
            IReadOnlyList<LevelMonster> monsters)
        {
            Id = id;
            Difficulty = difficulty;
            Level = level;
            GetGold = getGold < 0 ? 0 : getGold;
            LevelEntryNum = levelEntryNum < 0 ? 0 : levelEntryNum;
            MonsterCardHandScoreLevelLimit = monsterCardHandScoreLevelLimit < 0 ? 0 : monsterCardHandScoreLevelLimit;
            Monsters = monsters ?? Array.Empty<LevelMonster>();
            var hasBoss = false;
            for (var i = 0; i < Monsters.Count; i++)
            {
                if (Monsters[i].IsBoss)
                {
                    hasBoss = true;
                    break;
                }
            }

            HasBoss = hasBoss;
        }

        public int Id { get; }

        public int Difficulty { get; }

        public int Level { get; }

        /// <summary>通关发放的局内金币，来自 <see cref="LevelConfig.GetGold"/>。</summary>
        public int GetGold { get; }

        /// <summary>本关随机机制条数，来自 <see cref="LevelConfig.LevelEntryNum"/>。</summary>
        public int LevelEntryNum { get; }

        /// <summary>敌人开牌最大牌型顺位，来自 <see cref="LevelConfig.MonsterCardHandScoreLevelLimit"/>。0 表示不限制。</summary>
        public int MonsterCardHandScoreLevelLimit { get; }

        public bool HasBoss { get; }

        public IReadOnlyList<LevelMonster> Monsters { get; }
    }

    /// <summary>
    /// One spawn slot from MonsterGroupConfig + MonsterConfig.
    /// </summary>
    public sealed class LevelMonster
    {
        public LevelMonster(
            int groupId,
            int monsterId,
            int monsterLevel,
            MonsterType type,
            int hp,
            int damage,
            int monsterEntry,
            string icon,
            string name)
        {
            GroupId = groupId;
            MonsterId = monsterId;
            MonsterLevel = monsterLevel;
            Type = type;
            Hp = hp;
            Damage = damage;
            MonsterEntry = monsterEntry;
            Icon = icon;
            Name = name ?? string.Empty;
        }

        public int GroupId { get; }

        public int MonsterId { get; }

        public int MonsterLevel { get; }

        public MonsterType Type { get; }

        public int Hp { get; }

        /// <summary>`MonsterConfig.MonsterDamage`。</summary>
        public int Damage { get; }

        public int MonsterEntry { get; }

        /// <summary>`MonsterConfig.Icon`，拼 `_attack` / `_damage` / `_dead` 加载头像。</summary>
        public string Icon { get; }

        /// <summary>`MonsterConfig.Name`。</summary>
        public string Name { get; }

        public bool IsBoss => Type == MonsterType.Boss;
    }

    [Serializable]
    public sealed class DifficultyProgressEntry
    {
        public int Difficulty;
        public int HighestClearedLevel;
    }

    [Serializable]
    public sealed class LevelProgressSaveData
    {
        public int[] ClearedDifficulties = Array.Empty<int>();
        public int LastHeroId;
        public int LastLevelId;
        public int LastDifficulty;
        public int[] UnlockedHeroIds = Array.Empty<int>();
        /// <summary>旧存档：仅默认难度的最高关卡。新存档以 DifficultyProgress 为准。</summary>
        public int HighestClearedLevel;
        public DifficultyProgressEntry[] DifficultyProgress = Array.Empty<DifficultyProgressEntry>();
    }
}
