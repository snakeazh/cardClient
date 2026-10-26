using System;
using App.Bootstrap;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;
using UnityEngine;
using UnityEngine;

namespace App.Guide
{
    public sealed class DealFinishedWaitHandler : IGuideWaitHandler
    {
        private readonly GameSession _session;
        private Action _onComplete;
        private bool _armed;

        public DealFinishedWaitHandler(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Id => GuideWaitIds.DealFinished;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            // WaitParam=Fresh：必须等下一次发牌完成（第 2 轮），不吃当前已完成的 DealSerial。
            var requireFresh = step != null &&
                               string.Equals(step.WaitParam?.Trim(), "Fresh", StringComparison.OrdinalIgnoreCase);
            if (!requireFresh &&
                _session.DealSerial > 0 &&
                GuideSignals.LastDealSerial == _session.DealSerial)
            {
                Complete();
            }
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.DealFinished)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    public sealed class SelectCardsWaitHandler : IGuideWaitHandler
    {
        private readonly GameSession _session;
        private Action _onComplete;
        private int _needed;
        private bool _armed;

        public SelectCardsWaitHandler(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Id => GuideWaitIds.SelectCards;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _needed = GameBalance.OpenHandSize;
            if (step != null && int.TryParse(step.WaitParam, out var parsed) && parsed > 0)
            {
                _needed = parsed;
            }

            _armed = true;
            _session.Changed += OnChanged;
            OnChanged();
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _session.Changed -= OnChanged;
            _onComplete = null;
        }

        private void OnChanged()
        {
            if (!_armed || _session.Player == null)
            {
                return;
            }

            if (_session.Player.CountSelectedCards() >= _needed)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    public sealed class GamePhaseWaitHandler : IGuideWaitHandler
    {
        private readonly GameSession _session;
        private Action _onComplete;
        private GamePhase[] _targets;
        private bool _armed;

        public GamePhaseWaitHandler(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Id => GuideWaitIds.GamePhase;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _targets = ParseTargets(step?.WaitParam);
            _armed = true;
            _session.Changed += OnChanged;
            OnChanged();
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _session.Changed -= OnChanged;
            _onComplete = null;
            _targets = null;
        }

        private void OnChanged()
        {
            if (!_armed || _targets == null)
            {
                return;
            }

            var phase = _session.Phase;
            for (var i = 0; i < _targets.Length; i++)
            {
                if (phase == _targets[i])
                {
                    Complete();
                    return;
                }
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }

        /// <summary>
        /// WaitParam 支持单个阶段，或逗号/竖线分隔的多阶段（如 RoundSettle,Shop）。
        /// 清场时 RoundSettle 不 Notify 就进 Shop，必须同时认 Shop。
        /// </summary>
        private static GamePhase[] ParseTargets(string waitParam)
        {
            if (string.IsNullOrWhiteSpace(waitParam))
            {
                return new[] { GamePhase.RoundSettle };
            }

            var parts = waitParam.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<GamePhase>(parts.Length);
            for (var i = 0; i < parts.Length; i++)
            {
                if (Enum.TryParse(parts[i].Trim(), true, out GamePhase parsed))
                {
                    list.Add(parsed);
                }
            }

            return list.Count > 0 ? list.ToArray() : new[] { GamePhase.RoundSettle };
        }
    }

    /// <summary>等待指定下标手牌被搓成目标点数（默认 Ace）。WaitParam：下标，或 "index:Ace"。</summary>
    public sealed class RubCardWaitHandler : IGuideWaitHandler
    {
        private readonly GameSession _session;
        private Action _onComplete;
        private int _index;
        private Rank _targetRank;
        private Rank _startRank;
        private bool _armed;

        public RubCardWaitHandler(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Id => GuideWaitIds.RubCard;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _index = GuideDealScript.RubTargetIndex;
            _targetRank = Rank.Ace;
            ParseParam(step != null ? step.WaitParam : null);

            var hand = _session.Player?.Hand;
            _startRank = hand != null && _index >= 0 && _index < hand.Length && hand[_index].IsValid
                ? hand[_index].Rank
                : Rank.Three;

            _armed = true;
            _session.Changed += OnChanged;
            OnChanged();
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _session.Changed -= OnChanged;
            _onComplete = null;
        }

        private void ParseParam(string param)
        {
            if (string.IsNullOrWhiteSpace(param))
            {
                return;
            }

            var text = param.Trim();
            var colon = text.IndexOf(':');
            if (colon >= 0)
            {
                var left = text.Substring(0, colon).Trim();
                var right = text.Substring(colon + 1).Trim();
                if (int.TryParse(left, out var idx) && idx >= 0)
                {
                    _index = idx;
                }

                if (Enum.TryParse(right, true, out Rank rank))
                {
                    _targetRank = rank;
                }

                return;
            }

            if (int.TryParse(text, out var onlyIndex) && onlyIndex >= 0)
            {
                _index = onlyIndex;
            }
        }

        private void OnChanged()
        {
            if (!_armed || _session.Player?.Hand == null)
            {
                return;
            }

            var hand = _session.Player.Hand;
            if (_index < 0 || _index >= hand.Length || !hand[_index].IsValid)
            {
                return;
            }

            var card = hand[_index];
            if (card.Rank == _targetRank && card.Rank != _startRank)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待点选引导指定下标手牌。WaitParam：逗号分隔下标，默认 RevealTargetIndices。</summary>
    public sealed class SelectGuideCardsWaitHandler : IGuideWaitHandler
    {
        private readonly GameSession _session;
        private Action _onComplete;
        private int[] _indices;
        private bool _armed;

        public SelectGuideCardsWaitHandler(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Id => GuideWaitIds.SelectGuideCards;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _indices = ParseIndices(step != null ? step.WaitParam : null);
            _armed = true;
            _session.Changed += OnChanged;
            OnChanged();
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _session.Changed -= OnChanged;
            _onComplete = null;
        }

        private void OnChanged()
        {
            if (!_armed || _session.Player == null || _indices == null || _indices.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _indices.Length; i++)
            {
                var index = _indices[i];
                if (!_session.Player.IsCardSelected(index))
                {
                    return;
                }
            }

            if (_session.Player.CountSelectedCards() != _indices.Length)
            {
                return;
            }

            Complete();
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }

        private static int[] ParseIndices(string param)
        {
            if (string.IsNullOrWhiteSpace(param))
            {
                return GuideDealScript.RevealTargetIndices;
            }

            var parts = param.Split(',');
            var list = new int[parts.Length];
            var count = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i].Trim(), out var idx) && idx >= 0)
                {
                    list[count++] = idx;
                }
            }

            if (count == 0)
            {
                return GuideDealScript.RevealTargetIndices;
            }

            if (count == list.Length)
            {
                return list;
            }

            var trimmed = new int[count];
            Array.Copy(list, trimmed, count);
            return trimmed;
        }
    }

    /// <summary>等待玩家点选满开牌张数且牌型匹配。WaitParam：HandType 名，如 ThreeOfAKind。</summary>
    public sealed class SelectHandTypeWaitHandler : IGuideWaitHandler
    {
        private readonly GameSession _session;
        private Action _onComplete;
        private App.Game.HandType _target;
        private bool _armed;

        public SelectHandTypeWaitHandler(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Id => GuideWaitIds.SelectHandType;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _target = App.Game.HandType.ThreeOfAKind;
            if (step != null && !string.IsNullOrWhiteSpace(step.WaitParam) &&
                Enum.TryParse(step.WaitParam.Trim(), true, out App.Game.HandType parsed))
            {
                _target = parsed;
            }

            _armed = true;
            _session.Changed += OnChanged;
            OnChanged();
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _session.Changed -= OnChanged;
            _onComplete = null;
        }

        private void OnChanged()
        {
            if (!_armed || _session.Player == null)
            {
                return;
            }

            if (_session.Player.CountSelectedCards() < GameBalance.OpenHandSize)
            {
                return;
            }

            var cards = HandEvaluator.CopySelectedCards(
                _session.Player.Hand,
                _session.Player.CardSelected);
            if (cards == null || cards.Length < GameBalance.OpenHandSize)
            {
                return;
            }

            var score = HandEvaluator.Evaluate(cards);
            if (score.Type == _target)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待搓牌技能详情 tip 弹出。</summary>
    public sealed class PeekGoodTipShownWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;

        public string Id => GuideWaitIds.PeekGoodTipShown;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            if (GuideSignals.PeekGoodTipVisible)
            {
                Complete();
            }
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.PeekGoodTipShown)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待搓牌技能详情 tip 关闭；进入时若已关则立刻完成。</summary>
    public sealed class PeekGoodTipClosedWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;

        public string Id => GuideWaitIds.PeekGoodTipClosed;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            if (!GuideSignals.PeekGoodTipVisible)
            {
                Complete();
            }
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.PeekGoodTipClosed)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待天赋弹窗打开。</summary>
    public sealed class TalentPopupOpenWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;

        public string Id => GuideWaitIds.TalentPopupOpen;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            if (GuideSignals.TalentPopupOpened)
            {
                Complete();
            }
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.TalentPopupOpen)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待成功抽取天赋并打开详情。</summary>
    public sealed class TalentDrawnWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;

        public string Id => GuideWaitIds.TalentDrawn;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            if (GuideSignals.TalentDrawn)
            {
                Complete();
            }
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.TalentDrawn)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待点击商店货架圣物（打开购买详情）。</summary>
    public sealed class ShopOfferClickedWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;

        public string Id => GuideWaitIds.ShopOfferClicked;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            if (GuideSignals.ShopOfferClicked)
            {
                Complete();
            }
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.ShopOfferClicked)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待商店购买成功（详情关闭后）。</summary>
    public sealed class ShopBoughtWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;

        public string Id => GuideWaitIds.ShopBought;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            if (GuideSignals.ShopBought)
            {
                Complete();
            }
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.ShopBought)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>等待点击已购栏圣物；无已购目标（如买即用）约 1 秒后跳过。</summary>
    public sealed class ShopMineClickedWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;
        private int _token;

        public string Id => GuideWaitIds.ShopMineClicked;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            GuideSignals.Raised += OnRaised;
            if (GuideSignals.ShopMineClicked)
            {
                Complete();
                return;
            }

            var token = ++_token;
            _ = SkipIfNoMineTargetAsync(token);
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _token++;
            GuideSignals.Raised -= OnRaised;
            _onComplete = null;
        }

        private void OnRaised(string id)
        {
            if (id == GuideWaitIds.ShopMineClicked)
            {
                Complete();
            }
        }

        private async System.Threading.Tasks.Task SkipIfNoMineTargetAsync(int token)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(1000);
            }
            catch
            {
                return;
            }

            if (!_armed || token != _token)
            {
                return;
            }

            if (!AppServices.IsReady)
            {
                return;
            }

            var targets = AppServices.Resolve<GuideTargetRegistry>();
            if (targets != null && targets.HasTarget(GuideTargetIds.ShopBoughtMine))
            {
                return;
            }

            // 消耗品买即用等没有已购格：结束商店引导，不再走售出说明。
            GuideSignals.NotifyShopMineUnavailable();
        }

        private void Complete()
        {
            if (!_armed)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }

    /// <summary>WaitParam 秒数后自动推进（售出说明等无需点击的提示）。</summary>
    public sealed class DelayWaitHandler : IGuideWaitHandler
    {
        private Action _onComplete;
        private bool _armed;
        private int _token;

        public string Id => GuideWaitIds.Delay;

        public void Start(GuideStepConfig step, Action onComplete)
        {
            Stop();
            _onComplete = onComplete;
            _armed = true;
            var seconds = 2.5f;
            if (step != null &&
                !string.IsNullOrWhiteSpace(step.WaitParam) &&
                float.TryParse(step.WaitParam.Trim(), out var parsed) &&
                parsed > 0f)
            {
                seconds = parsed;
            }

            var targetId = step != null ? step.TargetId : null;
            var token = ++_token;
            _ = RunAsync(seconds, targetId, token);
        }

        public void Stop()
        {
            if (!_armed)
            {
                return;
            }

            _armed = false;
            _token++;
            _onComplete = null;
        }

        private async System.Threading.Tasks.Task RunAsync(float seconds, string targetId, int token)
        {
            try
            {
                // 点已购后详情异步打开，SellBtn 可能晚一帧才登记：先等目标出现再计时。
                if (!string.IsNullOrEmpty(targetId))
                {
                    var waitUntil = Time.realtimeSinceStartup + 1.5f;
                    while (_armed && token == _token && Time.realtimeSinceStartup < waitUntil)
                    {
                        if (AppServices.IsReady)
                        {
                            var targets = AppServices.Resolve<GuideTargetRegistry>();
                            if (targets != null && targets.HasTarget(targetId))
                            {
                                break;
                            }
                        }

                        await System.Threading.Tasks.Task.Delay(50);
                    }

                    if (!_armed || token != _token)
                    {
                        return;
                    }

                    // 目标始终没有（异常路径）：短提示后结束，避免空遮罩挂死。
                    if (AppServices.IsReady)
                    {
                        var targets = AppServices.Resolve<GuideTargetRegistry>();
                        if (targets == null || !targets.HasTarget(targetId))
                        {
                            seconds = 0.4f;
                        }
                    }
                }

                await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(seconds));
            }
            catch
            {
                return;
            }

            if (!_armed || token != _token)
            {
                return;
            }

            var done = _onComplete;
            Stop();
            done?.Invoke();
        }
    }
}
