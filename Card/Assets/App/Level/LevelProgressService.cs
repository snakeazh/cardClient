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
        private bool _dirty;

        public LevelProgressService(ISaveService save, ILevelService levels)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        }

        public bool IsDirty => _dirty;

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

        public IReadOnlyList<int> GetClearedDifficulties()
        {
            var list = new List<int>(_cleared);
            list.Sort();
            return list;
        }

        public void Clear()
        {
            if (_cleared.Count == 0)
            {
                return;
            }

            _cleared.Clear();
            _dirty = true;
        }

        public void Load()
        {
            _cleared.Clear();
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
            if (data?.ClearedDifficulties == null)
            {
                Debug.LogWarning("[LevelProgress] Failed to parse save data.");
                return;
            }

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

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var cleared = GetClearedDifficulties();
            var data = new LevelProgressSaveData
            {
                ClearedDifficulties = new int[cleared.Count]
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
