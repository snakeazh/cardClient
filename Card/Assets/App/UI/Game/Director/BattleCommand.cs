using System;
using App.Game;

namespace App.UI.Game.Director
{
    /// <summary>
    /// 演出命令：封装一次有时序的表现动作（发牌/翻牌/攻击/数值跳动/等待）。
    /// 由 <see cref="BattleDirector"/> 排队顺序执行；Execute 必须在所有分支调用 onComplete，否则队列卡死。
    /// 命令只依赖 <see cref="IBattleStage"/> 窄口，不碰 GameSession 本体。
    /// </summary>
    public abstract class BattleCommand
    {
        public abstract string Name { get; }

        public abstract void Execute(IBattleStage stage, Action onComplete);

        /// <summary>订阅一次性完成事件；触发后自动退订并回调。</summary>
        protected static void AwaitOnce(Action<Action> subscribe, Action<Action> unsubscribe, Action onComplete)
        {
            Action handler = null;
            handler = () =>
            {
                unsubscribe(handler);
                onComplete();
            };
            subscribe(handler);
        }
    }
}
