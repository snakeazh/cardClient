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
        private int _dragCard = -1;
        private float _rubAcc;
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
            OnSessionChanged();
        }

        public void Detach()
        {
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

            if (_vm.Session.Phase == GamePhase.Betting &&
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
                    _dragCard = index;
                    _rubAcc = 0f;
                    _vm.Session.SelectRubCard(index);
                }
            }

            if (_dragCard >= 0 && Input.GetMouseButton(0))
            {
                _rubAcc += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")).magnitude;
                var glow = Mathf.PingPong(Time.time * 6f, 1f);
                _cards.TintPlayerCard(_dragCard, Color.Lerp(Color.white, new Color(1f, 0.85f, 0.4f), glow));
                if (_rubAcc > 2.2f)
                {
                    var index = _dragCard;
                    _dragCard = -1;
                    _vm.Session.RubCard(index);
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                _dragCard = -1;
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

        private void BindScene()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                _camera = FindObjectOfType<Camera>();
            }

            _cards.Bind(transform);
        }
    }
}
