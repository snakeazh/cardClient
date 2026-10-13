using System;
using Framework.Save;

namespace App.PvpScore
{
    /// <summary>局外 PVP 分数本地缓存，键 pvp.score.v1；变更由服务器主档灌入。</summary>
    public sealed class PvpScoreService : IPvpScoreService
    {
        public const string SaveKey = "pvp.score.v1";

        private readonly ISaveService _save;
        private int _score;
        private bool _dirty;

        public PvpScoreService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public int Score => _score;

        public event Action Changed;

        public void ReplaceFromServer(int score)
        {
            var next = score < 0 ? 0 : score;
            if (_score == next)
            {
                return;
            }

            _score = next;
            _dirty = true;
            Changed?.Invoke();
        }

        public void Load()
        {
            _score = _save.GetInt(SaveKey, 0);
            if (_score < 0)
            {
                _score = 0;
            }

            _dirty = false;
            Changed?.Invoke();
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            _save.SetInt(SaveKey, _score);
            _save.Save();
            _dirty = false;
        }
    }
}
