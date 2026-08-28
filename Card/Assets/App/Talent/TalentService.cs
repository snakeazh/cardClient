using System;
using System.Collections.Generic;
using App.Config;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.Talent
{
    /// <summary>
    /// Indexes TalentConfig at construction. Ownership is count-based: each copy raises one level.
    /// Mutations stay in memory; Save() (or pause/quit auto-flush) writes disk.
    /// </summary>
    public sealed class TalentService : ITalentService
    {
        public const string SaveKey = "talent.v1";

        /// <summary>Copies required to raise one talent level.</summary>
        public const int CopiesPerLevel = TalentBalance.CopiesPerLevel;

        private readonly ISaveService _save;
        private readonly Dictionary<int, Dictionary<int, TalentConfig>> _rows =
            new Dictionary<int, Dictionary<int, TalentConfig>>();
        private readonly Dictionary<int, int> _maxLevel = new Dictionary<int, int>();
        private int[] _ids = Array.Empty<int>();
        private readonly Dictionary<int, int> _counts = new Dictionary<int, int>();
        private bool _dirty;

        public TalentService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            IndexRows();
            AppLog.Info(
                LogChannel.Talent,
                $"indexed: talents={_ids.Length}, rows={CountNested(_rows)}");
        }

        public bool IsDirty => _dirty;

        public IReadOnlyList<int> GetIds() => _ids;

        public int GetMaxLevel(int talentId)
        {
            return _maxLevel.TryGetValue(talentId, out var max) ? max : 0;
        }

        public int GetCount(int talentId)
        {
            return _counts.TryGetValue(talentId, out var count) ? count : 0;
        }

        public int GetLevel(int talentId)
        {
            var max = GetMaxLevel(talentId);
            if (max <= 0)
            {
                return 0;
            }

            var raw = GetCount(talentId) / CopiesPerLevel;
            return raw > max ? max : raw;
        }

        public bool IsOwned(int talentId) => GetLevel(talentId) > 0;

        public bool IsMaxLevel(int talentId)
        {
            var max = GetMaxLevel(talentId);
            return max > 0 && GetLevel(talentId) >= max;
        }

        public bool TryGet(int talentId, int level, out TalentConfig row)
        {
            if (_rows.TryGetValue(talentId, out var byLevel) &&
                byLevel.TryGetValue(level, out row))
            {
                return true;
            }

            row = null;
            return false;
        }

        public TalentConfig Get(int talentId, int level)
        {
            TryGet(talentId, level, out var row);
            return row;
        }

        public TalentSnapshot GetCurrent(int talentId)
        {
            var count = GetCount(talentId);
            var level = GetLevel(talentId);
            var max = GetMaxLevel(talentId);
            TryGet(talentId, level, out var config);
            TalentEntryConfig entry = null;
            if (config != null && config.TalentEntry > 0)
            {
                TalentEntryConfig.TryGet(config.TalentEntry, out entry);
            }

            return new TalentSnapshot(talentId, count, level, max, config, entry);
        }

        public IReadOnlyList<TalentSnapshot> GetOwned()
        {
            var list = new List<TalentSnapshot>();
            for (var i = 0; i < _ids.Length; i++)
            {
                var id = _ids[i];
                if (GetLevel(id) <= 0)
                {
                    continue;
                }

                list.Add(GetCurrent(id));
            }

            return list;
        }

        public int DrawRandomId()
        {
            return _ids.Length == 0 ? 0 : _ids[UnityEngine.Random.Range(0, _ids.Length)];
        }

        public TalentAddResult Add(int talentId, int amount = 1)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
            }

            WarnIfUnknown(talentId);
            var previousLevel = GetLevel(talentId);
            _counts.TryGetValue(talentId, out var current);
            _counts[talentId] = current + amount;
            _dirty = true;
            var snapshot = GetCurrent(talentId);
            if (snapshot.Level > previousLevel)
            {
                AppLog.Info(
                    LogChannel.Talent,
                    $"level up TalentId={talentId} {previousLevel}→{snapshot.Level} (count={snapshot.Count})");
            }

            return new TalentAddResult(previousLevel, snapshot);
        }

        public void Clear()
        {
            if (_counts.Count == 0)
            {
                return;
            }

            _counts.Clear();
            _dirty = true;
        }

        public void Load()
        {
            _counts.Clear();
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

            var data = JsonUtility.FromJson<TalentSaveData>(json);
            if (data?.Entries == null)
            {
                AppLog.Warn(LogChannel.Talent, "Failed to parse save data.");
                return;
            }

            for (var i = 0; i < data.Entries.Length; i++)
            {
                var entry = data.Entries[i];
                if (entry == null || entry.TalentId <= 0 || entry.Count <= 0)
                {
                    continue;
                }

                WarnIfUnknown(entry.TalentId);
                _counts.TryGetValue(entry.TalentId, out var current);
                _counts[entry.TalentId] = current + entry.Count;
            }
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var owned = new List<TalentSaveEntry>(_counts.Count);
            foreach (var pair in _counts)
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                owned.Add(new TalentSaveEntry
                {
                    TalentId = pair.Key,
                    Count = pair.Value
                });
            }

            owned.Sort((a, b) => a.TalentId.CompareTo(b.TalentId));
            var data = new TalentSaveData
            {
                Entries = owned.ToArray()
            };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        private void IndexRows()
        {
            foreach (var pair in TalentConfig.All)
            {
                var row = pair.Value;
                if (row == null || row.TalentId <= 0 || row.TalentLevel <= 0)
                {
                    continue;
                }

                if (!_rows.TryGetValue(row.TalentId, out var byLevel))
                {
                    byLevel = new Dictionary<int, TalentConfig>();
                    _rows[row.TalentId] = byLevel;
                }

                if (byLevel.ContainsKey(row.TalentLevel))
                {
                    AppLog.Warn(
                        LogChannel.Talent,
                        $"Duplicate TalentId={row.TalentId} Level={row.TalentLevel} (Id={row.Id}), later row wins.");
                }

                byLevel[row.TalentLevel] = row;
                if (!_maxLevel.TryGetValue(row.TalentId, out var max) || row.TalentLevel > max)
                {
                    _maxLevel[row.TalentId] = row.TalentLevel;
                }
            }

            var keys = new List<int>(_rows.Keys);
            keys.Sort();
            _ids = keys.ToArray();
        }

        private void WarnIfUnknown(int talentId)
        {
            if (TalentConfig.Count > 0 && !_rows.ContainsKey(talentId))
            {
                AppLog.Warn(LogChannel.Talent, $"Unknown TalentId={talentId} (not in TalentConfig).");
            }
        }

        private static int CountNested(Dictionary<int, Dictionary<int, TalentConfig>> map)
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
