using System;
using System.Collections.Generic;
using App.Config;
using CardShare.Contracts.Config;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.Unlock
{
    /// <summary>
    /// 按 UnlockConditionConfig 累计进度并解锁遗物，变更即落盘 unlock.condition.v1。
    /// </summary>
    public sealed class UnlockConditionService : IUnlockConditionService
    {
        public const string SaveKey = "unlock.condition.v1";

        private readonly ISaveService _save;
        private readonly Dictionary<int, int> _progress = new Dictionary<int, int>();
        private readonly HashSet<int> _unlockedRelics = new HashSet<int>();
        private readonly HashSet<int> _shopSnapshot = new HashSet<int>();
        private readonly List<int> _pendingUnlockRelicIds = new List<int>();
        private bool _dirty;

        public UnlockConditionService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public bool IsRelicUnlocked(int relicId)
        {
            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                return false;
            }

            if (relic.UnlockConditionId <= 0)
            {
                return true;
            }

            if (_unlockedRelics.Contains(relicId))
            {
                return true;
            }

            return MeetsCondition(relic.UnlockConditionId);
        }

        public int GetProgress(int conditionId)
        {
            if (conditionId <= 0)
            {
                return 0;
            }

            _progress.TryGetValue(conditionId, out var amount);
            return amount < 0 ? 0 : amount;
        }

        public bool IsRelicInShopPool(int relicId)
        {
            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                return false;
            }

            if (relic.UnlockConditionId <= 0)
            {
                return true;
            }

            return _shopSnapshot.Contains(relicId);
        }

        public void BeginRun()
        {
            SyncUnlockedRelics(toast: false);
            _shopSnapshot.Clear();
            foreach (var kv in RelicConfig.All)
            {
                var relic = kv.Value;
                if (relic != null && IsRelicUnlocked(relic.Id))
                {
                    _shopSnapshot.Add(relic.Id);
                }
            }

            Save();
        }

        public IReadOnlyList<int> ConsumePendingUnlockRelicIds()
        {
            if (_pendingUnlockRelicIds.Count == 0)
            {
                return Array.Empty<int>();
            }

            var ids = _pendingUnlockRelicIds.ToArray();
            _pendingUnlockRelicIds.Clear();
            return ids;
        }

        public void Report(ContidionType type, int amount = 1)
        {
            if (amount <= 0)
            {
                return;
            }

            var changed = false;
            foreach (var kv in UnlockConditionConfig.All)
            {
                var condition = kv.Value;
                if (condition == null || condition.Type != type)
                {
                    continue;
                }

                _progress.TryGetValue(condition.Id, out var current);
                int next;
                if (UsesMaxProgress(type))
                {
                    next = Math.Max(current, amount);
                }
                else
                {
                    if (condition.StackedValue <= 0)
                    {
                        continue;
                    }

                    next = current + amount * condition.StackedValue;
                }

                if (next == current)
                {
                    continue;
                }

                _progress[condition.Id] = next;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            _dirty = true;
            SyncUnlockedRelics(toast: true);
            Save();
        }

        public void ReplaceFromServer(IReadOnlyList<UnlockConditionProgressEntry> progress)
        {
            _progress.Clear();
            _unlockedRelics.Clear();
            if (progress != null)
            {
                for (var i = 0; i < progress.Count; i++)
                {
                    var entry = progress[i];
                    if (entry == null || entry.ConditionId <= 0 || entry.Amount <= 0)
                    {
                        continue;
                    }

                    _progress[entry.ConditionId] = entry.Amount;
                }
            }

            _dirty = true;
            SyncUnlockedRelics(toast: false);
        }

        private static bool UsesMaxProgress(ContidionType type)
        {
            return type == ContidionType.ClearDifficulty ||
                   type == ContidionType.NumberOfCoinsOwned ||
                   type == ContidionType.OneDamage ||
                   type == ContidionType.SingleDamage;
        }

        public void Load()
        {
            _progress.Clear();
            _unlockedRelics.Clear();
            _shopSnapshot.Clear();
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

            var data = JsonUtility.FromJson<UnlockConditionSaveData>(json);
            if (data == null)
            {
                AppLog.Warn(LogChannel.Config, "Failed to parse unlock condition save data.");
                return;
            }

            if (data.Progress != null)
            {
                for (var i = 0; i < data.Progress.Length; i++)
                {
                    var entry = data.Progress[i];
                    if (entry == null || entry.ConditionId <= 0 || entry.Amount <= 0)
                    {
                        continue;
                    }

                    _progress[entry.ConditionId] = entry.Amount;
                }
            }

            if (data.UnlockedRelicIds != null)
            {
                for (var i = 0; i < data.UnlockedRelicIds.Length; i++)
                {
                    var relicId = data.UnlockedRelicIds[i];
                    if (relicId > 0)
                    {
                        _unlockedRelics.Add(relicId);
                    }
                }
            }

            SyncUnlockedRelics(toast: false);
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var progress = new UnlockConditionProgressEntry[_progress.Count];
            var p = 0;
            foreach (var pair in _progress)
            {
                progress[p++] = new UnlockConditionProgressEntry
                {
                    ConditionId = pair.Key,
                    Amount = pair.Value
                };
            }

            var unlocked = new int[_unlockedRelics.Count];
            var u = 0;
            foreach (var relicId in _unlockedRelics)
            {
                unlocked[u++] = relicId;
            }

            var data = new UnlockConditionSaveData
            {
                Progress = progress,
                UnlockedRelicIds = unlocked
            };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        private bool MeetsCondition(int conditionId)
        {
            var condition = UnlockConditionConfig.Get(conditionId);
            if (condition == null)
            {
                return false;
            }

            _progress.TryGetValue(conditionId, out var amount);
            return amount >= condition.Value;
        }

        private void SyncUnlockedRelics(bool toast)
        {
            foreach (var kv in RelicConfig.All)
            {
                var relic = kv.Value;
                if (relic == null || relic.UnlockConditionId <= 0 || _unlockedRelics.Contains(relic.Id))
                {
                    continue;
                }

                if (!MeetsCondition(relic.UnlockConditionId))
                {
                    continue;
                }

                _unlockedRelics.Add(relic.Id);
                _dirty = true;
                if (toast)
                {
                    _pendingUnlockRelicIds.Add(relic.Id);
                }
            }
        }
    }

    [Serializable]
    public sealed class UnlockConditionProgressEntry
    {
        public int ConditionId;
        public int Amount;
    }

    [Serializable]
    public sealed class UnlockConditionSaveData
    {
        public UnlockConditionProgressEntry[] Progress = Array.Empty<UnlockConditionProgressEntry>();
        public int[] UnlockedRelicIds = Array.Empty<int>();
    }
}
