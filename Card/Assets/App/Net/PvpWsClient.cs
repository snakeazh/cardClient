using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CardShare.Contracts;
using Framework.Log;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace App.Net
{
    /// <summary>PVP WebSocket。收包进队列，主线程 <see cref="Pump"/> 分发。</summary>
    public sealed class PvpWsClient
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly ConcurrentQueue<Incoming> _inbox = new ConcurrentQueue<Incoming>();
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private long _seq;
        private int _running;

        public event Action<string, JToken> Message;
        public event Action<string> Faulted;
        public event Action Closed;

        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

        public async Task ConnectAsync(string accessToken)
        {
            await DisconnectAsync();
            _cts = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            _running = 1;
            try
            {
                await _socket.ConnectAsync(new Uri(GameApiSettings.WebSocketUrl), _cts.Token);
            }
            catch (Exception ex)
            {
                _running = 0;
                throw new GameApiException("connection_error", "无法连接对战服: " + ex.Message, 0);
            }

            _ = ReceiveLoop(_cts.Token);
            await SendAsync(WsMessageTypes.Auth, new WsAuthPayload { AccessToken = accessToken ?? string.Empty });
        }

        public Task QueueAsync() => SendAsync(WsMessageTypes.Queue, null);

        public Task CancelAsync() => SendAsync(WsMessageTypes.Cancel, null);

        public Task LeaveAsync() => SendAsync(WsMessageTypes.Leave, null);

        public Task BattleAsync(string action, int index = 0, int[] indexes = null)
        {
            return SendAsync(WsMessageTypes.Battle, new WsBattleActionPayload
            {
                Action = action ?? string.Empty,
                Index = index,
                Indexes = indexes ?? Array.Empty<int>()
            });
        }

        public async Task DisconnectAsync()
        {
            _running = 0;
            var cts = _cts;
            _cts = null;
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }

            var socket = _socket;
            _socket = null;
            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    }
                }
                catch (Exception)
                {
                }

                socket.Dispose();
            }

            while (_inbox.TryDequeue(out _))
            {
            }
        }

        public void Pump()
        {
            while (_inbox.TryDequeue(out var incoming))
            {
                if (incoming.Fault)
                {
                    Faulted?.Invoke(incoming.Error);
                    continue;
                }

                if (incoming.Closed)
                {
                    Closed?.Invoke();
                    continue;
                }

                Message?.Invoke(incoming.Type, incoming.Payload);
            }
        }

        private async Task SendAsync(string type, object payload)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                throw new GameApiException("connection_error", "对战连接已断开", 0);
            }

            var envelope = new WsEnvelope
            {
                T = type,
                Seq = Interlocked.Increment(ref _seq),
                Payload = payload
            };
            var json = JsonConvert.SerializeObject(envelope, Json);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var socket = _socket;
            var buffer = new byte[8 * 1024];
            var stream = new MemoryStream();
            try
            {
                while (_running == 1 && socket != null && socket.State == WebSocketState.Open)
                {
                    stream.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _inbox.Enqueue(Incoming.MakeClosed());
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                    var env = JsonConvert.DeserializeObject<WsWire>(json, Json);
                    if (env == null || string.IsNullOrEmpty(env.T))
                    {
                        continue;
                    }

                    _inbox.Enqueue(Incoming.Make(env.T.Trim().ToLowerInvariant(), env.Payload));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "pvp ws: " + ex.Message);
                _inbox.Enqueue(Incoming.MakeFault(ex.Message));
            }
        }

        private sealed class WsWire
        {
            public string T { get; set; }
            public long Seq { get; set; }
            public JToken Payload { get; set; }
        }

        private readonly struct Incoming
        {
            public readonly bool Fault;
            public readonly bool Closed;
            public readonly string Type;
            public readonly JToken Payload;
            public readonly string Error;

            private Incoming(bool fault, bool closed, string type, JToken payload, string error)
            {
                Fault = fault;
                Closed = closed;
                Type = type;
                Payload = payload;
                Error = error;
            }

            public static Incoming Make(string type, JToken payload)
                => new Incoming(false, false, type, payload, null);

            public static Incoming MakeFault(string error)
                => new Incoming(true, false, string.Empty, null, error);

            public static Incoming MakeClosed()
                => new Incoming(false, true, string.Empty, null, null);
        }
    }
}
