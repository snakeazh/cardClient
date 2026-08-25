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
        private int _rubPreviewIndex = -1;
        private bool _rubHolding;
        private bool _rubGrabArmed;
        private Vector3 _lastMouse;
        private bool _rubCompleting;

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

            ClearRubDrag(cancelPreview: true);
            _vm = null;
            enabled = false;
        }

        private void Update()
        {
            if (_vm == null || _cards.IsBusy)
            {
                return;
            }

            // 搓牌按住拖拽时即使滑到 UI 上也继续累计。
            if (_vm.Session.Phase == GamePhase.WaitingRub && _rubHolding)
            {
                UpdateRubHold();
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

            if (_vm.Session.Phase == GamePhase.WaitingOpen && Input.GetMouseButtonDown(0))
            {
                var pick = _cards.HitPlayerCard(_camera);
                if (pick >= 0)
                {
                    if (_vm.Session.Run.MagnifierThisRound && !_vm.Session.Run.PeekSuitUsed)
                    {
                        _vm.Session.PeekMagnifier(pick);
                    }
                    else
                    {
                        _vm.Session.TogglePlayerCard(pick);
                    }

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
                    BeginRubSelect(index);
                }
            }
        }

        private void BeginRubSelect(int index)
        {
            // 先锁面再 Notify，避免 Sync 按 Looked 把牌翻回正面。
            if (_rubPreviewIndex != index)
            {
                if (_rubPreviewIndex >= 0)
                {
                    _cards.CancelRubPreview();
                }

                _cards.BeginRubPreview(index);
                _vm.Session.SelectRubCard(index);
                _rubPreviewIndex = index;
            }

            _rubHolding = true;
            _rubGrabArmed = false;
            _lastMouse = Input.mousePosition;
        }

        private void UpdateRubHold()
        {
            if (!_rubHolding)
            {
                return;
            }

            if (Input.GetMouseButton(0))
            {
                var mouse = Input.mousePosition;
                if (_cards.IsRubShakeReady)
                {
                    if (!_rubGrabArmed)
                    {
                        _cards.BeginRubDrag(_camera, mouse);
                        _rubGrabArmed = true;
                    }

                    _cards.DragRubCard(_camera, mouse);
                }

                _lastMouse = mouse;
                return;
            }

            FinishRubHold();
        }

        private void FinishRubHold()
        {
            if (!_rubHolding)
            {
                return;
            }

            _rubHolding = false;
            var index = _rubPreviewIndex;
            if (index < 0)
            {
                return;
            }

            var enoughOffset = _cards.RubPeakOffset >= CardTableAnimator.MinRubOffset;
            var enoughTime = _cards.RubDragElapsed >= CardTableAnimator.MinRubDuration;
            var enough = _cards.IsRubShakeReady && enoughOffset && enoughTime;
            if (!enough)
            {
                _cards.ResetRubShake();
                _rubGrabArmed = false;
                if (_cards.IsRubShakeReady)
                {
                    if (!enoughOffset)
                    {
                        _vm.Session.NotifyRubTooWeak();
                    }
                    else
                    {
                        _vm.Session.NotifyRubTooShort();
                    }
                }

                return;
            }

            _rubCompleting = true;
            try
            {
                _vm.Session.RubCard(index);
                if (_cards.IsRubPreviewActive)
                {
                    _cards.CompleteRubFlip();
                }

                _rubPreviewIndex = -1;
                _rubGrabArmed = false;
            }
            finally
            {
                _rubCompleting = false;
            }
        }

        private void ClearRubDrag(bool cancelPreview)
        {
            _rubHolding = false;
            _rubGrabArmed = false;
            _rubPreviewIndex = -1;
            if (cancelPreview && _cards.IsRubPreviewActive)
            {
                _cards.CancelRubPreview();
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

            if (_vm.Session.Phase != GamePhase.WaitingRub && !_rubCompleting)
            {
                ClearRubDrag(cancelPreview: true);
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
