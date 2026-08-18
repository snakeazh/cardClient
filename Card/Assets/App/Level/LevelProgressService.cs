using System;
using System.Collections.Generic;
using Framework.Save;
using UnityEngine;

namespace App.Level
{
    /// <summary>
    /// In-memory set of cleared difficulties with dirty-flag persistence via ISaveService.
    /// </summary>
    public sealed class LevelProgressService : ILevelProgressService
    {
        public const string SaveKey = "level.progress.v1";

        private readonly ISaveService _save;
        private readonly ILevelService _levels;
        private readonly HashSet<int> _cleared = new HashSet<int>();
        private readonly HashSet<int> _unlockedHeroes = new HashSet<int>();
        private bool _dirty;

        public LevelProgressService(ISaveService save, ILevelService levels)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        }

        public bool IsDirty => _dirty;

        public int LastHeroId { get; private set; }

        public int LastLevelId { get; private set; }

        public int HighestClearedLevel { get; private set; }

        public bool IsCleared(int difficulty)
        {
            return _cleared.Contains(difficulty);
        }

        public void MarkCleared(int levelId)
        {
            if (!_levels.TryGetById(levelId, out var snapshot) || snapshot == null)
            {
                Debug.LogWarning($"[LevelProgress] Unknown level Id={levelId}.");
                return;
            }

            if (snapshot.Level > HighestClearedLevel)
            {
                HighestClearedLevel = snapshot.Level;
                _dirty = true;
            }

            if (_levels.TryGetNext(snapshot.Difficulty, snapshot.Level, out _))
            {
                return;
            }

            if (!_cleared.Add(snapshot.Difficulty))
            {
                return;
            }

            _dirty = true;
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

            return snapshot.Level <= HighestClearedLevel + 1;
        }

        public IReadOnlyList<int> GetClearedDifficulties()
        {
            var list = new List<int>(_cleared);
            list.Sort();
            return list;
        }

        public void Clear()
        {
            var hadData = _cleared.Count > 0 ||
                          _unlockedHeroes.Count > 0 ||
                          LastHeroId != 0 ||
                          LastLevelId != 0 ||
                          HighestClearedLevel != 0;
            _cleared.Clear();
            _unlockedHeroes.Clear();
            LastHeroId = 0;
            LastLevelId = 0;
            HighestClearedLevel = 0;
            if (hadData)
            {
                _dirty = true;
            }
        }

        public void Load()
        {
            _cleared.Clear();
            _unlockedHeroes.Clear();
            LastHeroId = 0;
            LastLevelId = 0;
            HighestClearedLevel = 0;
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
                Debug.LogWarning("[LevelProgress] Failed to parse save data.");
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
                }
            }

            LastHeroId = data.LastHeroId;
            LastLevelId = data.LastLevelId;
            HighestClearedLevel = Math.Max(0, data.HighestClearedLevel);
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

            var cleared = GetClearedDifficulties();
            var unlocked = new int[_unlockedHeroes.Count];
            var u = 0;
            foreach (var heroId in _unlockedHeroes)
            {
                unlocked[u++] = heroId;
            }

            var data = new LevelProgressSaveData
            {
                ClearedDifficulties = new int[cleared.Count],
                LastHeroId = LastHeroId,
                LastLevelId = LastLevelId,
                UnlockedHeroIds = unlocked,
                HighestClearedLevel = HighestClearedLevel
            };
            for (var i = 0; i < cleared.Count; i++)
            {
                data.ClearedDifficulties[i] = cleared[i];
            }

            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        private void WarnIfUnknownDifficulty(int difficulty)
        {
            if (_levels.GetMaxLevel(difficulty) <= 0)
            {
                Debug.LogWarning($"[LevelProgress] Unknown difficulty={difficulty} (not in LevelConfig).");
            }
        }
    }
}
