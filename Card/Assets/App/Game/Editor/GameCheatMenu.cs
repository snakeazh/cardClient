using App.Bootstrap;
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

        private static bool TryGetSession(out GameSession session)
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
}
