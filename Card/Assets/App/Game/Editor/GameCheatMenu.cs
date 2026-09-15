using System;
using System.Collections.Generic;
using App.Bootstrap;
using App.Config;
using App.Game;
using App.Net;
using App.Talent;
using App.Wallet;
using UnityEditor;
using UnityEngine;

namespace App.Game.Editor
{
    /// <summary>
    /// 编辑器外挂菜单（Debug/外挂）。仅 Play 模式生效，全部走 GameSession / ITalentService 的 UNITY_EDITOR 调试 API，不进真机包。
    /// </summary>
    public static class GameCheatMenu
    {
        private const string GodMenuPath = "Debug/外挂/无敌模式";
        private const string OneHitMenuPath = "Debug/外挂/一击必杀";
        private const string ExtraAttackMenuPath = "Debug/外挂/强制连击";
        private const string ForceMissMenuPath = "Debug/外挂/强制MISS";

        [MenuItem("Debug/外挂/添加指定圣物", false, 16)]
        public static void OpenGrantRelic()
        {
            GrantRelicCheatWindow.Open();
        }

        [MenuItem("Debug/外挂/指定关卡词缀", false, 17)]
        public static void OpenGrantBossEntry()
        {
            GrantBossEntryCheatWindow.Open();
        }

        [MenuItem("Debug/外挂/局内金币 +99999", false, 10)]
        public static void AddGold()
        {
            if (!TryGetSession(out var session))
            {
                return;
            }

            session.DebugAddGold();
        }

        [MenuItem("Debug/外挂/局外金币 +99999", false, 10)]
        public static async void AddWalletGold()
        {
            if (!Application.isPlaying || !AppServices.IsReady)
            {
                Debug.LogWarning("需要在 Play 模式且启动流程完成后使用");
                return;
            }

            if (!GameApi.IsReady)
            {
                Debug.LogWarning("未连接服务器，无法发放局外金币");
                return;
            }

            try
            {
                var profile = await GameApi.Client.DebugGrantGoldAsync(99999);
                GameApi.ApplyProfile(profile);
                var wallet = AppServices.Resolve<IWalletService>();
                Debug.Log($"[外挂] 局外金币 +99999，当前余额 {wallet.Gold}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[外挂] 发放局外金币失败: " + ex.Message);
            }
        }

        [MenuItem("Debug/外挂/随机获得一个天赋", false, 14)]
        public static void GrantRandomTalent()
        {
            if (!Application.isPlaying || !AppServices.IsReady)
            {
                Debug.LogWarning("需要在 Play 模式且启动流程完成后使用");
                return;
            }

            var talent = AppServices.Resolve<ITalentService>();
            var ids = talent.GetIds();
            if (ids.Count == 0)
            {
                return;
            }

            var id = ids[UnityEngine.Random.Range(0, ids.Count)];
            var result = talent.Add(id);
            talent.Save();
            Debug.Log($"[外挂] 获得天赋 {id}，等级 {result.PreviousLevel} -> {result.Current.Level}");
        }

        [MenuItem("Debug/外挂/回满血", false, 11)]
        public static void FullHp()
        {
            if (!TryGetSession(out var session))
            {
                return;
            }

            session.DebugFullHp();
        }

        [MenuItem("Debug/外挂/技能次数+99（搓牌 透视 替换）", false, 12)]
        public static void MaxSkillCharges()
        {
            if (!TryGetSession(out var session))
            {
                return;
            }

            session.DebugMaxSkillCharges();
        }

        [MenuItem("Debug/外挂/跳过本关（直接进商店）", false, 13)]
        public static void SkipStage()
        {
            if (!TryGetSession(out var session))
            {
                return;
            }

            session.DebugSkipStage();
        }

        [MenuItem("Debug/外挂/直接击杀当前怪物 _k", false, 15)]
        public static void KillCurrentEnemy()
        {
            if (!TryGetSession(out var session))
            {
                return;
            }

            session.DebugKillCurrentEnemy();
        }

        [MenuItem(GodMenuPath, false, 20)]
        public static void ToggleGodMode()
        {
            GameSession.DebugGodMode = !GameSession.DebugGodMode;
            Menu.SetChecked(GodMenuPath, GameSession.DebugGodMode);
            Debug.Log($"[外挂] 无敌模式 {(GameSession.DebugGodMode ? "开启" : "关闭")}");
        }

        [MenuItem(OneHitMenuPath, false, 21)]
        public static void ToggleOneHitKill()
        {
            GameSession.DebugOneHitKill = !GameSession.DebugOneHitKill;
            Menu.SetChecked(OneHitMenuPath, GameSession.DebugOneHitKill);
            Debug.Log($"[外挂] 一击必杀 {(GameSession.DebugOneHitKill ? "开启" : "关闭")}");
        }

        [MenuItem(ExtraAttackMenuPath, false, 22)]
        public static void ToggleForceExtraAttack()
        {
            GameSession.DebugForceExtraAttack = !GameSession.DebugForceExtraAttack;
            Menu.SetChecked(ExtraAttackMenuPath, GameSession.DebugForceExtraAttack);
            Debug.Log($"[外挂] 强制连击 {(GameSession.DebugForceExtraAttack ? "开启" : "关闭")}");
        }

        [MenuItem(ForceMissMenuPath, false, 23)]
        public static void ToggleForceMiss()
        {
            GameSession.DebugForceMiss = !GameSession.DebugForceMiss;
            Menu.SetChecked(ForceMissMenuPath, GameSession.DebugForceMiss);
            Debug.Log($"[外挂] 强制MISS {(GameSession.DebugForceMiss ? "开启" : "关闭")}");
        }

        internal static bool TryGetSession(out GameSession session)
        {
            if (!Application.isPlaying || !AppServices.IsReady)
            {
                Debug.LogWarning("需要在 Play 模式且启动流程完成后（已进入对局）使用");
                session = null;
                return false;
            }

            session = AppServices.Resolve<GameSession>();
            if (session == null)
            {
                Debug.LogWarning("GameSession 未注册");
                return false;
            }

            return true;
        }
    }

    /// <summary>局内测试：输入 RelicConfig.Id 直接加入本局已持有。</summary>
    public sealed class GrantRelicCheatWindow : EditorWindow
    {
        private int _relicId;
        private Vector2 _scroll;

        public static void Open()
        {
            var window = GetWindow<GrantRelicCheatWindow>("添加圣物");
            window.minSize = new Vector2(320f, 160f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Play 对局中输入圣物 Id，立即加入本局（不扣金币、不占上限）");
            EditorGUILayout.Space(4f);
            _relicId = EditorGUILayout.IntField("圣物 Id", _relicId);

            RelicConfig relic = null;
            if (Application.isPlaying)
            {
                relic = RelicConfig.Get(_relicId);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("名称", relic != null ? relic.Name : "—");
                EditorGUILayout.TextField("效果", relic != null ? relic.Desc : "—");
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || relic == null))
            {
                if (GUILayout.Button("添加到本局", GUILayout.Height(28f)))
                {
                    Grant();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("需要在 Play 模式且已进入对局。", MessageType.Info);
                return;
            }

            DrawOwned();
        }

        private void Grant()
        {
            if (!GameCheatMenu.TryGetSession(out var session))
            {
                return;
            }

            session.DebugGrantRelic(_relicId);
        }

        private void DrawOwned()
        {
            if (!AppServices.IsReady)
            {
                return;
            }

            var session = AppServices.Resolve<GameSession>();
            var ids = session?.Run?.RelicConfigIds;
            if (ids == null || ids.Count == 0)
            {
                EditorGUILayout.HelpBox("本局还没有圣物。", MessageType.None);
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"已持有 {ids.Count} 件");
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(180f));
            for (var i = 0; i < ids.Count; i++)
            {
                var owned = RelicConfig.Get(ids[i]);
                var label = owned != null ? $"{owned.Id}  {owned.Name}" : ids[i].ToString();
                EditorGUILayout.LabelField(label);
            }

            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>局内测试：指定本关 BossEntryConfig（敌人闪避等词缀），立即写入 LevelEntryIds。</summary>
    public sealed class GrantBossEntryCheatWindow : EditorWindow
    {
        private int _entryId = 104;
        private string _filter = string.Empty;
        private Vector2 _catalogScroll;
        private Vector2 _ownedScroll;

        public static void Open()
        {
            var window = GetWindow<GrantBossEntryCheatWindow>("关卡词缀");
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Play 对局中添加/移除本关词缀。闪避等即时生效；窃取/手牌压缩等要等下一手。");
            EditorGUILayout.Space(4f);
            _entryId = EditorGUILayout.IntField("词缀 Id", _entryId);

            BossEntryConfig entry = null;
            if (Application.isPlaying)
            {
                entry = BossEntryConfig.Get(_entryId);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("名称", entry != null ? entry.Name : "—");
                EditorGUILayout.TextField("效果", entry != null ? entry.Desc : "—");
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!Application.isPlaying || entry == null))
            {
                if (GUILayout.Button("添加到本关", GUILayout.Height(28f)))
                {
                    Grant(_entryId);
                }
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying || entry == null))
            {
                if (GUILayout.Button("从本关移除", GUILayout.Height(28f)))
                {
                    Remove(_entryId);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("需要在 Play 模式且已进入对局。", MessageType.Info);
                return;
            }

            DrawOwned();
            EditorGUILayout.Space(8f);
            DrawCatalog();
        }

        private void Grant(int entryId)
        {
            if (!GameCheatMenu.TryGetSession(out var session))
            {
                return;
            }

            session.DebugGrantBossEntry(entryId);
            Repaint();
        }

        private void Remove(int entryId)
        {
            if (!GameCheatMenu.TryGetSession(out var session))
            {
                return;
            }

            session.DebugRemoveBossEntry(entryId);
            Repaint();
        }

        private void DrawOwned()
        {
            if (!AppServices.IsReady)
            {
                return;
            }

            var session = AppServices.Resolve<GameSession>();
            var ids = session?.Run?.LevelEntryIds;
            EditorGUILayout.LabelField($"本关词缀 {(ids != null ? ids.Count : 0)} 条");
            if (ids == null || ids.Count == 0)
            {
                EditorGUILayout.HelpBox("本关还没有词缀。", MessageType.None);
                return;
            }

            _ownedScroll = EditorGUILayout.BeginScrollView(_ownedScroll, GUILayout.MaxHeight(120f));
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var owned = BossEntryConfig.Get(id);
                EditorGUILayout.BeginHorizontal();
                var label = owned != null ? $"{owned.Id}  {owned.Name}" : id.ToString();
                EditorGUILayout.LabelField(label);
                if (GUILayout.Button("移除", GUILayout.Width(52f)))
                {
                    Remove(id);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("清空本关词缀"))
            {
                if (GameCheatMenu.TryGetSession(out var sessionClear))
                {
                    sessionClear.DebugClearBossEntries();
                    Repaint();
                }
            }
        }

        private void DrawCatalog()
        {
            EditorGUILayout.LabelField("全部词缀");
            _filter = EditorGUILayout.TextField("筛选", _filter);
            var rows = new List<BossEntryConfig>(BossEntryConfig.Count);
            foreach (var pair in BossEntryConfig.All)
            {
                if (pair.Value != null && MatchFilter(pair.Value, _filter))
                {
                    rows.Add(pair.Value);
                }
            }

            rows.Sort((a, b) => a.Id.CompareTo(b.Id));
            _catalogScroll = EditorGUILayout.BeginScrollView(_catalogScroll);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                EditorGUILayout.BeginHorizontal();
                var tip = string.IsNullOrEmpty(row.Desc) ? row.Name : row.Desc;
                EditorGUILayout.LabelField(new GUIContent($"{row.Id}  {row.Name}", tip));
                if (GUILayout.Button("添加", GUILayout.Width(52f)))
                {
                    _entryId = row.Id;
                    Grant(row.Id);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private static bool MatchFilter(BossEntryConfig row, string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            if (row.Id.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(row.Name) &&
                row.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return !string.IsNullOrEmpty(row.Desc) &&
                   row.Desc.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
