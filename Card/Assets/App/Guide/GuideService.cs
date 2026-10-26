using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;
using App.Net;
using App.Talent;
using App.UI;
using App.Wallet;
using Framework.Log;
using Framework.UI;
using Framework.UI.Navigation;
using UnityEngine;
using UnityEngine.UI;

namespace App.Guide
{
    public sealed class GuideService : IGuideService
    {
        private readonly IUIManager _ui;
        private readonly IGuideProgressService _progress;
        private readonly GameSession _session;
        private readonly GuideTargetRegistry _targets;
        private readonly Dictionary<string, IGuideWaitHandler> _handlers;
        private readonly List<GuideStepConfig> _steps = new List<GuideStepConfig>(8);

        private GuideOverlayViewModel _overlay;
        private GuideStepConfig _step;
        private Button _clickButton;
        private Toggle _clickToggle;
        private bool _advancing;
        private int _stepIndex = -1;

        public GuideService(
            IUIManager ui,
            IGuideProgressService progress,
            GameSession session,
            GuideTargetRegistry targets)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _handlers = new Dictionary<string, IGuideWaitHandler>(StringComparer.OrdinalIgnoreCase)
            {
                [GuideWaitIds.DealFinished] = new DealFinishedWaitHandler(session),
                [GuideWaitIds.SelectCards] = new SelectCardsWaitHandler(session),
                [GuideWaitIds.SelectGuideCards] = new SelectGuideCardsWaitHandler(session),
                [GuideWaitIds.GamePhase] = new GamePhaseWaitHandler(session),
                [GuideWaitIds.RubCard] = new RubCardWaitHandler(session),
                [GuideWaitIds.SelectHandType] = new SelectHandTypeWaitHandler(session),
                [GuideWaitIds.PeekGoodTipShown] = new PeekGoodTipShownWaitHandler(),
                [GuideWaitIds.PeekGoodTipClosed] = new PeekGoodTipClosedWaitHandler(),
                [GuideWaitIds.TalentPopupOpen] = new TalentPopupOpenWaitHandler(),
                [GuideWaitIds.TalentDrawn] = new TalentDrawnWaitHandler(),
                [GuideWaitIds.ShopOfferClicked] = new ShopOfferClickedWaitHandler(),
                [GuideWaitIds.ShopBought] = new ShopBoughtWaitHandler(),
                [GuideWaitIds.ShopMineClicked] = new ShopMineClickedWaitHandler(),
                [GuideWaitIds.Delay] = new DelayWaitHandler()
            };

            _session.Changed += OnSessionChanged;
            _targets.Changed += OnTargetsChanged;
            GuideSignals.Raised += OnGuideSignal;
        }

        public bool IsRunning { get; private set; }

        public int CurrentGroupId { get; private set; }

        public GuideStepConfig CurrentStep => _step;

        public GuideTargetRegistry Targets => _targets;

        public void TryStart(GuideTriggerType type, string param)
        {
            if (IsRunning)
            {
                return;
            }

            GuideGroupConfig match = null;
            foreach (var kv in GuideGroupConfig.All)
            {
                var group = kv.Value;
                if (group == null || !group.Enabled || group.TriggerType != type)
                {
                    continue;
                }

                if (_progress.IsGroupCompleted(group.Id))
                {
                    continue;
                }

                if (!ParamEquals(group.TriggerParam, param))
                {
                    continue;
                }

                if (match == null || group.Id < match.Id)
                {
                    match = group;
                }
            }

            if (match != null)
            {
                StartGroup(match.Id);
            }
        }

        public void StartGroup(int groupId)
        {
            StartGroup(groupId, 0);
        }

        public void StartGroup(int groupId, int fromOrder)
        {
            if (IsRunning)
            {
                return;
            }

            var group = GuideGroupConfig.Get(groupId);
            if (group == null || !group.Enabled)
            {
                AppLog.Warn(LogChannel.UI, $"[Guide] unknown or disabled group {groupId}");
                return;
            }

            if (_progress.IsGroupCompleted(groupId))
            {
                return;
            }

            CollectSteps(groupId);
            if (_steps.Count == 0)
            {
                AppLog.Warn(LogChannel.UI, $"[Guide] group {groupId} has no steps");
                return;
            }

            var startIndex = 0;
            if (fromOrder > 0)
            {
                for (var i = 0; i < _steps.Count; i++)
                {
                    if (_steps[i].Order >= fromOrder)
                    {
                        startIndex = i;
                        break;
                    }
                }
            }

            IsRunning = true;
            CurrentGroupId = groupId;
            _stepIndex = startIndex - 1;
            GuideSignals.ResetTalentDrawn();
            GuideSignals.ResetShopOfferClicked();
            GuideSignals.ResetShopBought();
            GuideSignals.ResetShopMineClicked();
            AppLog.Info(LogChannel.UI, $"[Guide] start group {group.Name} ({groupId}) fromOrder={fromOrder}");
            _ = RunGroupAsync(group);
        }

        public void Advance()
        {
            if (!IsRunning || _advancing)
            {
                return;
            }

            _ = AdvanceAsync();
        }

        /// <summary>
        /// 蒙版洞点击。优先触发目标 Button/Toggle，避免洞点不穿到下层 UI。
        /// </summary>
        public void InvokeClickTarget()
        {
            if (!IsRunning || _advancing || _step == null || _step.StepType != GuideStepType.Click)
            {
                return;
            }

            if (_clickButton != null)
            {
                _clickButton.onClick.Invoke();
                return;
            }

            if (_clickToggle != null)
            {
                if (!_clickToggle.isOn)
                {
                    _clickToggle.isOn = true;
                }
                else
                {
                    Advance();
                }

                return;
            }

            Advance();
        }

        public void Skip()
        {
            if (!IsRunning || _advancing)
            {
                return;
            }

            var group = GuideGroupConfig.Get(CurrentGroupId);
            if (group != null && !group.Skipable)
            {
                return;
            }

            AppLog.Info(LogChannel.UI, $"[Guide] skip group {CurrentGroupId}");
            _ = FinishAsync(completed: true);
        }

        public void Abort()
        {
            if (!IsRunning)
            {
                return;
            }

            AppLog.Info(LogChannel.UI, $"[Guide] abort group {CurrentGroupId}");
            _ = FinishAsync(completed: false);
        }

        private async Task RunGroupAsync(GuideGroupConfig group)
        {
            try
            {
                _overlay = new GuideOverlayViewModel(this);
                await _ui.Open(_overlay);
                _overlay.ShowSkip.Value = group.Skipable;
                await AdvanceAsync();
            }
            catch (Exception e)
            {
                AppLog.Exception(LogChannel.UI, e);
                await FinishAsync(completed: false);
            }
        }

        private async Task AdvanceAsync()
        {
            if (!IsRunning || _advancing)
            {
                return;
            }

            _advancing = true;
            try
            {
                ClearStepRuntime();
                _stepIndex++;
                if (_stepIndex >= _steps.Count)
                {
                    await FinishAsync(completed: true);
                    return;
                }

                _step = _steps[_stepIndex];
                AppLog.Info(LogChannel.UI, $"[Guide] step {_step.Id} type={_step.StepType} target={_step.TargetId}");
                ApplyOverlay(_step);
            }
            finally
            {
                _advancing = false;
            }

            if (IsRunning && _step != null)
            {
                BindStepRuntime(_step);
            }
        }

        private async Task FinishAsync(bool completed)
        {
            if (!IsRunning)
            {
                return;
            }

            var groupId = CurrentGroupId;
            ClearStepRuntime();
            _stepIndex = -1;
            _step = null;
            _steps.Clear();

            _session.ClearGuideDealLocks();
            GuideSignals.NotifyGuideEnded();

            // 先关 Overlay，再清 IsRunning：避免商店等接档引导在旧层未关时抢开。
            var overlay = _overlay;
            _overlay = null;
            if (overlay != null && _ui.HasScreen(UILayer.Guide))
            {
                try
                {
                    await _ui.Close(overlay);
                }
                catch (Exception e)
                {
                    AppLog.Exception(LogChannel.UI, e);
                }
            }

            IsRunning = false;
            CurrentGroupId = 0;

            if (!completed || groupId <= 0)
            {
                return;
            }

            // 不通信 / 请求失败时也必须落本地，否则 FirstBattle 强制牌型会一直生效、引导会反复弹出。
            await SyncGuideCompletedAsync(groupId);
        }

        private async Task SyncGuideCompletedAsync(int groupId)
        {
            if (GameApi.IsReady)
            {
                try
                {
                    var profile = await GameApi.Client.CompleteGuideAsync(groupId);
                    GameApi.ApplyProfile(profile);
                }
                catch (GameApiException ex)
                {
                    AppLog.Warn(LogChannel.UI, $"[Guide] CompleteGuide group={groupId} failed: {ex.Message}");
                    Toast.Error(GameApi.Describe(ex));
                }
            }

            if (!_progress.IsGroupCompleted(groupId))
            {
                _progress.MarkGroupCompleted(groupId);
            }
        }

        private void ApplyOverlay(GuideStepConfig step)
        {
            if (_overlay == null)
            {
                return;
            }

            _overlay.Apply(step);
        }

        private void BindStepRuntime(GuideStepConfig step)
        {
            if (step.StepType == GuideStepType.Wait)
            {
                var key = step.WaitHandler ?? string.Empty;
                if (_handlers.TryGetValue(key, out var handler))
                {
                    handler.Start(step, Advance);
                }
                else
                {
                    AppLog.Warn(LogChannel.UI, $"[Guide] missing WaitHandler '{key}', auto-advance");
                    Advance();
                }

                return;
            }

            if (step.StepType == GuideStepType.Click)
            {
                if (string.Equals(step.TargetId, GuideTargetIds.TalentBuyBtn, StringComparison.Ordinal))
                {
                    EnsureTalentDrawGold();
                }

                TryBindClickTarget();
            }
        }

        private void TryBindClickTarget()
        {
            if (_step == null || _step.StepType != GuideStepType.Click)
            {
                return;
            }

            UnbindClickTarget();
            var ui = _targets.GetUi(_step.TargetId);
            if (ui == null)
            {
                return;
            }

            var button = ui.GetComponent<Button>();
            if (button == null)
            {
                button = ui.GetComponentInChildren<Button>(true);
            }

            if (button != null)
            {
                _clickButton = button;
                _clickButton.onClick.AddListener(OnClickTarget);
                if (_overlay != null)
                {
                    _overlay.ClickUsesButton.Value = true;
                }

                return;
            }

            var toggle = ui.GetComponent<Toggle>();
            if (toggle == null)
            {
                toggle = ui.GetComponentInChildren<Toggle>(true);
            }

            if (toggle == null)
            {
                return;
            }

            _clickToggle = toggle;
            _clickToggle.onValueChanged.AddListener(OnToggleTarget);
            if (_overlay != null)
            {
                _overlay.ClickUsesButton.Value = true;
            }
        }

        private void OnClickTarget()
        {
            Advance();
        }

        private void OnToggleTarget(bool isOn)
        {
            if (isOn)
            {
                Advance();
            }
        }

        private void UnbindClickTarget()
        {
            if (_clickButton != null)
            {
                _clickButton.onClick.RemoveListener(OnClickTarget);
                _clickButton = null;
            }

            if (_clickToggle != null)
            {
                _clickToggle.onValueChanged.RemoveListener(OnToggleTarget);
                _clickToggle = null;
            }

            if (_overlay != null)
            {
                _overlay.ClickUsesButton.Value = false;
            }
        }

        private void ClearStepRuntime()
        {
            UnbindClickTarget();
            foreach (var kv in _handlers)
            {
                kv.Value.Stop();
            }
        }

        private void CollectSteps(int groupId)
        {
            _steps.Clear();
            foreach (var kv in GuideStepConfig.All)
            {
                var step = kv.Value;
                if (step != null && step.GroupId == groupId)
                {
                    _steps.Add(step);
                }
            }

            _steps.Sort((a, b) =>
            {
                var order = a.Order.CompareTo(b.Order);
                return order != 0 ? order : a.Id.CompareTo(b.Id);
            });
        }

        private void OnSessionChanged()
        {
            if (!IsRunning)
            {
                TryStart(GuideTriggerType.GamePhase, _session.Phase.ToString());
            }
        }

        /// <summary>
        /// FirstBattle 只覆盖首局选牌；清场进商店后 FirstBattle 结束。
        /// 下一局/下一关发牌完成后启动 FirstSkill（搓牌引导）。
        /// 商店购买详情未买就关掉时，回到「点货架」步。
        /// </summary>
        private void OnGuideSignal(string id)
        {
            if (id == GuideWaitIds.ShopBuyAbandoned &&
                IsRunning &&
                CurrentGroupId == GuideGroupIds.FirstShop &&
                _step != null &&
                string.Equals(_step.WaitHandler, GuideWaitIds.ShopBought, StringComparison.OrdinalIgnoreCase))
            {
                GuideSignals.ResetShopOfferClicked();
                _ = RestartCurrentGroupFromOrderAsync(1);
                return;
            }

            if (id == GuideWaitIds.ShopMineUnavailable &&
                IsRunning &&
                CurrentGroupId == GuideGroupIds.FirstShop)
            {
                _ = FinishAsync(completed: true);
                return;
            }

            if (id != GuideWaitIds.DealFinished || IsRunning || _session.IsPvp)
            {
                return;
            }

            if (!_progress.IsGroupCompleted(GuideGroupIds.FirstBattle) ||
                _progress.IsGroupCompleted(GuideGroupIds.FirstSkill))
            {
                return;
            }

            StartGroup(GuideGroupIds.FirstSkill);
        }

        private async Task RestartCurrentGroupFromOrderAsync(int fromOrder)
        {
            if (!IsRunning || _advancing)
            {
                return;
            }

            var groupId = CurrentGroupId;
            ClearStepRuntime();
            CollectSteps(groupId);
            var startIndex = 0;
            if (fromOrder > 0)
            {
                for (var i = 0; i < _steps.Count; i++)
                {
                    if (_steps[i].Order >= fromOrder)
                    {
                        startIndex = i;
                        break;
                    }
                }
            }

            _stepIndex = startIndex - 1;
            await AdvanceAsync();
        }

        private void OnTargetsChanged()
        {
            if (IsRunning &&
                _step != null &&
                _step.StepType == GuideStepType.Click &&
                _clickButton == null &&
                _clickToggle == null)
            {
                TryBindClickTarget();
            }
        }

        private static async void EnsureTalentDrawGold()
        {
            if (!AppServices.IsReady)
            {
                return;
            }

            var wallet = AppServices.Resolve<IWalletService>();
            var talent = AppServices.Resolve<ITalentService>();
            if (wallet == null || talent == null)
            {
                return;
            }

            var cost = talent.GetDrawCost();
            if (cost <= 0 || wallet.Gold >= cost)
            {
                return;
            }

            if (!GameApi.IsReady)
            {
                return;
            }

            try
            {
                var profile = await GameApi.Client.DebugGrantGoldAsync(cost - wallet.Gold);
                GameApi.ApplyProfile(profile);
                AppLog.Info(LogChannel.UI, $"[Guide] granted gold for talent draw, now {wallet.Gold}");
            }
            catch (GameApiException ex)
            {
                Toast.Error(GameApi.Describe(ex));
            }
        }

        private static bool ParamEquals(string configured, string actual)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return true;
            }

            return string.Equals(configured.Trim(), actual ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
