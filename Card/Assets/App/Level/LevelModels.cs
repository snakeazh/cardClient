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
            IReadOnlyList<LevelMonster> monsters)
        {
            Id = id;
            Difficulty = difficulty;
            Level = level;
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
            int monsterEntry)
        {
            GroupId = groupId;
            MonsterId = monsterId;
            MonsterLevel = monsterLevel;
            Type = type;
            Hp = hp;
            MonsterEntry = monsterEntry;
        }

        public int GroupId { get; }

        public int MonsterId { get; }

        public int MonsterLevel { get; }

        public MonsterType Type { get; }

        public int Hp { get; }

        public int MonsterEntry { get; }

        public bool IsBoss => Type == MonsterType.Boss;
    }

    [Serializable]
    public sealed class LevelProgressSaveData
    {
        public int[] ClearedDifficulties = Array.Empty<int>();
    }
}
