using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Game;
using App.Net;
using App.PvpScore;
using CardShare.Contracts;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
        /// <summary>
        /// 竞技场：匹配成功后停在 search，全员点接受才播 searchSuccess 飞入并进局。
        /// 60 秒不点视为未接受。机器人先显示 ???，再由服务端按间隔替它们点接受。
        /// </summary>
    public sealed class PvpPageViewModel : ViewModelBase
    {
        private const int AcceptSeconds = 60;

        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly GameSession _session;
        private readonly GameTableViewModel _table;
        private readonly PvpMatchSession _pvp;
        private readonly IPvpScoreService _pvpScore;
        private readonly MainResourceViewModel _mainResource;
        private int _generation;
        private PvpPagePhase _phase = PvpPagePhase.Idle;
        private bool _entered;
        private bool _entering;
        private bool _starting;
        private bool _localAccepted;
        private bool _introStarted;
        private readonly HashSet<string> _acceptedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Task _leaveTask;

        public PvpPageViewModel(
            IUIManager ui,
            NavigationViewModel navigation,
            GameSession session,
            GameTableViewModel table,
            PvpMatchSession pvp,
            IPvpScoreService pvpScore,
            MainResourceViewModel mainResource)
        {
            _ui = ui;
            _navigation = navigation;
            _session = session;
            _table = table;
            _pvp = pvp ?? throw new ArgumentNullException(nameof(pvp));
            _pvpScore = pvpScore ?? throw new ArgumentNullException(nameof(pvpScore));
            _mainResource = mainResource ?? throw new ArgumentNullException(nameof(mainResource));

            GradeValueText = new ObservableProperty<string>("0");
            SearchTimeText = new ObservableProperty<string>(string.Empty);
            YesText = new ObservableProperty<string>($"接受({AcceptSeconds})");
            ShowStart = new ObservableProperty<bool>(true);
            ShowCancel = new ObservableProperty<bool>(false);
            ShowSearchTime = new ObservableProperty<bool>(false);
            ShowMatchSuccess = new ObservableProperty<bool>(false);
            ShowYes = new ObservableProperty<bool>(false);
            ShowCancelPvp = new ObservableProperty<bool>(false);
            ShowSearch = new ObservableProperty<bool>(true);
            ShowSearchSuccess = new ObservableProperty<bool>(false);

            BackCommand = new RelayCommand(Back);
            StartCommand = new RelayCommand(StartMatch, () => _phase == PvpPagePhase.Idle && !_entering && !_starting);
            CancelCommand = new RelayCommand(CancelMatch, () => _phase == PvpPagePhase.Searching);
            AcceptCommand = new RelayCommand(AcceptMatch, () =>
                _phase == PvpPagePhase.Accepting && !_entering && !_localAccepted && !_introStarted);
            DeclineCommand = new RelayCommand(DeclineMatch, () => _phase == PvpPagePhase.Accepting && !_entering);
        }

        public ObservableProperty<string> GradeValueText { get; }

        public ObservableProperty<string> SearchTimeText { get; }

        public ObservableProperty<string> YesText { get; }

        public ObservableProperty<bool> ShowStart { get; }

        public ObservableProperty<bool> ShowCancel { get; }

        public ObservableProperty<bool> ShowSearchTime { get; }

        public ObservableProperty<bool> ShowMatchSuccess { get; }

        public ObservableProperty<bool> ShowYes { get; }

        /// <summary>匹配成功后的取消，与接受倒计时同时出现。</summary>
        public ObservableProperty<bool> ShowCancelPvp { get; }

        public ObservableProperty<bool> ShowSearch { get; }

        public ObservableProperty<bool> ShowSearchSuccess { get; }

        public event Action SeatsChanged;

        public IReadOnlyList<PvpReadySeat> Seats { get; private set; } = Array.Empty<PvpReadySeat>();

        /// <summary>由 View 注入：全员接受后四个头像往外飞。返回 false 表示被打断。</summary>
        public Func<Task<bool>> PlaySuccessIntro { get; set; }

        public IRelayCommand BackCommand { get; }

        public IRelayCommand StartCommand { get; }

        public IRelayCommand CancelCommand { get; }

        public IRelayCommand AcceptCommand { get; }

        public IRelayCommand DeclineCommand { get; }

        protected override Task OnOpen(object args)
        {
            _pvpScore.Changed += OnPvpScoreChanged;
            _pvp.AllAccepted += OnAllAccepted;
            _pvp.AcceptUpdated += OnAcceptUpdated;
            _pvp.AcceptFailed += OnAcceptFailed;
            _pvp.RoomReady += OnRoomReady;
            RefreshGrade();
            ApplyIdle();
            _mainResource.HideBar();
            return Task.CompletedTask;
        }

        protected override async Task OnClose()
        {
            _pvpScore.Changed -= OnPvpScoreChanged;
            _pvp.AllAccepted -= OnAllAccepted;
            _pvp.AcceptUpdated -= OnAcceptUpdated;
            _pvp.AcceptFailed -= OnAcceptFailed;
            _pvp.RoomReady -= OnRoomReady;
            _generation++;
            if (!_entered && _phase != PvpPagePhase.Idle)
            {
                _phase = PvpPagePhase.Idle;
                await LeaveMatchAsync();
            }

            if (!_entered)
            {
                _mainResource.ShowBar();
                await _navigation.EnsureShown();
            }
        }

        protected override void OnDispose()
        {
            _pvpScore.Changed -= OnPvpScoreChanged;
            _pvp.AllAccepted -= OnAllAccepted;
            _pvp.AcceptUpdated -= OnAcceptUpdated;
            _pvp.AcceptFailed -= OnAcceptFailed;
            _pvp.RoomReady -= OnRoomReady;
            if (!_entered)
            {
                _mainResource.ShowBar();
            }
        }

        private void OnPvpScoreChanged()
        {
            RefreshGrade();
        }

        private void RefreshGrade()
        {
            GradeValueText.Value = _pvpScore.Score.ToString();
        }

        private async void Back()
        {
            try
            {
                await _ui.Close(this);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }

        private async void StartMatch()
        {
            if (_phase != PvpPagePhase.Idle || _entering || _starting)
            {
                return;
            }

            _starting = true;
            StartCommand.RaiseCanExecuteChanged();
            try
            {
                if (!GameApiSettings.Enabled)
                {
                    Toast.Error("服务器连接已关闭");
                    return;
                }

                if (!GameApi.IsReady)
                {
                    await PveSessionGate.ConnectWithRetryAsync(_ui.Dialogs);
                    if (!IsOpen || _phase != PvpPagePhase.Idle)
                    {
                        return;
                    }

                    if (!GameApi.IsReady)
                    {
                        Toast.Error("未连接服务器");
                        return;
                    }
                }

                if (!IsOpen)
                {
                    return;
                }

                var generation = ++_generation;
                _phase = PvpPagePhase.Searching;
                ApplySearching();
                _ = RunSearchClockAsync(generation);
                await RunSearchAsync(generation);
            }
            finally
            {
                _starting = false;
                StartCommand.RaiseCanExecuteChanged();
            }
        }

        private async void CancelMatch()
        {
            if (_phase != PvpPagePhase.Searching)
            {
                return;
            }

            await StopAndReturnIdleAsync();
        }

        private async void DeclineMatch()
        {
            if (_phase != PvpPagePhase.Accepting || _entering)
            {
                return;
            }

            await DeclineAsync(_generation);
        }

        private async void AcceptMatch()
        {
            if (_phase != PvpPagePhase.Accepting || _entering || _localAccepted || _introStarted)
            {
                return;
            }

            _localAccepted = true;
            YesText.Value = "已接受";
            ShowSearch.Value = false;
            ShowSearchSuccess.Value = true;
            RaiseCommands();
            try
            {
                await _pvp.AcceptAsync();
            }
            catch (Exception ex)
            {
                _localAccepted = false;
                ShowSearch.Value = true;
                ShowSearchSuccess.Value = false;
                AppLog.Exception(LogChannel.UI, ex);
                Toast.Error("接受失败");
                RaiseCommands();
            }
        }

        private async Task EnterMatchAsync()
        {
            if (_entering)
            {
                return;
            }

            _entering = true;
            _generation++;
            AcceptCommand.RaiseCanExecuteChanged();
            DeclineCommand.RaiseCanExecuteChanged();
            try
            {
                // 进对局会在同一层打开 GameResource，资源层必须先亮着。
                _mainResource.ShowBar();
                _session.BeginPvp();
                _entered = true;
                await _ui.Open(_table);
                await _ui.Close(this);
            }
            catch (Exception ex)
            {
                _entered = false;
                _mainResource.HideBar();
                AppLog.Exception(LogChannel.UI, ex);
                _session.EndPvp();
                await LeaveMatchAsync();
                if (IsOpen)
                {
                    ApplyIdle();
                }

                Toast.Error("进入对局失败");
            }
            finally
            {
                _entering = false;
                AcceptCommand.RaiseCanExecuteChanged();
                DeclineCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task RunSearchAsync(int generation)
        {
            try
            {
                await _pvp.Invoker.EnqueueStartQueue();
            }
            catch (GameApiException ex)
            {
                if (!IsPhase(generation, PvpPagePhase.Searching))
                {
                    await LeaveMatchAsync();
                    return;
                }

                AppLog.Warn(LogChannel.Net, "pvp queue failed: " + ex.Message);
                Toast.Error(string.Equals(ex.Code, "queue_timeout", StringComparison.OrdinalIgnoreCase)
                    ? "匹配超时"
                    : GameApi.Describe(ex));
                await StopAndReturnIdleAsync();
                return;
            }
            catch (Exception ex)
            {
                if (!IsPhase(generation, PvpPagePhase.Searching))
                {
                    await LeaveMatchAsync();
                    return;
                }

                AppLog.Exception(LogChannel.UI, ex);
                Toast.Error("匹配失败");
                await StopAndReturnIdleAsync();
                return;
            }

            if (!IsPhase(generation, PvpPagePhase.Searching))
            {
                if (_phase != PvpPagePhase.Accepting)
                {
                    await LeaveMatchAsync();
                }

                return;
            }

            _phase = PvpPagePhase.Accepting;
            ApplyAccepting();
            await RunAcceptCountdownAsync(generation);
        }

        /// <summary>停掉当前匹配或接受，离开队列/房间后再回到开始按钮。</summary>
        private async Task StopAndReturnIdleAsync()
        {
            _generation++;
            _phase = PvpPagePhase.Idle;
            ShowStart.Value = false;
            ShowCancel.Value = false;
            ShowSearchTime.Value = false;
            ShowMatchSuccess.Value = false;
            ShowYes.Value = false;
            ShowCancelPvp.Value = false;
            ShowSearch.Value = false;
            ShowSearchSuccess.Value = false;
            RaiseCommands();
            await LeaveMatchAsync();
            if (IsOpen && _phase == PvpPagePhase.Idle)
            {
                ApplyIdle();
            }
        }

        /// <summary>匹配等待计时。WebGL/微信上 Task.Delay 不可靠，用 realtime + Yield 按秒刷新。</summary>
        private async Task RunSearchClockAsync(int generation)
        {
            var start = Time.realtimeSinceStartup;
            while (IsPhase(generation, PvpPagePhase.Searching))
            {
                var elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - start);
                var totalSeconds = (int)elapsed;
                SearchTimeText.Value = $"匹配中...\n{totalSeconds / 60:00}：{totalSeconds % 60:00}";

                var nextTick = start + totalSeconds + 1f;
                while (IsPhase(generation, PvpPagePhase.Searching) && Time.realtimeSinceStartup < nextTick)
                {
                    await Task.Yield();
                }
            }
        }

        private async Task DeclineAsync(int generation)
        {
            if (!IsPhase(generation, PvpPagePhase.Accepting) || _entering || _introStarted)
            {
                return;
            }

            try
            {
                await _pvp.DeclineAsync();
            }
            catch (Exception ex)
            {
                if (!IsPhase(generation, PvpPagePhase.Accepting))
                {
                    return;
                }

                AppLog.Exception(LogChannel.UI, ex);
                Toast.Error("未接受失败");
            }
        }

        private void OnRoomReady(WsRoomReadyPayload ready)
        {
            if (!IsOpen || _entering || _introStarted || _phase == PvpPagePhase.Idle)
            {
                return;
            }

            var generation = ++_generation;
            _phase = PvpPagePhase.Accepting;
            ApplyAccepting();
            _ = RunAcceptCountdownAsync(generation);
        }

        private void OnAcceptFailed()
        {
            if (!IsOpen || _entering || _introStarted || _phase != PvpPagePhase.Accepting)
            {
                return;
            }

            var generation = ++_generation;
            _phase = PvpPagePhase.Searching;
            ApplySearching();
            _ = RunSearchClockAsync(generation);
        }

        private Task LeaveMatchAsync()
        {
            if (_leaveTask != null && !_leaveTask.IsCompleted)
            {
                return _leaveTask;
            }

            _leaveTask = LeaveMatchCoreAsync();
            return _leaveTask;
        }

        private async Task LeaveMatchCoreAsync()
        {
            try
            {
                await _pvp.StopAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "pvp leave failed: " + ex.Message);
            }
        }

        private bool IsPhase(int generation, PvpPagePhase phase)
        {
            return generation == _generation && _phase == phase && IsOpen;
        }

        private void ApplyIdle()
        {
            _phase = PvpPagePhase.Idle;
            _localAccepted = false;
            _introStarted = false;
            _acceptedIds.Clear();
            ShowStart.Value = true;
            ShowCancel.Value = false;
            ShowSearchTime.Value = false;
            ShowMatchSuccess.Value = false;
            ShowYes.Value = false;
            ShowCancelPvp.Value = false;
            ShowSearch.Value = true;
            ShowSearchSuccess.Value = false;
            SearchTimeText.Value = string.Empty;
            YesText.Value = $"接受({AcceptSeconds})";
            RaiseCommands();
        }

        private void ApplySearching()
        {
            ShowStart.Value = false;
            ShowCancel.Value = true;
            ShowSearchTime.Value = true;
            ShowMatchSuccess.Value = false;
            ShowYes.Value = false;
            ShowCancelPvp.Value = false;
            ShowSearch.Value = true;
            ShowSearchSuccess.Value = false;
            SearchTimeText.Value = "匹配中...\n00：00";
            _acceptedIds.Clear();
            RaiseCommands();
        }

        /// <summary>匹配成功先留在接受页。点接受后才打开 searchSuccess。</summary>
        private void ApplyAccepting()
        {
            _localAccepted = false;
            _introStarted = false;
            ShowSearch.Value = true;
            ShowSearchSuccess.Value = false;
            ShowStart.Value = false;
            ShowCancel.Value = false;
            ShowSearchTime.Value = false;
            ShowMatchSuccess.Value = true;
            ShowYes.Value = true;
            ShowCancelPvp.Value = true;
            YesText.Value = $"接受({AcceptSeconds})";
            _acceptedIds.Clear();
            PublishSeats();
            RaiseCommands();
        }

        private async Task RunAcceptCountdownAsync(int generation)
        {
            var start = Time.realtimeSinceStartup;
            while (IsPhase(generation, PvpPagePhase.Accepting) && !_introStarted)
            {
                var elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - start);
                var left = AcceptSeconds - (int)elapsed;
                if (left <= 0)
                {
                    YesText.Value = _localAccepted ? "已接受" : "接受(0)";
                    while (IsPhase(generation, PvpPagePhase.Accepting) && !_introStarted)
                    {
                        await Task.Yield();
                    }

                    return;
                }

                if (!_localAccepted)
                {
                    YesText.Value = $"接受({left})";
                }

                var nextTick = start + (AcceptSeconds - left) + 1f;
                while (IsPhase(generation, PvpPagePhase.Accepting) && !_introStarted && Time.realtimeSinceStartup < nextTick)
                {
                    await Task.Yield();
                }
            }
        }

        private void OnAcceptUpdated(string[] userIds)
        {
            if (userIds == null || _phase != PvpPagePhase.Accepting)
            {
                return;
            }

            for (var i = 0; i < userIds.Length; i++)
            {
                if (!string.IsNullOrEmpty(userIds[i]))
                {
                    _acceptedIds.Add(userIds[i]);
                }
            }

            PublishSeats();
        }

        private void PublishSeats()
        {
            var roster = _pvp.RoomPlayers ?? Array.Empty<PlayerPublic>();
            var seats = new PvpReadySeat[roster.Length];
            for (var i = 0; i < roster.Length; i++)
            {
                var player = roster[i];
                var revealed = player != null && IsAccepted(player.UserId);
                seats[i] = new PvpReadySeat
                {
                    UserId = player != null ? player.UserId : string.Empty,
                    NickName = player != null ? player.NickName : string.Empty,
                    AvatarUrl = player != null ? player.AvatarUrl : string.Empty,
                    Revealed = revealed
                };
            }

            Seats = seats;
            SeatsChanged?.Invoke();
        }

        private bool IsAccepted(string userId)
        {
            foreach (var id in _acceptedIds)
            {
                if (PvpMatchSession.SameUser(id, userId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>全员接受后头像往外飞，再进局。</summary>
        private async void OnAllAccepted()
        {
            if (_phase != PvpPagePhase.Accepting || _entering || _introStarted || !IsOpen)
            {
                return;
            }

            var generation = _generation;
            _introStarted = true;
            ApplySuccess();
            await PlaySuccessThenEnterAsync(generation);
        }

        /// <summary>藏起排队和接受按钮，只留四个头像往外飞。</summary>
        private void ApplySuccess()
        {
            ShowSearch.Value = false;
            ShowSearchSuccess.Value = true;
            ShowStart.Value = false;
            ShowCancel.Value = false;
            ShowSearchTime.Value = false;
            ShowMatchSuccess.Value = false;
            ShowYes.Value = false;
            ShowCancelPvp.Value = false;
            RaiseCommands();
        }

        private async Task PlaySuccessThenEnterAsync(int generation)
        {
            var play = PlaySuccessIntro;
            var finished = true;
            if (play != null)
            {
                try
                {
                    finished = await play();
                }
                catch (Exception ex)
                {
                    AppLog.Exception(LogChannel.UI, ex);
                    finished = false;
                }
            }

            if (!finished || !IsPhase(generation, PvpPagePhase.Accepting) || _entering)
            {
                return;
            }

            await EnterMatchAsync();
        }

        private void RaiseCommands()
        {
            StartCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            AcceptCommand.RaiseCanExecuteChanged();
            DeclineCommand.RaiseCanExecuteChanged();
        }

        private enum PvpPagePhase
        {
            Idle = 0,
            Searching = 1,
            Accepting = 2
        }
    }

    public sealed class PvpReadySeat
    {
        public string UserId { get; set; } = string.Empty;

        public string NickName { get; set; } = string.Empty;

        public string AvatarUrl { get; set; } = string.Empty;

        /// <summary>已点接受，或机器人已被服务端替点接受。未揭开时名称和头像显示 ???。</summary>
        public bool Revealed { get; set; }
    }
}
