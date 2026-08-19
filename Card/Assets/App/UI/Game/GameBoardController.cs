using App.Game;
using UnityEngine;
using UnityEngine.EventSystems;

namespace App.UI
{
    /// <summary>
    /// 牌桌世界表现（GameHud 发牌/翻牌、搓牌/放大镜输入）。
    /// Canvas HUD 在 <see cref="GameUIView"/>。
    /// </summary>
    public sealed class GameBoardController : MonoBehaviour
    {
        private GameTableViewModel _vm;
        private readonly CardTableAnimator _cards = new CardTableAnimator();
        private Camera _camera;
        private bool _bound;

        public void Attach(GameTableViewModel viewModel)
        {
            if (_vm != null)
            {
                Detach();
            }

            _vm = viewModel;
            enabled = true;
            if (!_bound)
            {
                BindScene();
                _bound = true;
            }

            _vm.Session.Changed += OnSessionChanged;
            _cards.DealFinished += OnDealFinished;
            OnSessionChanged();
        }

        public void Detach()
        {
            _cards.DealFinished -= OnDealFinished;
            if (_vm != null)
            {
                _vm.Session.Changed -= OnSessionChanged;
            }

            _vm = null;
            enabled = false;
        }

        private void Update()
        {
            if (_vm == null || _cards.IsBusy)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (_vm.Session.SelectingXRayTarget && Input.GetMouseButtonDown(0))
            {
                if (_cards.HitPlayerCard(_camera) >= 0)
                {
                    _vm.Session.TryXRayPlayer();
                    return;
                }

                var xraySlot = _cards.HitEnemySlot(_camera);
                if (xraySlot >= 0)
                {
                    _vm.Session.TryXRayEnemySlot(xraySlot);
                    return;
                }
            }

            if (_vm.Session.Phase == GamePhase.WaitingOpen &&
                _vm.Session.Run.MagnifierThisRound &&
                !_vm.Session.Run.PeekSuitUsed &&
                Input.GetMouseButtonDown(0))
            {
                var peek = _cards.HitPlayerCard(_camera);
                if (peek >= 0)
                {
                    _vm.Session.PeekMagnifier(peek);
                    return;
                }
            }

            if (_vm.Session.Phase == GamePhase.WaitingAttack && Input.GetMouseButtonDown(0))
            {
                var slot = _cards.HitEnemySlot(_camera);
                if (slot >= 0)
                {
                    _vm.Session.AttackEnemyAtSlot(slot);
                }

                return;
            }

            if (_vm.Session.SelectingOpenTarget && Input.GetMouseButtonDown(0))
            {
                var slot = _cards.HitEnemySlot(_camera);
                if (slot >= 0)
                {
                    _vm.Session.AttackEnemyAtSlot(slot);
                }

                return;
            }

            if (_vm.Session.Phase != GamePhase.WaitingRub)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                var index = _cards.HitPlayerCard(_camera);
                if (index >= 0)
                {
                    _vm.Session.RubCard(index);
                }
            }
        }

        private void OnDestroy()
        {
            Detach();
            _cards.Dispose();
        }

        private void OnSessionChanged()
        {
            if (_vm == null)
            {
                return;
            }

            _cards.Sync(_vm.Session);
        }

        private void OnDealFinished()
        {
            if (_vm != null)
            {
                _vm.NotifyDealReady();
            }
        }

        private void BindScene()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                _camera = FindObjectOfType<Camera>();
            }

            _cards.Bind(transform, _vm.Resources);
        }
    }
}
