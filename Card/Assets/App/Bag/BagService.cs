using System;
using System.Collections.Generic;
using App.Config;
using Framework.Save;
using UnityEngine;

namespace App.Bag
{
    /// <summary>
    /// In-memory bag with dirty-flag persistence via ISaveService.
    /// Mutations stay in memory; call Save() (or rely on pause/quit auto-flush) to flush disk.
    /// </summary>
    public sealed class BagService : IBagService
    {
        public const string SaveKey = "bag.v1";

        private readonly ISaveService _save;
        private readonly Dictionary<int, int> _counts = new Dictionary<int, int>();
        private bool _dirty;

        public BagService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public bool IsDirty => _dirty;

        public int GetCount(int itemId)
        {
            return _counts.TryGetValue(itemId, out var count) ? count : 0;
        }

        public bool Has(int itemId, int amount = 1)
        {
            if (amount <= 0)
            {
                return true;
            }

            return GetCount(itemId) >= amount;
        }

        public void Add(int itemId, int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
            }

            WarnIfUnknownItem(itemId);
            _counts.TryGetValue(itemId, out var current);
            _counts[itemId] = current + amount;
            _dirty = true;
        }

        public bool TryRemove(int itemId, int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
            }

            if (!Has(itemId, amount))
            {
                return false;
            }

            var next = GetCount(itemId) - amount;
            if (next <= 0)
            {
                _counts.Remove(itemId);
            }
            else
            {
                _counts[itemId] = next;
            }

            _dirty = true;
            return true;
        }

        public IReadOnlyList<BagEntry> GetAll()
        {
            var list = new List<BagEntry>(_counts.Count);
            foreach (var pair in _counts)
            {
                if (pair.Value > 0)
                {
                    list.Add(new BagEntry(pair.Key, pair.Value));
                }
            }

            list.Sort((a, b) => a.ItemId.CompareTo(b.ItemId));
            return list;
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

            var data = JsonUtility.FromJson<BagSaveData>(json);
            if (data?.Entries == null)
            {
                Debug.LogWarning("[Bag] Failed to parse save data.");
                return;
            }

            for (var i = 0; i < data.Entries.Length; i++)
            {
                var entry = data.Entries[i];
                if (entry == null || entry.Count <= 0)
                {
                    continue;
                }

                WarnIfUnknownItem(entry.ItemId);
                _counts.TryGetValue(entry.ItemId, out var current);
                _counts[entry.ItemId] = current + entry.Count;
            }
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var entries = GetAll();
            var data = new BagSaveData
            {
                Entries = new BagEntry[entries.Count]
            };
            for (var i = 0; i < entries.Count; i++)
            {
                data.Entries[i] = entries[i];
            }

            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        private static void WarnIfUnknownItem(int itemId)
        {
            if (ItemConfig.Count > 0 && !ItemConfig.TryGet(itemId, out _))
            {
                Debug.LogWarning($"[Bag] Unknown item Id={itemId} (not in ItemConfig).");
            }
        }
    }
}
