using System;
using App.Config;
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
}
