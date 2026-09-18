using System;
using System.Threading.Tasks;
using App.Game;
using App.UI;
using CardShare.Contracts;
using Newtonsoft.Json.Linq;

namespace App.Net
{
    /// <summary>客户端 PVP 会话：只信 match_update，操作只发 battle。</summary>
    public sealed class PvpMatchSession
    {
        private readonly PvpWsClient _ws;
        private TaskCompletionSource<bool> _authed;
        private TaskCompletionSource<PvpMatchStateDto> _opened;

        public PvpMatchSession(PvpWsClient ws)
        {
            _ws = ws ?? throw new ArgumentNullException(nameof(ws));
            _ws.Message += OnMessage;
            _ws.Faulted += OnFaulted;
            _ws.Closed += OnClosed;
        }

        public PvpMatchStateDto State { get; private set; }

        public bool IsActive { get; private set; }

        public event Action Updated;
        public event Action<string> Failed;
        public event Action Finished;

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

        public async Task StopAsync()
        {
            IsActive = false;
            State = null;
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

        public void ApplyTo(GameSession session)
        {
            if (session == null || State == null)
            {
                return;
            }

            session.ApplyPvpMatch(State, GameApi.Client != null ? GameApi.Client.UserId : string.Empty);
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

        public static bool SameUser(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            return string.Equals(a.Replace("-", string.Empty), b.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase);
        }
    }
}
