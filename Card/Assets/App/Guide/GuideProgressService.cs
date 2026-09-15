using System;
using App.Net;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.Guide
{
    public sealed class GuideProgressService : IGuideProgressService
    {
        public const string SaveKey = "guide.progress.v1";

        private readonly ISaveService _save;
        private int[] _completed = Array.Empty<int>();
        private bool _dirty;

        public GuideProgressService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public bool IsGroupCompleted(int groupId)
        {
            if (groupId <= 0 || _completed == null)
            {
                return false;
            }

            for (var i = 0; i < _completed.Length; i++)
            {
                if (_completed[i] == groupId)
                {
                    return true;
                }
            }

            return false;
        }

        public void MarkGroupCompleted(int groupId)
        {
            if (groupId <= 0 || IsGroupCompleted(groupId))
            {
                return;
            }

            if (!MetaLocalAuthority.AllowsLocalMutation("Guide.MarkGroupCompleted"))
            {
                return;
            }

            var next = new int[_completed.Length + 1];
            Array.Copy(_completed, next, _completed.Length);
            next[_completed.Length] = groupId;
            _completed = next;
            _dirty = true;
            Save();
        }

        public void ReplaceFromServer(int[] completedGroupIds)
        {
            _completed = completedGroupIds == null || completedGroupIds.Length == 0
                ? Array.Empty<int>()
                : (int[])completedGroupIds.Clone();
            _dirty = true;
        }

        public void ResetAll()
        {
            if (_completed.Length == 0)
            {
                return;
            }

            _completed = Array.Empty<int>();
            _dirty = true;
            Save();
        }

        public void Load()
        {
            _completed = Array.Empty<int>();
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

            var data = JsonUtility.FromJson<GuideProgressSaveData>(json);
            if (data == null)
            {
                AppLog.Warn(LogChannel.UI, "Failed to parse guide progress save data.");
                return;
            }

            _completed = data.CompletedGroupIds ?? Array.Empty<int>();
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var data = new GuideProgressSaveData { CompletedGroupIds = _completed };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        [Serializable]
        private sealed class GuideProgressSaveData
        {
            public int[] CompletedGroupIds = Array.Empty<int>();
        }
    }
}
