using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.UI;
using CardShare.Contracts;
using Newtonsoft.Json.Linq;

namespace App.Net
{
    /// <summary>
    /// 客户端 PVP 会话：对局只信 match_update，操作只发 battle；
    /// 匹配阶段抛出排队名单（RosterUpdated）与开房（RoomReady）事件。
    /// </summary>
    public sealed class PvpMatchSession
    {
        private readonly PvpWsClient _ws;
        private readonly Dictionary<string, PlayerPublic> _players = new Dictionary<string, PlayerPublic>();
        private TaskCompletionSource<bool> _authed;
        private TaskCompletionSource<PvpMatchStateDto> _opened;
        private TaskCompletionSource<bool> _cancelled;

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
            _authed = new TaskCompletionSource<bool>();
            _opened = new TaskCompletionSource<PvpMatchStateDto>();
            await _ws.ConnectAsync(GameApi.Client.AccessToken);
            await Wait(_authed.Task, 8000, "对战鉴权超时");
            await _ws.QueueAsync();
            var state = await Wait(_opened.Task, 12000, "匹配超时");
            IsActive = true;
            State = state;
        }

        public Task PickAsync(int[] indexes) => _ws.BattleAsync("pick", 0, indexes);

        public Task RubAsync(int index) => _ws.BattleAsync("rub", index);

        public Task ReplaceAsync() => _ws.BattleAsync("replace");

        public Task PeekAsync() => _ws.BattleAsync("peek");

        public Task ShowdownAsync() => _ws.BattleAsync("showdown");

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
                var message = ReadError(payload);
                _authed?.TrySetException(new GameApiException("invalid_request", message, 400));
                _opened?.TrySetException(new GameApiException("invalid_request", message, 400));
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

                Remember(roster.Players);
                RosterUpdated?.Invoke(roster.Players ?? Array.Empty<PlayerPublic>());
                return;
            }

            if (type == WsMessageTypes.RoomReady)
            {
                var ready = payload != null ? payload.ToObject<WsRoomReadyPayload>() : null;
                if (ready == null)
                {
                    return;
                }

                Remember(ready.Players);
                RoomReady?.Invoke(ready);
                return;
            }

            if (type == WsMessageTypes.Cancel)
            {
                _cancelled?.TrySetResult(true);
                return;
            }

            if (type == WsMessageTypes.MatchUpdate)
            {
                var state = payload != null ? payload.ToObject<PvpMatchStateDto>() : null;
                if (state == null)
                {
                    return;
                }

                State = state;
                _opened?.TrySetResult(state);
                Updated?.Invoke();
                if (string.Equals(state.Phase, "finished", StringComparison.OrdinalIgnoreCase))
                {
                    IsActive = false;
                    Finished?.Invoke();
                }
            }
        }

        private void OnFaulted(string error)
        {
            var message = string.IsNullOrEmpty(error) ? "对战连接断开" : error;
            _authed?.TrySetException(new GameApiException("connection_error", message, 0));
            _opened?.TrySetException(new GameApiException("connection_error", message, 0));
            _cancelled?.TrySetException(new GameApiException("connection_error", message, 0));
            Failed?.Invoke(message);
        }

        private void OnClosed()
        {
            if (IsActive)
            {
                Failed?.Invoke("对战连接已关闭");
            }

            IsActive = false;
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
