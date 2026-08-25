using System;
using System.Collections.Generic;
using App.Config;
using Framework.Log;

namespace App.Level
{
    /// <summary>
    /// Indexes level tables at construction and returns immutable snapshots.
    /// </summary>
    public sealed class LevelService : ILevelService
    {
        public const int MaxMonstersPerLevel = 3;

        public int DefaultDifficulty => 1;

        public int CurrentLevelId { get; private set; }

        public LevelSnapshot Current => GetById(CurrentLevelId);

        private readonly Dictionary<int, Dictionary<int, LevelSnapshot>> _levels =
            new Dictionary<int, Dictionary<int, LevelSnapshot>>();
        private int[] _difficulties = Array.Empty<int>();
        private readonly Dictionary<int, LevelSnapshot> _levelsById =
            new Dictionary<int, LevelSnapshot>();
        private readonly Dictionary<int, Dictionary<int, LevelMonster>> _monsters =
            new Dictionary<int, Dictionary<int, LevelMonster>>();

        public LevelService()
        {
            IndexMonsters();
            IndexLevels();
            TrySelect(DefaultDifficulty, 1);
            AppLog.Info(
                LogChannel.Level,
                $"indexed: levels={CountNested(_levels)}, monsters={CountNested(_monsters)}, difficulties={_levels.Count}, current={CurrentLevelId}");
        }

        public IReadOnlyList<int> GetDifficulties() => _difficulties;

        public int GetMaxLevel(int difficulty)
        {
            if (!_levels.TryGetValue(difficulty, out var byLevel) || byLevel.Count == 0)
            {
                return 0;
            }

            var max = 0;
            foreach (var level in byLevel.Keys)
            {
                if (level > max)
                {
                    max = level;
                }
            }

            return max;
        }

        public bool TryGet(int difficulty, int level, out LevelSnapshot snapshot)
        {
            if (_levels.TryGetValue(difficulty, out var byLevel) &&
                byLevel.TryGetValue(level, out snapshot))
            {
                return true;
            }

            snapshot = null;
            return false;
        }

        public LevelSnapshot Get(int difficulty, int level)
        {
            TryGet(difficulty, level, out var snapshot);
            return snapshot;
        }

        public bool TryGetNext(int difficulty, int level, out LevelSnapshot next)
        {
            return TryGet(difficulty, level + 1, out next);
        }

        public bool TryGetById(int levelId, out LevelSnapshot snapshot)
        {
            return _levelsById.TryGetValue(levelId, out snapshot);
        }

        public LevelSnapshot GetById(int levelId)
        {
            TryGetById(levelId, out var snapshot);
            return snapshot;
        }

        public bool TrySelect(int levelId)
        {
            if (!TryGetById(levelId, out var snapshot) || snapshot == null)
            {
                AppLog.Warn(LogChannel.Level, $"Unknown level Id={levelId}.");
                return false;
            }

            CurrentLevelId = snapshot.Id;
            return true;
        }

        public bool TrySelect(int difficulty, int level)
        {
            if (!TryGet(difficulty, level, out var snapshot) || snapshot == null)
            {
                AppLog.Warn(LogChannel.Level, $"Missing Difficulty={difficulty} Level={level}.");
                return false;
            }

            CurrentLevelId = snapshot.Id;
            return true;
        }

        public bool TryGetNextDifficulty(int difficulty, out int next)
        {
            for (var i = 0; i < _difficulties.Length; i++)
            {
                if (_difficulties[i] == difficulty && i + 1 < _difficulties.Length)
                {
                    next = _difficulties[i + 1];
                    return true;
                }
            }

            next = 0;
            return false;
        }

        public bool TryGetMonster(int monsterId, int monsterLevel, out LevelMonster monster)
        {
            if (_monsters.TryGetValue(monsterId, out var byLevel) &&
                byLevel.TryGetValue(monsterLevel, out monster))
            {
                return true;
            }

            monster = null;
            return false;
        }

        private void IndexMonsters()
        {
            foreach (var pair in MonsterConfig.All)
            {
                var row = pair.Value;
                if (row == null)
                {
                    continue;
                }

                if (!_monsters.TryGetValue(row.MonsterId, out var byLevel))
                {
                    byLevel = new Dictionary<int, LevelMonster>();
                    _monsters[row.MonsterId] = byLevel;
                }

                if (byLevel.ContainsKey(row.MonsterLevel))
                {
                    AppLog.Warn(
                        LogChannel.Level,
                        $"Duplicate MonsterId={row.MonsterId} Level={row.MonsterLevel} (Id={row.Id}), later row wins.");
                }

                byLevel[row.MonsterLevel] = new LevelMonster(
                    groupId: 0,
                    monsterId: row.MonsterId,
                    monsterLevel: row.MonsterLevel,
                    type: row.Type,
                    hp: row.MonsterHp,
                    damage: row.MonsterDamage,
                    monsterEntry: row.MonsterEntry);
            }
        }

        private void IndexLevels()
        {
            foreach (var pair in LevelConfig.All)
            {
                var row = pair.Value;
                if (row == null)
                {
                    continue;
                }

                var snapshot = ResolveLevel(row);
                if (!_levels.TryGetValue(row.Difficulty, out var byLevel))
                {
                    byLevel = new Dictionary<int, LevelSnapshot>();
                    _levels[row.Difficulty] = byLevel;
                }

                if (byLevel.ContainsKey(row.Level))
                {
                    AppLog.Warn(
                        LogChannel.Level,
                        $"Duplicate Difficulty={row.Difficulty} Level={row.Level} (Id={row.Id}), later row wins.");
                }

                byLevel[row.Level] = snapshot;
                _levelsById[row.Id] = snapshot;
            }

            var keys = new List<int>(_levels.Keys);
            keys.Sort();
            _difficulties = keys.ToArray();
        }

        private LevelSnapshot ResolveLevel(LevelConfig row)
        {
            var monsters = new List<LevelMonster>(MaxMonstersPerLevel);
            var groups = row.MonsterGroup;
            if (groups == null || groups.Length == 0)
            {
                AppLog.Warn(LogChannel.Level, $"Level Id={row.Id} has no MonsterGroup.");
                return new LevelSnapshot(row.Id, row.Difficulty, row.Level, monsters);
            }

            if (groups.Length > MaxMonstersPerLevel)
            {
                AppLog.Warn(
                    LogChannel.Level,
                    $"Level Id={row.Id} has {groups.Length} monster groups, only first {MaxMonstersPerLevel} are used.");
            }

            var count = groups.Length < MaxMonstersPerLevel ? groups.Length : MaxMonstersPerLevel;
            for (var i = 0; i < count; i++)
            {
                var groupId = groups[i];
                if (!MonsterGroupConfig.TryGet(groupId, out var group) || group == null)
                {
                    AppLog.Warn(LogChannel.Level, $"Level Id={row.Id} missing MonsterGroup Id={groupId}.");
                    continue;
                }

                if (!TryGetMonster(group.MonsterId, group.MonsterLevel, out var monster) || monster == null)
                {
                    AppLog.Warn(
                        LogChannel.Level,
                        $"Level Id={row.Id} group {groupId}: missing MonsterId={group.MonsterId} Level={group.MonsterLevel}.");
                    continue;
                }

                monsters.Add(new LevelMonster(
                    groupId,
                    monster.MonsterId,
                    monster.MonsterLevel,
                    monster.Type,
                    monster.Hp,
                    monster.Damage,
                    monster.MonsterEntry));
            }

            return new LevelSnapshot(row.Id, row.Difficulty, row.Level, monsters);
        }

        private static int CountNested<T>(Dictionary<int, Dictionary<int, T>> map)
        {
            var count = 0;
            foreach (var inner in map.Values)
            {
                count += inner.Count;
            }

            return count;
        }
    }
}
