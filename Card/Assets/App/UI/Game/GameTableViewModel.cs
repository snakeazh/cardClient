using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Atlas;
using App.Audio;
using App.Bootstrap;
using App.Game;
using App.Guide;
using App.Level;
using App.Net;
using App.Resources;
using App.UI.Popup;
using App.UI.Game.Director;
using CardShare.Contracts;
using Framework.Assets;
using Framework.Log;
using Framework.Save;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    /// <summary>把 <see cref="GameSession"/> 的状态绑到桌面 HUD：下注、看牌、商店、攻击。</summary>
    public sealed class GameTableViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly IGuideService _guide;
        private readonly ISaveService _save;
        private readonly IAudioService _audio;
        private readonly PvpMatchSession _pvp;
        private readonly BattleDirector _pvpDirector;
        private readonly PvpBattleDriver _pvpDriver;
        private GameResourceViewModel _gameResource;
        private bool _shopPopupOpen;
        private bool _resultPopupOpen;
        private bool _infoPopupOpen;
        private bool _remainListOpen;
        private bool _settleShownThisShop;
        private string _shownInfoKey;
        private bool _pvpShopDoneRequested;
        private bool _pvpReconnecting;
        private bool _pvpLeaving;
        private long _pvpDeadlineLocalMs;

        // Refresh 挂在 Session.Changed 上(每个动作/演出帧都进):字符串只在源值变化时重建
        private int _lastStage = -1;
        private bool _lastHasBoss = true;
        private int _lastGold = -1;
        private int _lastCourage = -1;
        private int _lastRoundIndex = -1;
        private int _lastRubCur = -1;
        private int _lastRubMax = -1;
        private int _lastXrayCur = -1;
        private int _lastXrayMax = -1;
        private int _lastReplaceCur = -1;
        private int _lastReplaceMax = -1;
        private readonly System.Text.StringBuilder _logBuilder = new System.Text.StringBuilder(256);
        private int _lastLogCount = -1;
        private string _lastLogTail;

        public GameTableViewModel(
            GameSession session,
            IResourceService resources,
            IUIManager ui,
            NavigationViewModel navigation,
            ILevelProgressService progress,
            IAtlasService atlas,
            IGuideService guide,
            ISaveService save,
            IAudioService audio,
            PvpMatchSession pvp)
        {
            Session = session;
            Resources = resources;
            Progress = progress;
            Atlas = atlas;
            _ui = ui;
            _navigation = navigation;
            _guide = guide ?? throw new ArgumentNullException(nameof(guide));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _audio = audio;
            _pvp = pvp;
            _pvpDirector = new BattleDirector(session);
            _pvpDriver = new PvpBattleDriver(session, _pvpDirector);
            Session.AttachPvpSession(pvp);
            Session.Changed += Refresh;
            BlindBetCommand = new RelayCommand(
                () => Session.BlindBet(),
                () => !Session.AiActing &&
                      !Session.Player.Folded &&
                      (Session.Phase == GamePhase.Betting ||
                       Session.Phase == GamePhase.WaitingLookChoice ||
                       (Session.Phase == GamePhase.WaitingRub && Session.Player.Looked)));
            RaiseCommand = new RelayCommand(() => Session.RaiseBet(), () => Session.PlayerMayRaise);
            RaiseHighCommand = new RelayCommand(() => Session.RaiseBetHigh(), () => Session.PlayerMayRaise);
            LookCommand = new RelayCommand(() => Session.LookCards(), () => Session.PlayerMayLookCards);
            FoldCommand = new RelayCommand(() => Session.Fold(), () => Session.PlayerMayFold);
            OpenCommand = new RelayCommand(
                () =>
                {
                    if (Session.IsPvp)
                    {
                        SendPvp(_pvp.Invoker.EnqueueShowdown());
                        return;
                    }

                    Session.RequestShowdown();
                },
                () => Session.PlayerMayCompare);
            AllInCommand = new RelayCommand(() => Session.AllIn(), () => Session.PlayerMayAllIn);
            PeekGoodCommand = new RelayCommand(
                () => Session.UsePeekGood(),
                () => Session.PlayerMayTogglePeekGood);
            ChaKanGoodCommand = new RelayCommand(
                () =>
                {
                    if (Session.IsPvp)
                    {
                        SendPvpSkill(() => _pvp.Invoker.EnqueuePeek());
                        return;
                    }

                    Session.UseChaKanGood();
                },
                () => Session.IsPvp ? Session.PlayerMayUseChaKanGood : Session.PlayerMayUseChaKanGood);
            XRayPlayerCommand = new RelayCommand(
                () => Session.TryXRayPlayer(),
                () => Session.SelectingXRayTarget && Session.PlayerMayUseChaKanGood);
            TiHuanGoodCommand = new RelayCommand(
                () =>
                {
                    if (Session.IsPvp)
                    {
                        Session.ClearPvpSelection();
                        SendPvpSkill(() => _pvp.Invoker.EnqueueReplace());
                        return;
                    }

                    Session.UseTiHuanGood();
                },
                () => Session.PlayerMayUseTiHuanGood);
            RubCommand = new RelayCommand(() => Session.TryRubSelected(), () => Session.Phase == GamePhase.WaitingRub);
            SkipRubCommand = new RelayCommand(() => Session.CancelLookOrRub(), () => Session.PlayerMayCancelLookOrRub);
            MinusBetCommand = new RelayCommand(() => Session.AdjustBetUnits(-GameBalance.MinBet), () => Session.Phase == GamePhase.Betting);
            PlusBetCommand = new RelayCommand(() => Session.AdjustBetUnits(GameBalance.MinBet), () => Session.Phase == GamePhase.Betting);
            ContinueCommand = new RelayCommand(
                () => Session.Continue(),
                () => Session.Phase == GamePhase.RoundSettle ||
                      (Session.Phase == GamePhase.WaitingAttack &&
                       !Session.AttackPlaying &&
                       !Session.SequentialCompare));
            BackCommand = new RelayCommand(OnBack);
            LeaveShopCommand = new RelayCommand(() => Session.LeaveShop(), () => Session.Phase == GamePhase.Shop);
            LoanCommand = new RelayCommand(() => Session.WatchAdLoan(), () => Session.Phase == GamePhase.StageFail);
            ReviveCommand = new RelayCommand(() => Session.WatchAdRevive(), () => Session.Phase == GamePhase.StageFail);
            RestartCommand = new RelayCommand(() => Session.RestartStage(), () => Session.Phase == GamePhase.StageFail);
            ExtraRubAdCommand = new RelayCommand(() => Session.WatchAdExtraRub());
            DoubleGoldAdCommand = new RelayCommand(() => Session.WatchAdDoubleGold(), () => Session.Phase == GamePhase.Shop);
            OpenRemainListCommand = new RelayCommand(OpenRemainList);
            ToggleSpeedCommand = new RelayCommand(TogglePlaybackSpeed);
            LoadPlaybackSpeed();
            AttackCommands = new IRelayCommand[3];
            for (var i = 0; i < AttackCommands.Length; i++)
            {
                var slot = i;
                AttackCommands[i] = new RelayCommand(
                    () => Session.AttackEnemyAtSlot(slot),
                    () => Session.CanAttackSlot(slot));
            }

            Refresh();
        }

        public GameSession Session { get; }
        public IResourceService Resources { get; }
        public ILevelProgressService Progress { get; }
        public IAtlasService Atlas { get; }

        public ObservableProperty<string> Title { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> Hint { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> GoldText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PotText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerChips { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerBet { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> PlayerState { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> StageInfoText { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> RoundInfo { get; } = new ObservableProperty<string>();
        public ObservableProperty<bool> ShowRoundInfo { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<string> RoundBuffName { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<string> RoundBuffDesc { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<bool> RoundBuffVisible { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<string> BetAmount { get; } = new ObservableProperty<string>();
        public ObservableProperty<string> LogText { get; } = new ObservableProperty<string>();
        public ObservableProperty<bool> ShowActions { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowTableButtons { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> ShowActionBar { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowLook { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowRub { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowCancel { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowBlind { get; } = new ObservableProperty<bool>();
        public ObservableProperty<string> BlindLabel { get; } = new ObservableProperty<string>("闷注");
        public ObservableProperty<string> RaiseLabel { get; } = new ObservableProperty<string>("x2下注");
        public ObservableProperty<string> RaiseHighLabel { get; } = new ObservableProperty<string>("x4下注");
        public ObservableProperty<string> AllInLabel { get; } = new ObservableProperty<string>("全部下注");
        public ObservableProperty<string> PeekGoodLabel { get; } = new ObservableProperty<string>("搓牌(3/3)");
        public ObservableProperty<bool> PeekGoodArmed { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<string> ChaKanGoodLabel { get; } = new ObservableProperty<string>("透视(1/1)");
        public ObservableProperty<string> TiHuanGoodLabel { get; } = new ObservableProperty<string>("替换(1/1)");
        public ObservableProperty<bool> ShowAllIn { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowFold { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowCompare { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowContinue { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowShop { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowFail { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowAttack { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowMask { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowHpText { get; } = new ObservableProperty<bool>();
        public ObservableProperty<string> HpText { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<bool> ShowCardInfo { get; } = new ObservableProperty<bool>(false);
        public ObservableProperty<bool> ShowYiwuBtn { get; } = new ObservableProperty<bool>(true);
        public ObservableProperty<float> PlaybackSpeed { get; } = new ObservableProperty<float>(MinPlaybackSpeed);
        public ObservableProperty<string> SpeedLabel { get; } = new ObservableProperty<string>("x1");
        public ObservableProperty<Sprite> CardTypeIcon { get; } = new ObservableProperty<Sprite>();
        public ObservableProperty<Sprite> CardTypeLabel { get; } = new ObservableProperty<Sprite>();
        public ObservableProperty<string> CardTypeNum { get; } = new ObservableProperty<string>(string.Empty);
        public bool IsHandSettling { get; private set; }
        public readonly ObservableProperty<bool>[] ShowEnemy =
        {
            new ObservableProperty<bool>(),
            new ObservableProperty<bool>(),
            new ObservableProperty<bool>()
        };
        public readonly ObservableProperty<string>[] EnemyChips =
        {
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty)
        };
        public readonly ObservableProperty<string>[] EnemyBet =
        {
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty)
        };
        public readonly ObservableProperty<string>[] EnemyState =
        {
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty),
            new ObservableProperty<string>(string.Empty)
        };

        public IRelayCommand BlindBetCommand { get; }
        public IRelayCommand RaiseCommand { get; }
        public IRelayCommand RaiseHighCommand { get; }
        public IRelayCommand LookCommand { get; }
        public IRelayCommand FoldCommand { get; }
        public IRelayCommand OpenCommand { get; }
        public IRelayCommand AllInCommand { get; }
        public IRelayCommand PeekGoodCommand { get; }
        public IRelayCommand ChaKanGoodCommand { get; }
        public IRelayCommand XRayPlayerCommand { get; }
        public IRelayCommand TiHuanGoodCommand { get; }
        public IRelayCommand RubCommand { get; }
        public IRelayCommand SkipRubCommand { get; }
        public IRelayCommand MinusBetCommand { get; }
        public IRelayCommand PlusBetCommand { get; }
        public IRelayCommand ContinueCommand { get; }
        public IRelayCommand BackCommand { get; }
        public IRelayCommand LeaveShopCommand { get; }
        public IRelayCommand LoanCommand { get; }
        public IRelayCommand ReviveCommand { get; }
        public IRelayCommand RestartCommand { get; }
        public IRelayCommand ExtraRubAdCommand { get; }
        public IRelayCommand DoubleGoldAdCommand { get; }
        public IRelayCommand OpenRemainListCommand { get; }
        public IRelayCommand ToggleSpeedCommand { get; }
        public IRelayCommand[] AttackCommands { get; }
        public const float MinPlaybackSpeed = 1f;
        public const float MaxPlaybackSpeed = 2f;
        public const string PlaybackSpeedSaveKey = "game.playback.speed.v1";
        private int _seenDealSerial = -1;
        private int _openingCompletedSerial = -1;

        /// <summary>每关第一手开场（对话+VS）尚未完成时拦住视觉发牌。</summary>
        public bool HoldDealVisual => ShouldHoldDealVisual();

        public bool ShouldHoldDealVisual()
        {
            return Session != null &&
                   !Session.IsPvp &&
                   Session.DealSerial > 0 &&
                   Session.StageRoundIndex == 1 &&
                   _openingCompletedSerial != Session.DealSerial;
        }

        public void CompleteOpening()
        {
            if (Session != null)
            {
                _openingCompletedSerial = Session.DealSerial;
            }
        }

        /// <summary>进局前调用。单例 VM 会残留上一局的开场完成标记。</summary>
        public void ResetOpeningGate()
        {
            _openingCompletedSerial = -1;
        }

        public void NotifyDealReady()
        {
            ShowTableButtons.Value = true;
            ShowRoundInfo.Value = true;
            GuideSignals.NotifyDealFinished(Session.DealSerial);
            if (Session.IsPvp)
            {
                Session.NotifyPvpDealFinished();
            }

            Refresh();
        }

        public void ToggleOpenCard(int index)
        {
            Session.TogglePlayerCard(index);
            if (!Session.IsPvp || Session.PvpHandLocked || _pvp == null || Session.Player.CountSelectedCards() != GameBalance.OpenHandSize)
            {
                return;
            }

            var pick = new int[GameBalance.OpenHandSize];
            var n = 0;
            for (var i = 0; i < Session.Player.CardSelected.Length && n < pick.Length; i++)
            {
                if (Session.Player.CardSelected[i])
                {
                    pick[n++] = i;
                }
            }

            SendPvp(_pvp.Invoker.EnqueuePick(pick));
        }

        public bool TryRubPlayerCard(int index)
        {
            if (Session.IsPvp)
            {
                if (!Session.CanRubPlayerCard(index) || _pvp == null)
                {
                    return false;
                }

                SendPvp(_pvp.Invoker.EnqueueRub(index));
                return true;
            }

            return Session.TryRubPlayerCard(index);
        }

        /// <summary>搓牌：PVE 本地换牌；PVP 发 rub 并等 match_update 写入 Session 后再揭面。</summary>
        public async Task<bool> TryRubPlayerCardAsync(int index)
        {
            if (!Session.IsPvp)
            {
                return Session.TryRubPlayerCard(index);
            }

            if (!Session.CanRubPlayerCard(index) || _pvp == null)
            {
                return false;
            }

            try
            {
                var ok = await _pvp.BattleAndWaitAsync(() => _pvp.Invoker.EnqueueRub(index));
                if (!ok)
                {
                    Toast.Error("搓牌超时，请重试");
                    return false;
                }

                // 快照已扣次：按剩余次数退出/保持点选，避免次数用尽后按钮因 CanExecute=false 取消不了。
                Session.RefreshRubSelectAfterRub();
                return true;
            }
            catch (GameApiException ex)
            {
                Toast.Error(GameApi.Describe(ex));
                return false;
            }
        }

        private async void SendPvp(Task task)
        {
            try
            {
                await task;
            }
            catch (GameApiException ex)
            {
                Toast.Error(GameApi.Describe(ex));
            }
        }

        /// <summary>PVP 技能：先挂 waiter 再发 battle，等 match_update 落桌后再继续（透视/替换）。</summary>
        private async void SendPvpSkill(Func<Task> send)
        {
            if (_pvp == null || send == null)
            {
                return;
            }

            try
            {
                var ok = await _pvp.BattleAndWaitAsync(send);
                if (!ok)
                {
                    Toast.Error("操作超时，请重试");
                }
            }
            catch (GameApiException ex)
            {
                Toast.Error(GameApi.Describe(ex));
            }
        }

        private void UnbindPvp()
        {
            if (_pvp == null)
            {
                return;
            }

            _pvp.Updated -= OnPvpUpdated;
            _pvp.Finished -= OnPvpFinished;
            _pvp.Failed -= OnPvpFailed;
            _pvp.MatchEvent -= OnPvpMatchEvent;
            _pvp.Notice -= OnPvpNotice;
            _pvp.Reconnecting -= OnPvpReconnecting;
            _pvp.Reconnected -= OnPvpReconnected;
            _pvpShopDoneRequested = false;
            _pvpReconnecting = false;
        }

        private void OnPvpUpdated()
        {
            if (!Session.IsPvp || _pvp == null)
            {
                return;
            }

            var state = _pvp.State;
            _pvpDeadlineLocalMs = state != null && state.PhaseDeadlineUtcMs > 0
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (state.PhaseDeadlineUtcMs - state.ServerNowUtcMs)
                : 0;
            _pvpDriver.OnMatch(state, GameApi.Client != null ? GameApi.Client.UserId : string.Empty);
        }

        /// <summary>PVP 选牌倒计时：服务器快照下发 deadline + 服务器当前时刻，换算成本地截止点后本地秒级跳动。到期不做任何事，等服务器快照驱动。</summary>
        public void TickPvpCountdown()
        {
            if (!Session.IsPvp || _pvpDeadlineLocalMs <= 0)
            {
                return;
            }

            RoundInfo.Value = FormatRoundInfo();
        }

        private string FormatRoundInfo()
        {
            if (Session.IsPvp && _pvp != null && _pvp.State != null)
            {
                var round = $"第{_pvp.State.Round}轮";
                if (_pvpDeadlineLocalMs <= 0)
                {
                    return round;
                }

                var leftMs = _pvpDeadlineLocalMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var seconds = (int)Math.Max(0, (leftMs + 999) / 1000);
                var phase = _pvp.State.Phase;
                var label = string.Equals(phase, "shop", StringComparison.OrdinalIgnoreCase)
                    ? "商店"
                    : string.Equals(phase, "settle", StringComparison.OrdinalIgnoreCase)
                        ? "结算"
                        : "选牌";
                return $"{round} · {label} {seconds}s";
            }

            return $"第{Session.StageRoundIndex}轮";
        }

        /// <summary>match_event 入口：演出类事件转给 Driver，商店/掉线提示在这里处理。同批 match_update 已先处理。</summary>
        private void OnPvpMatchEvent(PvpMatchEventDto evt)
        {
            if (!Session.IsPvp || _pvp == null || evt == null)
            {
                return;
            }

            var userId = GameApi.Client != null ? GameApi.Client.UserId : string.Empty;
            _pvpDriver.OnEvent(evt, userId);
            if (evt.Kind == "shop_start")
            {
                TryPresentShopPopup();
                return;
            }

            if (evt.Kind == "player_offline" || evt.Kind == "player_online")
            {
                NotifyPvpPresence(evt, userId);
            }
        }

        /// <summary>对手掉线/回来：Toast 提示（座位"离线"标识由 ApplyPvpTable 按 fighter.Disconnected 刷新）。自己的重连走 Reconnecting/Reconnected。</summary>
        private void NotifyPvpPresence(PvpMatchEventDto evt, string userId)
        {
            if (PvpMatchSession.SameUser(evt.UserId, userId))
            {
                return;
            }

            var nick = FindPvpNick(evt.UserId);
            if (string.IsNullOrEmpty(nick))
            {
                return;
            }

            Toast.Show(evt.Kind == "player_offline" ? $"{nick} 掉线了，由托管代打" : $"{nick} 回来了");
        }

        private string FindPvpNick(string userId)
        {
            var players = _pvp != null && _pvp.State != null ? _pvp.State.Players : null;
            if (players != null)
            {
                for (var i = 0; i < players.Length; i++)
                {
                    if (PvpMatchSession.SameUser(players[i].UserId, userId))
                    {
                        return players[i].NickName;
                    }
                }
            }

            return _pvp != null && _pvp.TryGetPlayer(userId, out var player) ? player.NickName : null;
        }

        private void OnPvpNotice(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Toast.Error(message);
            }
        }

        private void OnPvpReconnecting()
        {
            if (!Session.IsPvp)
            {
                return;
            }

            _pvpReconnecting = true;
            ShowMask.Value = true;
            Toast.Show("连接中断，正在重连…");
        }

        private void OnPvpReconnected()
        {
            _pvpReconnecting = false;
            ShowMask.Value = false;
            Toast.Show("已重新连接");
            Refresh();
        }

        private async void OnPvpFinished()
        {
            if (!Session.IsPvp || _pvpLeaving)
            {
                return;
            }

            // 服务端在 settle 演出窗结束后才发 finished，演出必然已排空，直接结算。
            _pvpLeaving = true;
            await LeaveAfterPvpFinished();
        }

        private async Task LeaveAfterPvpFinished()
        {
            await ShowPvpResultAsync();
            await StopPvpAndLeave();
        }

        /// <summary>PVP 结算：名次 + 名次奖励金币 + 全桌排名（简化弹窗，复用通用确认框，不新建预制体）。</summary>
        private async Task ShowPvpResultAsync()
        {
            if (_ui == null || _pvp == null)
            {
                return;
            }

            var state = _pvp.State;
            var players = state != null ? state.Players : null;
            if (players == null || players.Length == 0)
            {
                return;
            }

            var userId = GameApi.Client != null ? GameApi.Client.UserId : string.Empty;
            var ranked = new List<PvpFighterDto>(players);
            ranked.Sort((a, b) => RankOf(a).CompareTo(RankOf(b)));
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < ranked.Count; i++)
            {
                var p = ranked[i];
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(p.Rank > 0 ? $"第{p.Rank}名" : "未排名");
                sb.Append(' ').Append(string.IsNullOrEmpty(p.NickName) ? "玩家" : p.NickName);
                if (p.IsBot)
                {
                    sb.Append("(bot)");
                }

                if (PvpMatchSession.SameUser(p.UserId, userId))
                {
                    sb.Append("（你）");
                }

                if (p.RewardGold > 0)
                {
                    sb.Append("  +").Append(p.RewardGold).Append("金币");
                }
            }

            try
            {
                await _ui.Dialogs.ConfirmAsync("PVP 结算", sb.ToString(), DialogButtons.Ok, "返回大厅");
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                var self = _pvp.Self;
                Toast.Show(self != null && self.Rank > 0 ? $"PVP 结束，第 {self.Rank} 名" : "PVP 结束");
            }
        }

        private static int RankOf(PvpFighterDto fighter)
        {
            return fighter != null && fighter.Rank > 0 ? fighter.Rank : int.MaxValue;
        }

        private async void OnPvpFailed(string message)
        {
            if (_pvpLeaving)
            {
                return;
            }

            _pvpLeaving = true;
            Toast.Error(string.IsNullOrEmpty(message) ? "对战中断" : message);
            await StopPvpAndLeave();
        }

        private async Task StopPvpAndLeave()
        {
            _pvpReconnecting = false;
            ShowMask.Value = false;
            if (_shopPopupOpen && _ui != null)
            {
                // 商店弹窗还挂着（如断线判死时）：先关掉再离场，避免残留在 Popup 层。
                await _ui.Dialogs.CloseWithResult(false);
            }

            _pvpDriver.Reset();
            if (_pvp != null)
            {
                await _pvp.StopAsync();
            }

            Session.EndPvp();
            if (IsOpen)
            {
                await LeaveToHome();
            }
        }

        public void Refresh()
        {
            if (Session.IsPvp && Session.Phase != GamePhase.Shop)
            {
                _pvpShopDoneRequested = false;
                if (_shopPopupOpen)
                {
                    // PVP 商店：阶段离开 shop（服务端推进）时从外部关掉弹窗。
                    _ = _ui.Dialogs.CloseWithResult(false);
                }
            }

            if (Session.DealSerial != _seenDealSerial)
            {
                _seenDealSerial = Session.DealSerial;
                if (Session.DealSerial > 0)
                {
                    ShowTableButtons.Value = false;
                    ShowRoundInfo.Value = false;
                }
            }

            var run = Session.Run;
            if (run.Stage != _lastStage || run.HasBoss != _lastHasBoss)
            {
                _lastStage = run.Stage;
                _lastHasBoss = run.HasBoss;
                Title.Value = run.HasBoss
                    ? $"第{run.Stage}关 BOSS"
                    : $"第{run.Stage}关";
                StageInfoText.Value = $"第{run.Stage}关";
            }

            var entries = BossMechanics.ResolveAll(run);
            Title.Value = run.HasBoss
                ? $"第{run.Stage}关 BOSS"
                : $"第{run.Stage}关";
            StageInfoText.Value = $"第{run.Stage}关";
            if (Session.IsPvp && _pvp != null && _pvp.State != null)
            {
                var fight = string.Equals(_pvp.State.FightKind, "monster", StringComparison.OrdinalIgnoreCase)
                    ? "野怪"
                    : "对战";
                Title.Value = $"PVP 第{_pvp.State.Round}轮 {fight}";
                StageInfoText.Value = Title.Value;
            }
            RoundBuffVisible.Value = entries.Count > 0;
            RoundBuffName.Value = FormatEntryNames(entries);
            RoundBuffDesc.Value = FormatEntryDescs(entries);
            Hint.Value = Session.Hint ?? string.Empty;
            if (run.Gold != _lastGold)
            {
                _lastGold = run.Gold;
                GoldText.Value = _lastGold.ToString();
            }
            if (_gameResource != null)
            {
                _gameResource.ShowBackBtn.Value = !_shopPopupOpen && !_resultPopupOpen;
            }
            PotText.Value = string.Empty;
            if (Session.Player.Courage != _lastCourage)
            {
                _lastCourage = Session.Player.Courage;
                PlayerChips.Value = $"勇气 {_lastCourage}";
            }

            PlayerBet.Value = BetLabel(Session.Player);
            PlayerState.Value = SeatLine(Session.Player);
            if (Session.StageRoundIndex != _lastRoundIndex)
            {
                _lastRoundIndex = Session.StageRoundIndex;
                RoundInfo.Value = $"第{_lastRoundIndex}轮";
            }

            PlayerChips.Value = Session.IsPvp
                ? $"HP {Session.Player.Hp}/{Session.Player.MaxHp}"
                : $"勇气 {Session.Player.Courage}";
            PlayerBet.Value = BetLabel(Session.Player);
            PlayerState.Value = SeatLine(Session.Player);
            RoundInfo.Value = FormatRoundInfo();
            BetAmount.Value = string.Empty;
            var canAct = !Session.Player.Folded && !Session.AiActing;
            var opening = Session.Phase == GamePhase.WaitingOpen && canAct;
            var rubbing = Session.Phase == GamePhase.WaitingRub && canAct;
            var rubMax = SkillChargeMax(
                GameBalance.SkillRubUses + Session.Run.BonusRubCharges,
                RelicMechanics.SumValue(Session.Run, CardShare.Contracts.Config.MechanismType.RubbingCardsNum) +
                HeroMechanics.SumValue(Session.Run, CardShare.Contracts.Config.MechanismType.RubbingCardsNum));
            if (Session.Run.PeekGoodCharges != _lastRubCur || rubMax != _lastRubMax)
            {
                _lastRubCur = Session.Run.PeekGoodCharges;
                _lastRubMax = rubMax;
                PeekGoodLabel.Value = FormatCharges("搓牌", _lastRubCur, _lastRubMax);
            }

            PeekGoodArmed.Value = Session.SelectingRubTarget;
            var xrayMax = SkillChargeMax(
                GameBalance.SkillXRayUses + Session.Run.BonusXRayCharges,
                RelicMechanics.SumValue(Session.Run, CardShare.Contracts.Config.MechanismType.PerspectiveNum) +
                HeroMechanics.SumValue(Session.Run, CardShare.Contracts.Config.MechanismType.PerspectiveNum));
            if (Session.Run.ChaKanGoodCharges != _lastXrayCur || xrayMax != _lastXrayMax)
            {
                _lastXrayCur = Session.Run.ChaKanGoodCharges;
                _lastXrayMax = xrayMax;
                ChaKanGoodLabel.Value = FormatCharges("透视", _lastXrayCur, _lastXrayMax);
            }

            var replaceMax = GameBalance.SkillReplaceUses + Session.Run.BonusReplaceCharges;
            if (Session.Run.TiHuanGoodCharges != _lastReplaceCur || replaceMax != _lastReplaceMax)
            {
                _lastReplaceCur = Session.Run.TiHuanGoodCharges;
                _lastReplaceMax = replaceMax;
                TiHuanGoodLabel.Value = FormatCharges("替换", _lastReplaceCur, _lastReplaceMax);
            }
            PeekGoodLabel.Value = FormatCharges(
                "搓牌",
                Session.Run.PeekGoodCharges,
                SkillChargeMax(
                    GameBalance.SkillRubUses + Session.Run.BonusRubCharges,
                    RelicMechanics.SumValue(Session.Run, CardShare.Contracts.Config.MechanismType.RubbingCardsNum) +
                    HeroMechanics.SumValue(Session.Run, CardShare.Contracts.Config.MechanismType.RubbingCardsNum)));
            PeekGoodArmed.Value = Session.SelectingRubTarget;
            ChaKanGoodLabel.Value = FormatCharges(
                "透视",
                Session.Run.ChaKanGoodCharges,
                SkillChargeMax(
                    GameBalance.SkillXRayUses + Session.Run.BonusXRayCharges,
                    RelicMechanics.SumValue(Session.Run, CardShare.Contracts.Config.MechanismType.PerspectiveNum)));
            TiHuanGoodLabel.Value = FormatCharges(
                "替换",
                Session.Run.TiHuanGoodCharges,
                GameBalance.SkillReplaceUses + Session.Run.BonusReplaceCharges);
            ShowLook.Value = false;
            ShowBlind.Value = false;
            ShowActions.Value = false;
            ShowRub.Value = false;
            ShowCancel.Value = Session.PlayerMayCancelLookOrRub;
            ShowAllIn.Value = false;
            ShowFold.Value = false;
            ShowCompare.Value = ShowTableButtons.Value && opening && Session.PlayerMayCompare;
            ShowActionBar.Value = opening || rubbing || ShowCompare.Value;
            ShowContinue.Value = Session.Phase == GamePhase.RoundSettle ||
                                 (Session.Phase == GamePhase.WaitingAttack &&
                                  !Session.AttackPlaying &&
                                  !Session.SequentialCompare);
            ShowShop.Value = Session.Phase == GamePhase.Shop;
            ShowFail.Value = Session.Phase == GamePhase.StageFail;
            if (Session.Phase != GamePhase.Shop)
            {
                _settleShownThisShop = false;
            }

            TryPresentShopPopup();
            TryPresentResultPopup();
            TryPresentGamePopupInfo();
            ShowAttack.Value = Session.IsPvp
                ? Session.AttackPlaying
                : !Session.SequentialCompare &&
                  (Session.Phase == GamePhase.WaitingAttack || Session.SelectingOpenTarget);
            if (!Session.AttackPlaying && !_pvpReconnecting)
            {
                ShowMask.Value = false;
                ShowHpText.Value = false;
            }
            RefreshEnemies();
            RefreshCardInfo();

            // 日志 O(n²) 拼接 → StringBuilder 复用,且仅在条数或末行变化时重建
            var logCount = run.Log.Count;
            var logTail = logCount > 0 ? run.Log[logCount - 1] : null;
            if (logCount != _lastLogCount || !ReferenceEquals(logTail, _lastLogTail))
            {
                _lastLogCount = logCount;
                _lastLogTail = logTail;
                var start = logCount > 8 ? logCount - 8 : 0;
                _logBuilder.Clear();
                for (var i = start; i < logCount; i++)
                {
                    if (i > start)
                    {
                        _logBuilder.Append('\n');
                    }

                    _logBuilder.Append(run.Log[i]);
                }

                LogText.Value = _logBuilder.ToString();
            }

            BlindBetCommand.RaiseCanExecuteChanged();
            RaiseCommand.RaiseCanExecuteChanged();
            RaiseHighCommand.RaiseCanExecuteChanged();
            LookCommand.RaiseCanExecuteChanged();
            FoldCommand.RaiseCanExecuteChanged();
            OpenCommand.RaiseCanExecuteChanged();
            AllInCommand.RaiseCanExecuteChanged();
            PeekGoodCommand.RaiseCanExecuteChanged();
            ChaKanGoodCommand.RaiseCanExecuteChanged();
            XRayPlayerCommand.RaiseCanExecuteChanged();
            TiHuanGoodCommand.RaiseCanExecuteChanged();
            RubCommand.RaiseCanExecuteChanged();
            SkipRubCommand.RaiseCanExecuteChanged();
            ContinueCommand.RaiseCanExecuteChanged();
            LeaveShopCommand.RaiseCanExecuteChanged();
            LoanCommand.RaiseCanExecuteChanged();
            ReviveCommand.RaiseCanExecuteChanged();
            RestartCommand.RaiseCanExecuteChanged();
            DoubleGoldAdCommand.RaiseCanExecuteChanged();
            for (var i = 0; i < AttackCommands.Length; i++)
            {
                AttackCommands[i].RaiseCanExecuteChanged();
            }
        }

        protected override async Task OnOpen(object args)
        {
            BattleTrace.Log(
                $"GameTableVM.OnOpen begin deal={Session?.DealSerial} stageRound={Session?.StageRoundIndex} hold={ShouldHoldDealVisual()}");
            _shownInfoKey = null;
            Session.Changed -= Refresh;
            Session.Changed += Refresh;
            Refresh();
            BattleTrace.Log("GameTableVM StartBattleBgmAsync…");
            await StartBattleBgmAsync();
            BattleTrace.Log("GameTableVM ShowGameResource…");
            await ShowGameResource();
            if (!Session.IsPvp)
            {
                _guide.TryStart(CardShare.Contracts.Config.GuideTriggerType.ScreenOpen, AppScreenIds.GameUI);
            }

            if (_pvp != null)
            {
                _pvpLeaving = false;
                _pvp.Updated += OnPvpUpdated;
                _pvp.Finished += OnPvpFinished;
                _pvp.Failed += OnPvpFailed;
                _pvp.MatchEvent += OnPvpMatchEvent;
                _pvp.Notice += OnPvpNotice;
                _pvp.Reconnecting += OnPvpReconnecting;
                _pvp.Reconnected += OnPvpReconnected;
                if (Session.IsPvp && _pvp.State != null)
                {
                    OnPvpUpdated();
                }
            }

            BattleTrace.Log("GameTableVM.OnOpen end");
        }

        private async Task StartBattleBgmAsync()
        {
            if (_audio == null || Resources == null)
            {
                return;
            }

            try
            {
                var clip = await Resources.LoadAsync<AudioClip>(ResResourcePaths.BgmBattle);
                _audio.PlayBgm(clip);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Assets, "Battle BGM load failed: " + ex.Message);
            }
        }

        protected override async Task OnClose()
        {
            RestorePlaybackSpeed();
            _guide.Abort();
            ReleaseBattleResources();
            UnbindPvp();
            await CloseGameResource();
        }

        protected override void OnDispose()
        {
            RestorePlaybackSpeed();
            UnbindPvp();
            Session.Changed -= Refresh;
            ReleaseBattleResources();
            _ = CloseGameResource();
        }

        /// <summary>退局卸载：战斗 BGM 与 _damage/_dead 立绘（与加载点一一对应；未进缓存时 Release 为空操作，重复调用安全）。</summary>
        private void ReleaseBattleResources()
        {
            Resources?.Release(ResResourcePaths.BgmBattle);
            PortraitLoader.ReleaseBattleStates();
        }

        private void TogglePlaybackSpeed()
        {
            var next = NormalizePlaybackSpeed(PlaybackSpeed.Value) >= MaxPlaybackSpeed
                ? MinPlaybackSpeed
                : MaxPlaybackSpeed;
            ApplyPlaybackSpeed(next);
            PersistPlaybackSpeed(next);
        }

        private void LoadPlaybackSpeed()
        {
            ApplyPlaybackSpeed(NormalizePlaybackSpeed(_save.GetFloat(PlaybackSpeedSaveKey, MinPlaybackSpeed)));
        }

        private void ApplyPlaybackSpeed(float speed)
        {
            PlaybackSpeed.Value = speed;
            SpeedLabel.Value = FormatPlaybackSpeed(speed);
        }

        private void PersistPlaybackSpeed(float speed)
        {
            _save.SetFloat(PlaybackSpeedSaveKey, speed);
            _save.Save();
        }

        public static void RestorePlaybackSpeed()
        {
            Time.timeScale = 1f;
        }

        private static float NormalizePlaybackSpeed(float speed)
        {
            return speed >= MaxPlaybackSpeed - 0.01f ? MaxPlaybackSpeed : MinPlaybackSpeed;
        }

        private static string FormatPlaybackSpeed(float speed)
        {
            return NormalizePlaybackSpeed(speed) >= MaxPlaybackSpeed ? "x2" : "x1";
        }

        private async Task ShowGameResource()
        {
            await CloseGameResource();
            if (_ui == null)
            {
                return;
            }

            var registration = _ui.Registry.GetByViewModelType(typeof(GameResourceViewModel));
            _gameResource = (GameResourceViewModel)_ui.Registry.CreateViewModel(registration);
            _gameResource.BindBack(BackCommand);
            await _ui.Open(_gameResource);
        }

        private async Task CloseGameResource()
        {
            var bar = _gameResource;
            _gameResource = null;
            if (bar == null || !bar.IsOpen || _ui == null)
            {
                return;
            }

            await _ui.Close(bar);
        }

        private void SetShowBackBtn(bool visible)
        {
            if (_gameResource != null)
            {
                _gameResource.ShowBackBtn.Value = visible;
            }
        }

        private async void TryPresentResultPopup()
        {
            if (!IsOpen ||
                _resultPopupOpen ||
                _shopPopupOpen ||
                _ui == null)
            {
                return;
            }

            if (Session.Phase != GamePhase.RunComplete && Session.Phase != GamePhase.StageFail)
            {
                return;
            }

            _resultPopupOpen = true;
            SetShowBackBtn(false);
            try
            {
                await PresentResultThenLeaveOrRetry();
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _resultPopupOpen = false;
                SetShowBackBtn(!_shopPopupOpen);
            }

            TryPresentGamePopupInfo();
        }

        private async Task PresentResultThenLeaveOrRetry()
        {
            var action = await ShowBattleResultAsync();
            if (action == BattleResultAction.Again)
            {
                return;
            }

            await LeaveToHome();
        }

        private async void TryPresentShopPopup()
        {
            if (!IsOpen || _shopPopupOpen || _ui == null)
            {
                return;
            }

            if (Session.IsPvp)
            {
                // PVP 商店：shop_start / 快照进入 shop 阶段时开一次；点"下一关"发 shop_done 关闭；
                // 服务端推进阶段（phase 离开 shop）由 Refresh 从外部关闭。
                if (!IsPvpShopOpen())
                {
                    return;
                }

                _shopPopupOpen = true;
                SetShowBackBtn(false);
                try
                {
                    var pvpRegistration = _ui.Registry.GetByViewModelType(typeof(BattleShopPopViewModel));
                    var pvpPopup = (BattleShopPopViewModel)_ui.Registry.CreateViewModel(pvpRegistration);
                    await _ui.Dialogs.ShowCustomAsync<BattleShopPopViewModel, bool>(pvpPopup);
                }
                catch (Exception ex)
                {
                    AppLog.Exception(LogChannel.UI, ex);
                }
                finally
                {
                    _shopPopupOpen = false;
                    // 本阶段不再自动重开（已 shop_done 或被外部关闭）；phase 离开 shop 时 Refresh 会清零。
                    _pvpShopDoneRequested = true;
                    SetShowBackBtn(!_resultPopupOpen);
                }

                return;
            }

            if (Session.Phase != GamePhase.Shop)
            {
                return;
            }

            _shopPopupOpen = true;
            SetShowBackBtn(false);
            try
            {
                if (Session.IsLastLevel)
                {
                    Session.LeaveShop();
                }
                else
                {
                    if (!_settleShownThisShop)
                    {
                        _settleShownThisShop = true;
                        try
                        {
                            var settleReg = _ui.Registry.GetByViewModelType(typeof(BattleSettleUpPopViewModel));
                            var settle = (BattleSettleUpPopViewModel)_ui.Registry.CreateViewModel(settleReg);
                            await _ui.Dialogs.ShowCustomAsync<BattleSettleUpPopViewModel, bool>(settle);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Exception(LogChannel.UI, ex);
                        }
                    }

                    await Session.WaitShopReadyAsync();

                    while (IsOpen && Session.Phase == GamePhase.Shop)
                    {
                        var registration = _ui.Registry.GetByViewModelType(typeof(BattleShopPopViewModel));
                        var popup = (BattleShopPopViewModel)_ui.Registry.CreateViewModel(registration);
                        await _ui.Dialogs.ShowCustomAsync<BattleShopPopViewModel, bool>(popup);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _shopPopupOpen = false;
                SetShowBackBtn(!_resultPopupOpen);
            }

            TryPresentResultPopup();
            TryPresentGamePopupInfo();
        }

        /// <summary>PVP 商店弹窗条件：shop 阶段 + 本人有商店（淘汰观战 Shop 为 null）+ 未点过"下一关"（shop_done）。</summary>
        private bool IsPvpShopOpen()
        {
            return Session.IsPvp &&
                   _pvp != null &&
                   _pvp.State != null &&
                   Session.Phase == GamePhase.Shop &&
                   _pvp.State.Shop != null &&
                   !_pvp.State.Shop.Done &&
                   !_pvpShopDoneRequested;
        }

        private async Task<BattleResultAction> ShowBattleResultAsync(bool forfeitNoRevive = false)
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(BattleResultPopupViewModel));
            var popup = (BattleResultPopupViewModel)_ui.Registry.CreateViewModel(registration);
            return await _ui.Dialogs.ShowCustomAsync<BattleResultPopupViewModel, BattleResultAction>(
                popup,
                forfeitNoRevive);
        }

        private async Task<bool> ShowCommonTopAsync()
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(CommonTopViewModel));
            var popup = (CommonTopViewModel)_ui.Registry.CreateViewModel(registration);
            return await _ui.Dialogs.ShowCustomAsync<CommonTopViewModel, bool>(popup);
        }

        private async void OnBack()
        {
            if (_ui == null || _resultPopupOpen || _shopPopupOpen)
            {
                return;
            }

            if (Session.IsPvp)
            {
                var confirmed = await ShowCommonTopAsync();
                if (!confirmed || !IsOpen)
                {
                    return;
                }

                await StopPvpAndLeave();
                return;
            }

            _resultPopupOpen = true;
            SetShowBackBtn(false);
            try
            {
                var confirmed = await ShowCommonTopAsync();
                if (!confirmed || !IsOpen)
                {
                    return;
                }

                var action = await ShowBattleResultAsync(forfeitNoRevive: true);
                if (action == BattleResultAction.Again)
                {
                    return;
                }

                await LeaveToHome();
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _resultPopupOpen = false;
                SetShowBackBtn(!_shopPopupOpen);
            }
        }

        private async Task LeaveToHome()
        {
            // 关闭 GameUI 后 navigator 自动重新显示压在栈底的 Home（开局时未销毁），
            // 直接复用并刷新；栈里没有 Home 的异常路径兜底新建打开。
            await _ui.Close(this);
            var home = _ui.FindOpen<HomeViewModel>();
            if (home != null)
            {
                await home.RefreshOnReturnAsync();
            }
            else
            {
                home = (HomeViewModel)_ui.Registry.CreateViewModel(
                    _ui.Registry.GetByViewModelType(typeof(HomeViewModel)));
                await _ui.Open(home);
            }

            await _navigation.EnsureShown();
            TryStartFirstTalentGuide();
        }

        private void TryStartFirstTalentGuide()
        {
            if (_guide == null || _guide.IsRunning)
            {
                return;
            }

            if (!AppServices.IsReady)
            {
                return;
            }

            var progress = AppServices.Resolve<IGuideProgressService>();
            if (progress == null || progress.IsGroupCompleted(GuideGroupIds.FirstTalentDraw))
            {
                return;
            }

            // 不强制 FirstBattle 已完成：关 GameUI 时 Abort 不会记完成，否则回大厅永远起不来。
            _guide.StartGroup(GuideGroupIds.FirstTalentDraw);
        }

        private void RefreshEnemies()
        {
            var activeCount = 0;
            for (var i = 0; i < Session.Enemies.Length; i++)
            {
                if (Session.Enemies[i].ActiveInStage)
                {
                    activeCount++;
                }
            }

            var shown = new bool[ShowEnemy.Length];
            var placed = 0;
            for (var i = 0; i < Session.Enemies.Length; i++)
            {
                var enemy = Session.Enemies[i];
                if (!enemy.ActiveInStage)
                {
                    continue;
                }

                var slot = VisualSlot(placed, activeCount);
                placed++;
                if (slot < 0 || slot >= ShowEnemy.Length)
                {
                    continue;
                }

                shown[slot] = enemy.Alive;
                EnemyChips[slot].Value = $"勇气 {enemy.Courage}";
                EnemyBet[slot].Value = BetLabel(enemy);
                EnemyState[slot].Value = SeatLine(enemy);
            }

            for (var i = 0; i < ShowEnemy.Length; i++)
            {
                ShowEnemy[i].Value = shown[i];
                if (!shown[i])
                {
                    EnemyChips[i].Value = string.Empty;
                    EnemyBet[i].Value = string.Empty;
                    EnemyState[i].Value = string.Empty;
                }
            }

            // PVP 撞击期间强制露出目标槽，避免 ShowEnemy 关掉导致 AttackCutscene 拿不到 root。
            if (Session.IsPvp && Session.AttackPlaying)
            {
                var slot = Session.AttackVisualSlot;
                if (slot >= 0 && slot < ShowEnemy.Length)
                {
                    ShowEnemy[slot].Value = true;
                }
            }
        }

        private static string SeatLine(SeatState seat)
        {
            if (seat == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(seat.PeekedType))
            {
                return seat.Folded ? $"弃牌 {seat.PeekedType}" : $"透视 {seat.PeekedType}";
            }

            if (!string.IsNullOrEmpty(seat.Banner))
            {
                return seat.Banner;
            }

            if (seat.Folded)
            {
                return "弃牌";
            }

            return seat.Status ?? string.Empty;
        }

        private static string AmountLabel(string name, int amount)
        {
            return amount > 0 ? $"{name}{amount}" : name;
        }

        private static string BetLabel(SeatState seat)
        {
            if (seat == null)
            {
                return "下注(0)";
            }

            return $"下注({seat.StreetPaid})";
        }

        private static int VisualSlot(int enemyIndex, int activeCount)
        {
            return GameSession.TableVisualSlot(enemyIndex, activeCount);
        }

        private void RefreshCardInfo()
        {
            IsHandSettling = ShouldShowCardInfo(Session);
            var preview = ShouldShowPlayerHandPreview(Session);
            if ((!IsHandSettling && !preview) || Session.Player == null)
            {
                ShowCardInfo.Value = false;
                if (!IsHandSettling && !preview)
                {
                    CardTypeNum.Value = string.Empty;
                }

                return;
            }

            var score = Session.EvaluateSeat(Session.Player);
            ApplyCardType(score.Type, CardTypeIcon, CardTypeLabel, null);
            // 亮牌结算显示与伤害一致的总倍率；选牌预览仍只显示牌型基础倍率。
            CardTypeNum.Value = IsHandSettling
                ? FormatMultiplier(Session.ResolveAttackMagnification(Session.Player, score))
                : FormatMultiplier(GameSession.HandTypeMagnification(score.Type));
            ShowCardInfo.Value = true;
        }

        public static bool ShouldShowCardInfo(GameSession session)
        {
            return session != null &&
                   (session.SequentialCompare ||
                    session.Phase == GamePhase.Showdown ||
                    session.Phase == GamePhase.WaitingAttack ||
                    session.Phase == GamePhase.RoundSettle);
        }

        /// <summary>开牌前选满 3 张时预览玩家牌型；亮牌结算仍走 <see cref="ShouldShowCardInfo"/>。</summary>
        public static bool ShouldShowPlayerHandPreview(GameSession session)
        {
            if (session?.Player == null || ShouldShowCardInfo(session))
            {
                return false;
            }

            if (session.Phase != GamePhase.WaitingOpen && session.Phase != GamePhase.WaitingRub)
            {
                return false;
            }

            return !session.Player.Folded &&
                   session.Player.CountSelectedCards() == GameBalance.OpenHandSize;
        }

        public void ApplyCardType(
            HandType type,
            ObservableProperty<Sprite> icon,
            ObservableProperty<Sprite> label,
            ObservableProperty<string> num)
        {
            if (icon != null)
            {
                icon.Value = GetCardTypeSprite(IconSpriteName(type));
            }

            if (label != null)
            {
                label.Value = GetCardTypeSprite(LabelSpriteName(type));
            }

            if (num != null)
            {
                num.Value = FormatMultiplier(GameSession.HandTypeMagnification(type));
            }
        }

        public Sprite GetCardTypeIcon(HandType type) => GetCardTypeSprite(IconSpriteName(type));

        public Sprite GetCardTypeLabel(HandType type) => GetCardTypeSprite(LabelSpriteName(type));

        public static string FormatHandMultiplier(HandType type)
        {
            return FormatMultiplier(GameSession.HandTypeMagnification(type));
        }

        private Sprite GetCardTypeSprite(string spriteName)
        {
            if (Atlas == null || string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.CardTypeAtlas, spriteName, out var sprite);
            return sprite;
        }

        private static string IconSpriteName(HandType type)
        {
            switch (type)
            {
                case HandType.Pair: return "PairIcon";
                case HandType.Straight: return "StraightIcon";
                case HandType.Flush: return "SameSuitIcon";
                case HandType.StraightFlush: return "FlushIcon";
                case HandType.ThreeOfAKind: return "LeopardIcon";
                default: return "HighCardIcon";
            }
        }

        private static string LabelSpriteName(HandType type)
        {
            switch (type)
            {
                case HandType.Pair: return "Pair";
                case HandType.Straight: return "Straight";
                case HandType.Flush: return "SameSuit";
                case HandType.StraightFlush: return "Flush";
                case HandType.ThreeOfAKind: return "Leopard";
                default: return "HighCard";
            }
        }

        public static string FormatMultiplier(float value)
        {
            var rounded = (float)Math.Round(value, 2);
            if (Math.Abs(rounded - (float)Math.Round(rounded)) < 0.001f)
            {
                return $"x{(int)Math.Round(rounded)}";
            }

            return $"x{rounded:0.##}";
        }

        public static string FormatBonus(float value)
        {
            var rounded = (float)Math.Round(value, 2);
            if (Math.Abs(rounded - (float)Math.Round(rounded)) < 0.001f)
            {
                return $"+{(int)Math.Round(rounded)}";
            }

            return $"+{rounded:0.##}";
        }

        private static string FormatCharges(string name, int current, int max)
        {
            return $"{name}({Math.Max(0, current)}/{Math.Max(0, max)})";
        }

        private static string FormatEntryNames(List<CardShare.Contracts.Config.BossEntryConfig> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                parts[i] = entries[i] != null ? entries[i].Name : string.Empty;
            }

            return string.Join("、", parts);
        }

        private static string FormatEntryDescs(List<CardShare.Contracts.Config.BossEntryConfig> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                var desc = string.IsNullOrEmpty(entry.Desc) ? entry.Name : entry.Desc;
                if (!string.IsNullOrEmpty(desc))
                {
                    parts.Add(desc);
                }
            }

            return string.Join("\n", parts);
        }

        private static int SkillChargeMax(int baseline, float extra)
        {
            return Math.Max(0, baseline + (int)Math.Round(extra));
        }

        private async void TryPresentGamePopupInfo()
        {
            if (!IsOpen ||
                _infoPopupOpen ||
                _shopPopupOpen ||
                _resultPopupOpen ||
                HoldDealVisual ||
                !ShowTableButtons.Value ||
                _ui == null)
            {
                return;
            }

            if (Session.Phase == GamePhase.Shop ||
                Session.Phase == GamePhase.RunComplete ||
                Session.Phase == GamePhase.StageFail)
            {
                return;
            }

            var entries = BossMechanics.ResolveAll(Session.Run);
            if (entries.Count == 0)
            {
                return;
            }

            var key = BuildInfoKey(Session.Run);
            if (string.IsNullOrEmpty(key) || key == _shownInfoKey)
            {
                return;
            }

            _shownInfoKey = key;
            _infoPopupOpen = true;
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(GamePopupInfoViewModel));
                var popup = (GamePopupInfoViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(popup);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _infoPopupOpen = false;
            }
        }

        private static string BuildInfoKey(RunState run)
        {
            if (run?.LevelEntryIds == null || run.LevelEntryIds.Count == 0)
            {
                return string.Empty;
            }

            return run.Stage + ":" + string.Join(",", run.LevelEntryIds);
        }

        private async void OpenRemainList()
        {
            if (_ui == null || _remainListOpen)
            {
                return;
            }

            _remainListOpen = true;
            ShowYiwuBtn.Value = false;
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(RemainListPopViewModel));
                var popup = (RemainListPopViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(popup);
                await popup.Closed;
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _remainListOpen = false;
                ShowYiwuBtn.Value = true;
            }
        }
    }
}
