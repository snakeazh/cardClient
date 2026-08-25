using System;
using System.Collections.Generic;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.Level
{
    /// <summary>
    /// 按难度记录最高通关关卡。打完该难度全部关卡后解锁下一难度。
    /// </summary>
    public sealed class LevelProgressService : ILevelProgressService
    {
        public const string SaveKey = "level.progress.v1";

        private readonly ISaveService _save;
        private readonly ILevelService _levels;
        private readonly HashSet<int> _cleared = new HashSet<int>();
        private readonly HashSet<int> _unlockedHeroes = new HashSet<int>();
        private readonly Dictionary<int, int> _highestCleared = new Dictionary<int, int>();
        private bool _dirty;

        public LevelProgressService(ISaveService save, ILevelService levels)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        }

        public bool IsDirty => _dirty;

        public int LastHeroId { get; private set; }

        public int LastLevelId { get; private set; }

        public int LastDifficulty { get; private set; }

        public bool IsCleared(int difficulty)
        {
            if (_cleared.Contains(difficulty))
            {
                return true;
            }

            var max = _levels.GetMaxLevel(difficulty);
            return max > 0 && GetHighestClearedLevel(difficulty) >= max;
        }

        public bool IsDifficultyUnlocked(int difficulty)
        {
            var diffs = _levels.GetDifficulties();
            if (diffs == null || diffs.Count == 0)
            {
                return false;
            }

            if (difficulty == diffs[0])
            {
                return true;
            }

            for (var i = 1; i < diffs.Count; i++)
            {
                if (diffs[i] == difficulty)
                {
                    return IsCleared(diffs[i - 1]);
                }
            }

            return false;
        }

        public int GetHighestClearedLevel(int difficulty)
        {
            return _highestCleared.TryGetValue(difficulty, out var level) ? level : 0;
        }

        public void MarkCleared(int levelId)
        {
            if (!_levels.TryGetById(levelId, out var snapshot) || snapshot == null)
            {
                AppLog.Warn(LogChannel.Level, $"Unknown level Id={levelId}.");
                return;
            }

            var prev = GetHighestClearedLevel(snapshot.Difficulty);
            if (snapshot.Level > prev)
            {
                _highestCleared[snapshot.Difficulty] = snapshot.Level;
                _dirty = true;
            }

            if (_levels.TryGetNext(snapshot.Difficulty, snapshot.Level, out _))
            {
                return;
            }

            if (_cleared.Add(snapshot.Difficulty))
            {
                _dirty = true;
            }
        }

        public void SetLastHero(int heroId)
        {
            if (LastHeroId == heroId)
            {
                return;
            }

            LastHeroId = heroId;
            _dirty = true;
        }

        public void SetLastLevel(int levelId)
        {
            if (LastLevelId == levelId)
            {
                return;
            }

            LastLevelId = levelId;
            _dirty = true;
        }

        public void SetLastDifficulty(int difficulty)
        {
            if (LastDifficulty == difficulty)
            {
                return;
            }

            LastDifficulty = difficulty;
            _dirty = true;
        }

        public bool IsHeroUnlocked(int heroId)
        {
            return _unlockedHeroes.Contains(heroId);
        }

        public bool TryUnlockHero(int heroId)
        {
            if (heroId <= 0 || !_unlockedHeroes.Add(heroId))
            {
                return false;
            }

            _dirty = true;
            return true;
        }

        public bool IsLevelUnlocked(int levelId)
        {
            if (!_levels.TryGetById(levelId, out var snapshot) || snapshot == null)
            {
                return false;
            }

            if (!IsDifficultyUnlocked(snapshot.Difficulty))
            {
                return false;
            }

            if (snapshot.Level <= 1)
            {
                return true;
            }

            return GetHighestClearedLevel(snapshot.Difficulty) >= snapshot.Level - 1;
        }

        public IReadOnlyList<int> GetClearedDifficulties()
        {
            SyncClearedFromHighest();
            var list = new List<int>(_cleared);
            list.Sort();
            return list;
        }

        public void Clear()
        {
            var hadData = _cleared.Count > 0 ||
                          _unlockedHeroes.Count > 0 ||
                          _highestCleared.Count > 0 ||
                          LastHeroId != 0 ||
                          LastLevelId != 0 ||
                          LastDifficulty != 0;
            _cleared.Clear();
            _unlockedHeroes.Clear();
            _highestCleared.Clear();
            LastHeroId = 0;
            LastLevelId = 0;
            LastDifficulty = 0;
            if (hadData)
            {
                _dirty = true;
            }
        }

        public void Load()
        {
            _cleared.Clear();
            _unlockedHeroes.Clear();
            _highestCleared.Clear();
            LastHeroId = 0;
            LastLevelId = 0;
            LastDifficulty = 0;
            _dirty = false;
            if (!_save.HasKey(SaveKey))
            {
                return;
            }

            var json = _save.GetString(SaveKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var data = JsonUtility.FromJson<LevelProgressSaveData>(json);
            if (data == null)
            {
                AppLog.Warn(LogChannel.Level, "Failed to parse save data.");
                return;
            }

            if (data.ClearedDifficulties != null)
            {
                for (var i = 0; i < data.ClearedDifficulties.Length; i++)
                {
                    var difficulty = data.ClearedDifficulties[i];
                    if (difficulty <= 0)
                    {
                        continue;
                    }

                    WarnIfUnknownDifficulty(difficulty);
                    _cleared.Add(difficulty);
                    var max = _levels.GetMaxLevel(difficulty);
                    if (max > GetHighestClearedLevel(difficulty))
                    {
                        _highestCleared[difficulty] = max;
                    }
                }
            }

            if (data.DifficultyProgress != null)
            {
                for (var i = 0; i < data.DifficultyProgress.Length; i++)
                {
                    var entry = data.DifficultyProgress[i];
                    if (entry == null || entry.Difficulty <= 0 || entry.HighestClearedLevel <= 0)
                    {
                        continue;
                    }

                    var current = GetHighestClearedLevel(entry.Difficulty);
                    if (entry.HighestClearedLevel > current)
                    {
                        _highestCleared[entry.Difficulty] = entry.HighestClearedLevel;
                    }
                }
            }
            else if (data.HighestClearedLevel > 0)
            {
                var fallback = _levels.DefaultDifficulty;
                if (GetHighestClearedLevel(fallback) < data.HighestClearedLevel)
                {
                    _highestCleared[fallback] = data.HighestClearedLevel;
                }
            }

            LastHeroId = data.LastHeroId;
            LastLevelId = data.LastLevelId;
            LastDifficulty = data.LastDifficulty > 0 ? data.LastDifficulty : _levels.DefaultDifficulty;
            SyncClearedFromHighest();
            if (data.UnlockedHeroIds == null)
            {
                return;
            }

            for (var i = 0; i < data.UnlockedHeroIds.Length; i++)
            {
                var heroId = data.UnlockedHeroIds[i];
                if (heroId > 0)
                {
                    _unlockedHeroes.Add(heroId);
                }
            }
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            SyncClearedFromHighest();
            var cleared = new List<int>(_cleared);
            cleared.Sort();
            var progress = new DifficultyProgressEntry[_highestCleared.Count];
            var p = 0;
            foreach (var pair in _highestCleared)
            {
                progress[p++] = new DifficultyProgressEntry
                {
                    Difficulty = pair.Key,
                    HighestClearedLevel = pair.Value
                };
            }

            var unlocked = new int[_unlockedHeroes.Count];
            var u = 0;
            foreach (var heroId in _unlockedHeroes)
            {
                unlocked[u++] = heroId;
            }

            var data = new LevelProgressSaveData
            {
                ClearedDifficulties = cleared.ToArray(),
                LastHeroId = LastHeroId,
                LastLevelId = LastLevelId,
                LastDifficulty = LastDifficulty,
                UnlockedHeroIds = unlocked,
                HighestClearedLevel = GetHighestClearedLevel(_levels.DefaultDifficulty),
                DifficultyProgress = progress
            };

            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        private void SyncClearedFromHighest()
        {
            var diffs = _levels.GetDifficulties();
            if (diffs == null)
            {
                return;
            }

            for (var i = 0; i < diffs.Count; i++)
            {
                var difficulty = diffs[i];
                var max = _levels.GetMaxLevel(difficulty);
                if (max > 0 && GetHighestClearedLevel(difficulty) >= max)
                {
                    _cleared.Add(difficulty);
                }
            }
        }

        private void WarnIfUnknownDifficulty(int difficulty)
        {
            if (_levels.GetMaxLevel(difficulty) <= 0)
            {
                AppLog.Warn(LogChannel.Level, $"Unknown difficulty={difficulty} (not in LevelConfig).");
            }
        }
    }
}
