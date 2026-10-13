using System;
using System.Collections.Generic;
using App.Game;
using Framework.Log;

namespace App.UI.Game.Director
{
    /// <summary>
    /// 演出驱动器：命令队列，回调链顺序执行，同一时刻只播一条。
    /// 离开时 <see cref="Clear"/> 丢弃未播命令（进行中的动画由各自完成回调自然收尾）。
    /// </summary>
    public sealed class BattleDirector
    {
        private readonly IBattleStage _stage;
        private readonly Queue<BattleCommand> _queue = new Queue<BattleCommand>(8);
        private bool _running;

        public BattleDirector(IBattleStage stage)
        {
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        }

        /// <summary>最后一条命令播完、队列排空时触发。</summary>
        public event Action Drained;

        public bool IsBusy => _running || _queue.Count > 0;

        public void Enqueue(BattleCommand command)
        {
            if (command == null)
            {
                return;
            }

            _queue.Enqueue(command);
            Pump();
        }

        public void Clear()
        {
            _queue.Clear();
        }

        private void Pump()
        {
            if (_running)
            {
                return;
            }

            if (_queue.Count == 0)
            {
                Drained?.Invoke();
                return;
            }

            _running = true;
            var command = _queue.Dequeue();
            AppLog.Info(LogChannel.UI, "battle director: " + command.Name);
            var done = false;
            command.Execute(_stage, () =>
            {
                if (done)
                {
                    return;
                }

                done = true;
                _running = false;
                Pump();
            });
        }
    }
}
