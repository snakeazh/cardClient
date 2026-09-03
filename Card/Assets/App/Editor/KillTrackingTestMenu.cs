using System.Collections.Generic;
using App.Score;
using Framework.Save;
using UnityEditor;
using UnityEngine;

namespace App.Editor
{
    /// <summary>临时自测：逐回合击杀统计逻辑（编辑器模式直接跑，Ctrl+Alt+K）。</summary>
    internal static class KillTrackingTestMenu
    {
        private sealed class DummySave : ISaveService
        {
            public bool HasKey(string key) => false;
            public string GetString(string key, string defaultValue = "") => defaultValue;
            public void SetString(string key, string value) { }
            public int GetInt(string key, int defaultValue = 0) => defaultValue;
            public void SetInt(string key, int value) { }
            public float GetFloat(string key, float defaultValue = 0f) => defaultValue;
            public void SetFloat(string key, float value) { }
            public void DeleteKey(string key) { }
            public void DeleteAll() { }
            public void Save() { }
        }

        [MenuItem("Tools/自测击杀统计 %&k")]
        public static void Run()
        {
            var score = new ScoreService(new DummySave());

            // 回合1：先击杀 2 个，再入账积分 5 → 击杀记在第 1 条
            score.BeginRound();
            score.TrackStageKill();
            score.TrackStageKill();
            score.AwardRoundScore(5);

            // 回合2：无击杀，入账 3 → 补 0
            score.BeginRound();
            score.AwardRoundScore(3);

            // 回合3：积分换算为 0（chips 太少）→ 伤 0，随后击杀 1 → 记在第 3 条
            score.BeginRound();
            score.AwardRoundScore(0);
            score.TrackStageKill();

            // 回合4：只击杀未入账（关卡在此结束）→ kills 比 scores 多一条尾巴
            score.BeginRound();
            score.TrackStageKill();

            var ok = score.StageRoundScores.Count == 3;
            ok &= score.StageRoundKills.Count == 4;
            ok &= score.StageRoundKills[0] == 2 && score.StageRoundKills[1] == 0 && score.StageRoundKills[2] == 1;

            score.BeginStage();
            ok &= score.StageRoundKills.Count == 0 && score.StageRoundScores.Count == 0;

            Debug.Log("[KillTrackingTest] " + (ok
                ? "PASS scores=[5,3,0] kills=[2,0,1,1], BeginStage 清空 ✓"
                : $"FAIL scores=[{string.Join(",", score.StageRoundScores)}] kills=[{string.Join(",", score.StageRoundKills)}]"));
        }
    }
}
