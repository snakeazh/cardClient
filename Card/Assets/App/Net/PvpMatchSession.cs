using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.UI;
using CardShare.Contracts;
using Framework.Log;
using Newtonsoft.Json.Linq;

namespace App.Net
{
    /// <summary>
    /// 客户端 PVP 会话：对局只信 match_update，操作只发 battle；
    /// 匹配阶段抛出排队名单（RosterUpdated）与开房（RoomReady）事件。
    /// 对局中断线自动指数退避重连（1s/2s/4s，最多 5 次），成功后发 sync 拉全量快照。
    /// </summary>
    public sealed class PvpMatchSession
    {
        private readonly PvpWsClient _ws;
        private readonly Dictionary<string, PlayerPublic> _players = new Dictionary<string, PlayerPublic>();
        private TaskCompletionSource<bool> _authed;
        private TaskCompletionSource<PvpMatchStateDto> _opened;
        private TaskCompletionSource<long> _queued;
        private TaskCompletionSource<bool> _cancelled;
        private TaskCompletionSource<bool> _synced;
        private TaskCompletionSource<bool> _updateWaiter;
        private long _lastVersion;
        private bool _acceptAnySnapshot;
        private bool _reconnecting;

        // 与服务端 PvpRules.QueueTimeoutMs 对齐的兜底值；房间直接开成（机器人补房/第4人入队）
        // 时服务端不发 queued，由 room_ready/match_update 用该值直接解锁排队等待。
        private const long DefaultQueueTimeoutMs = 60000;

        public PvpMatchSession(PvpWsClient ws)
        {
            _ws = ws ?? throw new ArgumentNullException(nameof(ws));
            _ws.Message += OnMessage;
            _ws.Faulted += OnFaulted;
            _ws.Closed += OnClosed;
            Invoker = new PvpWsCommandInvoker(this);
        }

        /// <summary>用户操作命令队列：所有经长链接下发的操作（pick/rub/replace/peek/showdown/queue）从这里串行走。</summary>
        public PvpWsCommandInvoker Invoker { get; }

        public PvpMatchStateDto State { get; private set; }

        public bool IsActive { get; private set; }

        public event Action Updated;
        public event Action<string> Failed;
        public event Action Finished;

        /// <summary>阶段事件（round_start / settle_start / shop_start / duel_resolved / player_offline / player_online / match_finished），随 match_update 同批逐条下发。</summary>
        public event Action<PvpMatchEventDto> MatchEvent;

        /// <summary>对局内的业务错误（商店校验失败、阶段不符等）：只提示，不判死。</summary>
        public event Action<string> Notice;

        /// <summary>对局中断线，开始自动重连。</summary>
        public event Action Reconnecting;

        /// <summary>重连 + sync 成功，快照已恢复。</summary>
        public event Action Reconnected;

        /// <summary>排队名单变化（queued / queue_update），PlayerPublic 带昵称与头像。</summary>
        public event Action<PlayerPublic[]> RosterUpdated;

        /// <summary>满 4 人开房：RoomId / Seed / ModeId 与最终 4 人名单。头像只在此时下发，已缓存进 <see cref="TryGetPlayer"/>。</summary>
        public event Action<WsRoomReadyPayload> RoomReady;

        public PvpFighterDto Self
        {
            get
            {
                var userId = GameApi.Client != null ? GameApi.Client.UserId : string.Empty;
                var players = State != null ? State.Players : null;
                if (players == null)
                {
                    return null;
                }

                for (var i = 0; i < players.Length; i++)
                {
                    if (SameUser(players[i].UserId, userId))
                    {
                        return players[i];
                    }
                }

                return players.Length > 0 ? players[0] : null;
            }
        }

        public async Task StartQueueAsync()
        {
            if (GameApi.Client == null || !GameApi.Client.HasSession)
            {
                throw new GameApiException("unauthorized", "请先登录", 401);
            }

            _players.Clear();
            _lastVersion = 0;
            _acceptAnySnapshot = false;
            _authed = new TaskCompletionSource<bool>();
            _opened = new TaskCompletionSource<PvpMatchStateDto>();
            _queued = new TaskCompletionSource<long>();
            await _ws.ConnectAsync(GameApi.Client.AccessToken);
            await Wait(_authed.Task, 8000, "对战鉴权超时");
            await _ws.QueueAsync();
            // 排队超时时长以服务端 queued 回包的 TimeoutMs 为准（PvpRules.QueueTimeoutMs），兜底 60s。
            var timeoutMs = await Wait(_queued.Task, 10000, "匹配无响应");
            var waitMs = timeoutMs > 0 ? (int)Math.Min(timeoutMs + 5000, 300000) : 60000;
            var state = await Wait(_opened.Task, waitMs, "匹配超时");
            IsActive = true;
            State = state;
        }

        public Task PickAsync(int[] indexes) => _ws.BattleAsync("pick", 0, indexes);

        public Task RubAsync(int index) => _ws.BattleAsync("rub", index);

        public Task ReplaceAsync() => _ws.BattleAsync("replace");

        public Task PeekAsync() => _ws.BattleAsync("peek");

        public Task ShowdownAsync() => _ws.BattleAsync("showdown");

        public Task ShopBuyAsync(int relicId) => _ws.BattleAsync("buy", relicId);

        public Task ShopSellAsync(int relicId) => _ws.BattleAsync("sell", relicId);

        public Task ShopRefreshAsync() => _ws.BattleAsync("refresh");

        public Task ShopDoneAsync() => _ws.BattleAsync("shop_done");

        /// <summary>编辑器测试：PVP 向服务端添加圣物（index = relicId）。</summary>
        public Task DebugGrantRelicAsync(int relicId) => _ws.BattleAsync("debug_grant", relicId);

        /// <summary>等下一个 match_update（商店操作后等快照回刷新），超时返回 false。</summary>
        public async Task<bool> WaitNextUpdateAsync(int timeoutMs)
        {
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _updateWaiter = waiter;
            var done = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
            if (done == waiter.Task)
            {
                return true;
            }

            if (ReferenceEquals(_updateWaiter, waiter))
            {
                _updateWaiter = null;
            }

            return false;
        }

        /// <summary>
        /// 发 battle 并等对应 match_update：先挂 waiter 再 send，避免回包早于等待注册被漏掉。
        /// 用于搓牌/透视/替换等必须等权威快照再刷牌的操作。
        /// </summary>
        public async Task<bool> BattleAndWaitAsync(Func<Task> send, int timeoutMs = 3000)
        {
            if (send == null)
            {
                throw new ArgumentNullException(nameof(send));
            }

            var before = _lastVersion;
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _updateWaiter = waiter;
            try
            {
                await send();
            }
            catch
            {
                if (ReferenceEquals(_updateWaiter, waiter))
                {
                    _updateWaiter = null;
                }

                throw;
            }

            if (_lastVersion > before)
            {
                if (ReferenceEquals(_updateWaiter, waiter))
                {
                    _updateWaiter = null;
                }

                return true;
            }

            var done = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
            if (done == waiter.Task)
            {
                return true;
            }

            if (ReferenceEquals(_updateWaiter, waiter))
            {
                _updateWaiter = null;
            }

            return _lastVersion > before;
        }

        /// <summary>取消匹配：发 cancel 并等服务端回 ack。</summary>
        public async Task CancelAsync()
        {
            _cancelled = new TaskCompletionSource<bool>();
            await _ws.CancelAsync();
            await Wait(_cancelled.Task, 5000, "取消匹配超时");
        }

        /// <summary>按 UserId 查匹配/开房阶段缓存的玩家公开信息（昵称、头像）。match_update 的 Fighter 不带头像，走这里。</summary>
        public bool TryGetPlayer(string userId, out PlayerPublic player)
        {
            return _players.TryGetValue(NormalizeUserId(userId), out player);
        }

        public async Task StopAsync()
        {
            IsActive = false;
            State = null;
            _players.Clear();
            _lastVersion = 0;
            _acceptAnySnapshot = false;
            var waiter = _updateWaiter;
            _updateWaiter = null;
            waiter?.TrySetCanceled();
            Invoker.Clear();
            try
            {
                if (_ws.IsConnected)
                {
                    await _ws.LeaveAsync();
                }
            }
            catch (GameApiException ex)
            {
                Toast.Error(GameApi.Describe(ex));
            }

            await _ws.DisconnectAsync();
        }

        private void OnMessage(string type, JToken payload)
        {
            if (type == WsMessageTypes.Authed)
            {
                _authed?.TrySetResult(true);
                return;
            }

            if (type == WsMessageTypes.Error)
            {
                var code = payload != null ? payload.Value<string>("code") : null;
                var message = ReadError(payload);
                var exception = new GameApiException(
                    string.IsNullOrEmpty(code) ? "invalid_request" : code,
                    message,
                    400);
                _authed?.TrySetException(exception);
                _opened?.TrySetException(exception);
                _queued?.TrySetException(exception);
                _cancelled?.TrySetException(exception);
                if (string.Equals(code, ErrorCodes.NotInBattle, StringComparison.OrdinalIgnoreCase))
                {
                    // sync 未命中：房间已回收/对局已结束，走离开流程。
                    _synced?.TrySetException(exception);
                    if (IsActive)
                    {
                        IsActive = false;
                        Failed?.Invoke("对局已结束");
                    }

                    return;
                }

                if (IsActive)
                {
                    Notice?.Invoke(message);
                    return;
                }

                Failed?.Invoke(message);
                return;
            }

            if (type == WsMessageTypes.Queued || type == WsMessageTypes.QueueUpdate)
            {
                var roster = payload != null ? payload.ToObject<WsQueueRosterPayload>() : null;
                if (roster == null)
                {
                    return;
                }

                if (type == WsMessageTypes.Queued)
                {
                    _queued?.TrySetResult(roster.TimeoutMs);
                }

                Remember(roster.Players);
                RosterUpdated?.Invoke(roster.Players ?? Array.Empty<PlayerPublic>());
                return;
            }

            if (type == WsMessageTypes.QueueTimeout)
            {
                var exception = new GameApiException("queue_timeout", "匹配超时", 0);
                _opened?.TrySetException(exception);
                Failed?.Invoke("匹配超时");
                return;
            }

            if (type == WsMessageTypes.RoomReady)
            {
                var ready = payload != null ? payload.ToObject<WsRoomReadyPayload>() : null;
                if (ready == null)
                {
                    return;
                }

                _queued?.TrySetResult(DefaultQueueTimeoutMs);
                Remember(ready.Players);
                RoomReady?.Invoke(ready);
                return;
            }

            if (type == WsMessageTypes.Cancel)
            {
                _cancelled?.TrySetResult(true);
                return;
            }

            if (type == WsMessageTypes.MatchEvent)
            {
                var evt = payload != null ? payload.ToObject<PvpMatchEventDto>() : null;
                if (evt == null)
                {
                    return;
                }

                AppLog.Info(LogChannel.Net, "pvp match_event ← " + evt.Kind
                    + " round=" + evt.Round + " user=" + evt.UserId + " value=" + evt.Value);
                MatchEvent?.Invoke(evt);
                return;
            }

            if (type == WsMessageTypes.MatchUpdate)
            {
                var state = payload != null ? payload.ToObject<PvpMatchStateDto>() : null;
                if (state == null)
                {
                    return;
                }

                // 收到快照即说明已开房：解锁排队等待（机器人补房/第4人入队时服务端不发 queued）。
                _queued?.TrySetResult(DefaultQueueTimeoutMs);

                // 版本过滤：乱序/重复快照丢弃；sync 回包（重连后第一个快照）无条件接受。
                var catchUp = _acceptAnySnapshot;
                if (!catchUp && state.StateVersion > 0 && state.StateVersion <= _lastVersion)
                {
                    return;
                }

                _acceptAnySnapshot = false;
                if (state.StateVersion > _lastVersion)
                {
                    _lastVersion = state.StateVersion;
                }

                AppLog.Info(LogChannel.Net, Describe(state));
                State = state;
                _opened?.TrySetResult(state);
                var waiter = _updateWaiter;
                _updateWaiter = null;
                waiter?.TrySetResult(true);
                Updated?.Invoke();
                if (catchUp)
                {
                    var synced = _synced;
                    _synced = null;
                    synced?.TrySetResult(true);
                    // sync 全量快照内嵌了广播尚未 drain 的增量事件：补发一遍，消费方（Driver/ViewModel）幂等。
                    var events = state.Events;
                    if (events != null)
                    {
                        for (var i = 0; i < events.Length; i++)
                        {
                            MatchEvent?.Invoke(events[i]);
                        }
                    }
                }

                if (string.Equals(state.Phase, "finished", StringComparison.OrdinalIgnoreCase))
                {
                    IsActive = false;
                    Finished?.Invoke();
                }
            }
        }

        /// <summary>match_update 的一行摘要：轮次/阶段/伤害/倒计时 + 各家血量和攻击，Info 级正式包也可见。</summary>
        private static string Describe(PvpMatchStateDto state)
        {
            var players = state.Players ?? Array.Empty<PvpFighterDto>();
            var alive = 0;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p.Alive)
                {
                    alive++;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(p.NickName);
                if (p.IsBot)
                {
                    sb.Append("(bot)");
                }

                if (!p.Alive)
                {
                    sb.Append("[淘汰#").Append(p.Rank).Append(']');
                }

                sb.Append(" hp=").Append(p.Hp).Append('/').Append(p.MaxHp)
                    .Append(" atk=").Append(p.Attack);
            }

            var duel = string.Empty;
            if (state.Duel != null && state.Duel.Seats != null && state.Duel.Seats.Count > 0)
            {
                var names = new System.Text.StringBuilder();
                for (var i = 0; i < state.Duel.Seats.Count; i++)
                {
                    if (i > 0)
                    {
                        names.Append(" vs ");
                    }

                    names.Append(state.Duel.Seats[i].NickName);
                    if (state.Duel.Seats[i].Locked)
                    {
                        names.Append("(锁)");
                    }
                }

                duel = " duel=" + names;
            }
            var countdown = state.PhaseDeadlineUtcMs > 0
                ? " 剩" + Math.Max(0, (state.PhaseDeadlineUtcMs - state.ServerNowUtcMs) / 1000) + "s"
                : string.Empty;
            return "pvp match_update ← 第" + state.Round + "轮 phase=" + state.Phase
                + " fight=" + state.FightKind + " 存活" + alive + "/" + players.Length
                + " dmg=" + state.DuelDamage + countdown + duel + " [" + sb + "]";
        }

        private void OnFaulted(string error)
        {
            var message = string.IsNullOrEmpty(error) ? "对战连接断开" : error;
            FailPending(new GameApiException("connection_error", message, 0));
            if (IsActive)
            {
                BeginReconnect();
                return;
            }

            Failed?.Invoke(message);
        }

        private void OnClosed()
        {
            FailPending(new GameApiException("connection_error", "对战连接已关闭", 0));
            if (IsActive)
            {
                BeginReconnect();
            }
        }

        private void FailPending(GameApiException exception)
        {
            _authed?.TrySetException(exception);
            _opened?.TrySetException(exception);
            _queued?.TrySetException(exception);
            _cancelled?.TrySetException(exception);
            _synced?.TrySetException(exception);
        }

        /// <summary>
        /// 对局中断线：指数退避（1s/2s/4s，最多 5 次）重建连接，auth 成功后发 sync 按 userId 拉全量快照；
        /// sync 回包无条件接受（见 match_update 的 catchUp 分支）。全部失败或服务端告知 not_in_battle 才判死。
        /// </summary>
        private async void BeginReconnect()
        {
            if (_reconnecting)
            {
                return;
            }

            _reconnecting = true;
            _acceptAnySnapshot = true;
            Reconnecting?.Invoke();
            var gone = false;
            try
            {
                for (var attempt = 0; attempt < 5 && IsActive; attempt++)
                {
                    await Task.Delay(1000 << Math.Min(attempt, 2));
                    if (!IsActive)
                    {
                        return;
                    }

                    try
                    {
                        _authed = new TaskCompletionSource<bool>();
                        _synced = new TaskCompletionSource<bool>();
                        await _ws.ConnectAsync(GameApi.Client != null ? GameApi.Client.AccessToken : string.Empty);
                        await Wait(_authed.Task, 8000, "对战鉴权超时");
                        await _ws.SyncAsync();
                        await Wait(_synced.Task, 8000, "对局同步超时");
                        AppLog.Info(LogChannel.Net, "pvp ws: 第 " + (attempt + 1) + " 次重连成功");
                        Reconnected?.Invoke();
                        return;
                    }
                    catch (GameApiException ex)
                    {
                        AppLog.Warn(LogChannel.Net, "pvp ws: 重连失败 " + ex.Message);
                        if (string.Equals(ex.Code, ErrorCodes.NotInBattle, StringComparison.OrdinalIgnoreCase))
                        {
                            gone = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn(LogChannel.Net, "pvp ws: 重连失败 " + ex.Message);
                    }
                }

                if (IsActive)
                {
                    IsActive = false;
                    Failed?.Invoke(gone ? "对局已结束" : "对战连接已断开");
                }
            }
            finally
            {
                _reconnecting = false;
            }
        }

        private static async Task<T> Wait<T>(Task<T> task, int timeoutMs, string timeoutMessage)
        {
            var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (done != task)
            {
                throw new GameApiException("connection_error", timeoutMessage, 0);
            }

            return await task;
        }

        private static string ReadError(JToken payload)
        {
            if (payload == null)
            {
                return "对战请求失败";
            }

            var message = payload.Value<string>("message");
            return string.IsNullOrEmpty(message) ? "对战请求失败" : message;
        }

        private void Remember(PlayerPublic[] players)
        {
            if (players == null)
            {
                return;
            }

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player != null && !string.IsNullOrEmpty(player.UserId))
                {
                    _players[NormalizeUserId(player.UserId)] = player;
                }
            }
        }

        private static string NormalizeUserId(string userId)
        {
            return string.IsNullOrEmpty(userId) ? string.Empty : userId.Replace("-", string.Empty).ToUpperInvariant();
        }

        public static bool SameUser(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            return string.Equals(NormalizeUserId(a), NormalizeUserId(b), StringComparison.Ordinal);
        }
    }
}
