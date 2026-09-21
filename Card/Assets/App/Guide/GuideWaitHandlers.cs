using System;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;

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
            if (_session.DealSerial > 0 && GuideSignals.LastDealSerial == _session.DealSerial)
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
        private GamePhase _target;
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
            _target = GamePhase.RoundSettle;
            if (step != null && !string.IsNullOrWhiteSpace(step.WaitParam) &&
                Enum.TryParse(step.WaitParam.Trim(), true, out GamePhase parsed))
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
            if (_armed && _session.Phase == _target)
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
}
