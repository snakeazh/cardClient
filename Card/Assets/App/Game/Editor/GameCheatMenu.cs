using App.Bootstrap;
using App.Config;
using App.Game;
using App.Talent;
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

        [MenuItem("Debug/外挂/添加指定圣物", false, 16)]
        public static void OpenGrantRelic()
        {
            GrantRelicCheatWindow.Open();
        }

        [MenuItem("Debug/外挂/金币 +99999", false, 10)]
        public static void AddGold()
        {
            if (!TryGetSession(out var session))
            {
                return;
            }

            session.DebugAddGold();
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

            var id = ids[Random.Range(0, ids.Count)];
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
}
