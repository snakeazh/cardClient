using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace App.Net
{
    /// <summary>长链接用户操作命令：一次点击 = 一个命令，由 PvpWsCommandInvoker 串行下发，避免连击/乱序。</summary>
    public abstract class PvpWsCommand
    {
        public abstract string Name { get; }

        public abstract Task ExecuteAsync(PvpMatchSession session);

        /// <summary>无参数的重复命令在队列中只保留一个（连击合并）。带参数的命令不合并。</summary>
        public virtual bool Coalescible => false;
    }

    public sealed class PvpWsPickCommand : PvpWsCommand
    {
        private readonly int[] _indexes;

        public PvpWsPickCommand(int[] indexes)
        {
            _indexes = indexes;
        }

        public override string Name => "pick";

        public override Task ExecuteAsync(PvpMatchSession session) => session.PickAsync(_indexes);
    }

    public sealed class PvpWsRubCommand : PvpWsCommand
    {
        private readonly int _index;

        public PvpWsRubCommand(int index)
        {
            _index = index;
        }

        public override string Name => "rub";

        public override Task ExecuteAsync(PvpMatchSession session) => session.RubAsync(_index);
    }

    public sealed class PvpWsReplaceCommand : PvpWsCommand
    {
        public override string Name => "replace";

        public override bool Coalescible => true;

        public override Task ExecuteAsync(PvpMatchSession session) => session.ReplaceAsync();
    }

    public sealed class PvpWsPeekCommand : PvpWsCommand
    {
        public override string Name => "peek";

        public override bool Coalescible => true;

        public override Task ExecuteAsync(PvpMatchSession session) => session.PeekAsync();
    }

    public sealed class PvpWsShowdownCommand : PvpWsCommand
    {
        public override string Name => "showdown";

        public override bool Coalescible => true;

        public override Task ExecuteAsync(PvpMatchSession session) => session.ShowdownAsync();
    }

    public sealed class PvpWsStartQueueCommand : PvpWsCommand
    {
        public override string Name => "queue";

        public override bool Coalescible => true;

        public override Task ExecuteAsync(PvpMatchSession session) => session.StartQueueAsync();
    }

    /// <summary>商店购买：battle 的 index 传 relicId。</summary>
    public sealed class PvpWsShopBuyCommand : PvpWsCommand
    {
        private readonly int _relicId;

        public PvpWsShopBuyCommand(int relicId)
        {
            _relicId = relicId;
        }

        public override string Name => "buy";

        public override Task ExecuteAsync(PvpMatchSession session) => session.ShopBuyAsync(_relicId);
    }

    /// <summary>商店出售：battle 的 index 传 relicId。</summary>
    public sealed class PvpWsShopSellCommand : PvpWsCommand
    {
        private readonly int _relicId;

        public PvpWsShopSellCommand(int relicId)
        {
            _relicId = relicId;
        }

        public override string Name => "sell";

        public override Task ExecuteAsync(PvpMatchSession session) => session.ShopSellAsync(_relicId);
    }

    public sealed class PvpWsShopRefreshCommand : PvpWsCommand
    {
        public override string Name => "refresh";

        public override Task ExecuteAsync(PvpMatchSession session) => session.ShopRefreshAsync();
    }

    public sealed class PvpWsShopDoneCommand : PvpWsCommand
    {
        public override string Name => "shop_done";

        public override bool Coalescible => true;

        public override Task ExecuteAsync(PvpMatchSession session) => session.ShopDoneAsync();
    }

    /// <summary>编辑器测试加圣物：battle action=debug_grant，index=relicId。</summary>
    public sealed class PvpWsDebugGrantRelicCommand : PvpWsCommand
    {
        private readonly int _relicId;

        public PvpWsDebugGrantRelicCommand(int relicId)
        {
            _relicId = relicId;
        }

        public override string Name => "debug_grant";

        public override Task ExecuteAsync(PvpMatchSession session) => session.DebugGrantRelicAsync(_relicId);
    }

    /// <summary>
    /// 命令调用器：FIFO 串行执行，同一时刻最多一个在途操作。
    /// Enqueue 返回的任务随命令完成/失败，调用方自行决定 await 或 fire-and-forget。
    /// </summary>
    public sealed class PvpWsCommandInvoker
    {
        private sealed class Entry
        {
            public PvpWsCommand Command;
            public TaskCompletionSource<bool> Done;
        }

        private readonly PvpMatchSession _session;
        private readonly Queue<Entry> _queue = new Queue<Entry>();
        private bool _draining;

        public PvpWsCommandInvoker(PvpMatchSession session)
        {
            _session = session;
        }

        public int PendingCount => _queue.Count;

        public Task EnqueuePick(int[] indexes) => Enqueue(new PvpWsPickCommand(indexes));

        public Task EnqueueRub(int index) => Enqueue(new PvpWsRubCommand(index));

        public Task EnqueueReplace() => Enqueue(new PvpWsReplaceCommand());

        public Task EnqueuePeek() => Enqueue(new PvpWsPeekCommand());

        public Task EnqueueShowdown() => Enqueue(new PvpWsShowdownCommand());

        public Task EnqueueStartQueue() => Enqueue(new PvpWsStartQueueCommand());

        public Task EnqueueShopBuy(int relicId) => Enqueue(new PvpWsShopBuyCommand(relicId));

        public Task EnqueueShopSell(int relicId) => Enqueue(new PvpWsShopSellCommand(relicId));

        public Task EnqueueShopRefresh() => Enqueue(new PvpWsShopRefreshCommand());

        public Task EnqueueShopDone() => Enqueue(new PvpWsShopDoneCommand());

        public Task EnqueueDebugGrantRelic(int relicId) => Enqueue(new PvpWsDebugGrantRelicCommand(relicId));

        public Task Enqueue(PvpWsCommand command)
        {
            if (command.Coalescible)
            {
                foreach (var entry in _queue)
                {
                    if (entry.Command.GetType() == command.GetType())
                    {
                        return Task.CompletedTask;
                    }
                }
            }

            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(new Entry { Command = command, Done = done });
            if (!_draining)
            {
                _ = DrainAsync();
            }

            return done.Task;
        }

        /// <summary>离场/断线时丢弃未执行的命令，等待方按取消处理。</summary>
        public void Clear()
        {
            while (_queue.Count > 0)
            {
                _queue.Dequeue().Done.TrySetCanceled();
            }
        }

        private async Task DrainAsync()
        {
            _draining = true;
            try
            {
                while (_queue.Count > 0)
                {
                    var entry = _queue.Dequeue();
                    try
                    {
                        await entry.Command.ExecuteAsync(_session);
                        entry.Done.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        entry.Done.TrySetException(ex);
                    }
                }
            }
            finally
            {
                _draining = false;
            }
        }
    }
}
