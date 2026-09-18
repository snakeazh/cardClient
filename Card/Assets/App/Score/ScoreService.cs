using System;
using System.Collections.Generic;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.Score
{
    /// <summary>
    /// In-memory chapter score; mutations save immediately via ISaveService.
    /// </summary>
    public sealed class ScoreService : IScoreService
    {
        public const string SaveKey = "score.v1";

        private readonly ISaveService _save;
        private readonly List<int> _stageRoundScores = new List<int>();
        private readonly List<int> _stageRoundKills = new List<int>();
        /// <summary>直接击杀（不走牌局积分）的伤害旁路记录，按行与积分对应；仅结算明细显示，不入 Stage/Total 积分。</summary>
        private readonly List<int> _stageDirectKillDamages = new List<int>();
        private int _lastKillIndex = -1;
        private int _total;
        private int _stage;
        private int _round;
        private int _grantedGold;
        private bool _dirty;
        private bool _roundScoreAwarded;

        public ScoreService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public bool IsDirty => _dirty;

        public ScoreSnapshot Current => new ScoreSnapshot(_total, _stage, _round);

        public IReadOnlyList<int> StageRoundScores => _stageRoundScores;

        public IReadOnlyList<int> StageRoundKills => _stageRoundKills;

        public IReadOnlyList<int> StageDirectKillDamages => _stageDirectKillDamages;

        public void TrackStageKill()
        {
            // 比牌流：击杀先于本回合积分入账 → 挂下一行，AwardRoundScore 补齐对应积分行；
            // 亮牌/无人争夺流：攻击阶段在入账之后 → 挂最后一行。
            var index = _roundScoreAwarded ? _stageRoundScores.Count - 1 : _stageRoundScores.Count;
            while (_stageRoundKills.Count <= index)
            {
                _stageRoundKills.Add(0);
            }

            _stageRoundKills[index]++;
            _lastKillIndex = index;
            _dirty = true;
            Save();
        }

        /// <summary>把直接击杀（不走牌局积分，编辑器外挂）造成的伤害补记到最近一次击杀行，仅供结算明细显示。</summary>
        public void RecordDirectKillDamage(int damage)
        {
            if (damage <= 0 || _lastKillIndex < 0)
            {
                return;
            }

            while (_stageDirectKillDamages.Count <= _lastKillIndex)
            {
                _stageDirectKillDamages.Add(0);
            }

            _stageDirectKillDamages[_lastKillIndex] += damage;
        }

        public int CollectableGold => ScoreBalance.PointsToGold(_total);

        public int GrantedGold => _grantedGold;

        public void BeginChapter()
        {
            _stageRoundScores.Clear();
            _stageRoundKills.Clear();
            _stageDirectKillDamages.Clear();
            _lastKillIndex = -1;
            if (_total == 0 && _stage == 0 && _round == 0 && _grantedGold == 0)
            {
                return;
            }

            _total = 0;
            _stage = 0;
            _round = 0;
            _grantedGold = 0;
            _dirty = true;
            Save();
        }

        public void BeginStage()
        {
            _stageRoundScores.Clear();
            _stageRoundKills.Clear();
            _stageDirectKillDamages.Clear();
            _lastKillIndex = -1;
            if (_stage == 0 && _round == 0)
            {
                return;
            }

            _stage = 0;
            _round = 0;
            _dirty = true;
            Save();
        }

        public void BeginRound()
        {
            _roundScoreAwarded = false;
            if (_round == 0)
            {
                return;
            }

            _round = 0;
            _dirty = true;
            Save();
        }

        public void AwardRoundScore(int chipsWon)
        {
            var points = ScoreBalance.ChipsToPoints(chipsWon);
            if (points <= 0)
            {
                _stageRoundScores.Add(0);
                PadRoundKills();
                BeginRound();
                _roundScoreAwarded = true;
                return;
            }

            _round = points;
            _stage += points;
            _total += points;
            _stageRoundScores.Add(points);
            PadRoundKills();
            _roundScoreAwarded = true;
            _dirty = true;
            Save();
        }

        /// <summary>回合无击杀时补 0，保证与 <see cref="_stageRoundScores"/> 一一对应。</summary>
        private void PadRoundKills()
        {
            while (_stageRoundKills.Count < _stageRoundScores.Count)
            {
                _stageRoundKills.Add(0);
            }
        }

        public int CollectGoldDelta()
        {
            var collectable = CollectableGold;
            var delta = collectable - _grantedGold;
            if (delta <= 0)
            {
                return 0;
            }

            _grantedGold += delta;
            _dirty = true;
            Save();
            return delta;
        }

        public void Load()
        {
            _total = 0;
            _stage = 0;
            _round = 0;
            _grantedGold = 0;
            _stageRoundScores.Clear();
            _stageDirectKillDamages.Clear();
            _lastKillIndex = -1;
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

            var data = JsonUtility.FromJson<ScoreSaveData>(json);
            if (data == null)
            {
                AppLog.Warn(LogChannel.Score, "Failed to parse save data.");
                return;
            }

            _total = ClampNonNegative(data.Total);
            _stage = ClampNonNegative(data.Stage);
            _round = ClampNonNegative(data.Round);
            _grantedGold = ClampNonNegative(data.GrantedGold);
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var data = new ScoreSaveData
            {
                Total = _total,
                Stage = _stage,
                Round = _round,
                GrantedGold = _grantedGold
            };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

        private static int ClampNonNegative(int value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
