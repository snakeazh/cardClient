using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Game;
using App.Guide;
using App.Resources;
using Framework.Log;
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
        private bool _rubCompleting;
        private GuideTargetRegistry _guideTargets;
        private readonly List<Transform> _playerCardTransforms = new List<Transform>(GameBalance.MaxCardsPerSeat);
        private readonly List<string> _guideTargetIds = new List<string>(8);
        private HudBackgroundFit _hudBg;
        private Sprite _hudNormalBg;
        private Sprite _hudBossBg;
        private bool? _hudBgBoss;
        private int _hudBgSerial;

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
            if (AppServices.IsReady)
            {
                _guideTargets = AppServices.Resolve<GuideTargetRegistry>();
            }

            OnSessionChanged();
            RefreshGuideTargets();
        }

        public void CollectSelectedCards(SeatState seat, List<CardItem> dest)
        {
            _cards.CollectSelectedCards(_vm != null ? _vm.Session : null, seat, dest);
        }

        public void Detach()
        {
            _cards.DealFinished -= OnDealFinished;
            if (_vm != null)
            {
                _vm.Session.Changed -= OnSessionChanged;
            }

            CancelRubPreviewIfNeeded();
            UnregisterGuideTargets();
            // 卸载本局加载的 Boss 背景（与 LoadHudBossBackground 的 LoadAsync 计数对齐）
            if (_hudBossBg != null && _vm?.Resources != null)
            {
                _vm.Resources.Release(ResResourcePaths.GameHudBossBg);
                _hudBossBg = null;
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
                var xraySlot = _cards.HitEnemySlot(_camera);
                if (xraySlot >= 0)
                {
                    _vm.Session.TryXRayEnemySlot(xraySlot);
                    return;
                }
            }

            if (_vm.Session.SelectingRubTarget && Input.GetMouseButtonDown(0))
            {
                var pick = _cards.HitPlayerCard(_camera);
                if (pick >= 0)
                {
                    ApplyInstantRub(pick);
                    return;
                }
            }

            if (_vm.Session.Phase == GamePhase.WaitingOpen && Input.GetMouseButtonDown(0))
            {
                var pick = _cards.HitPlayerCard(_camera);
                if (pick >= 0)
                {
                    HandleOpenClick(pick);
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
            }
        }

        private void HandleOpenClick(int index)
        {
            if (index < 0 || _vm.Session.Phase != GamePhase.WaitingOpen)
            {
                return;
            }

            if (_vm.Session.Run.MagnifierThisRound && !_vm.Session.Run.PeekSuitUsed)
            {
                _vm.Session.PeekMagnifier(index);
                return;
            }

            _vm.Session.TogglePlayerCard(index);
        }

        private void ApplyInstantRub(int index)
        {
            if (_cards.IsRubPlaying || !_vm.Session.CanRubPlayerCard(index))
            {
                return;
            }

            _rubCompleting = true;
            _cards.PlayRubReplace(
                index,
                () => _vm != null && _vm.Session.TryRubPlayerCard(index),
                () =>
                {
                    _rubCompleting = false;
                    if (_vm != null)
                    {
                        _cards.Sync(_vm.Session);
                    }
                });
        }

        private void CancelRubPreviewIfNeeded()
        {
            if (_cards.IsRubPreviewActive)
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

            _ = ApplyHudBackgroundAsync();

            if (!_rubCompleting && !_cards.IsRubPlaying)
            {
                CancelRubPreviewIfNeeded();
            }

            if (_vm.ShouldHoldDealVisual())
            {
                BattleTrace.Log("Board.OnSessionChanged hold deal visual (no PlayDeal)");
                _cards.SyncHoldingDeal(_vm.Session);
                RefreshGuideTargets();
                return;
            }

            BattleTrace.Log("Board.OnSessionChanged Sync cards");
            _cards.Sync(_vm.Session);
            RefreshGuideTargets();
        }

        public void SyncCards()
        {
            if (_vm == null)
            {
                return;
            }

            BattleTrace.Log("Board.SyncCards");
            _cards.Sync(_vm.Session);
            RefreshGuideTargets();
        }

        private void OnDealFinished()
        {
            if (_vm != null)
            {
                _vm.NotifyDealReady();
            }

            RefreshGuideTargets();
        }

        private void RefreshGuideTargets()
        {
            if (_guideTargets == null)
            {
                return;
            }

            UnregisterGuideTargets();
            _playerCardTransforms.Clear();
            for (var i = 0; i < GameBalance.MaxCardsPerSeat; i++)
            {
                if (!_cards.TryGetPlayerCard(i, out var item) || item == null)
                {
                    continue;
                }

                var id = GuideTargetIds.PlayerCard(i);
                _guideTargets.RegisterWorld(id, item.transform);
                _guideTargetIds.Add(id);
                _playerCardTransforms.Add(item.transform);
            }

            if (_playerCardTransforms.Count > 0)
            {
                _guideTargets.RegisterWorldGroup(GuideTargetIds.PlayerHand, _playerCardTransforms);
                _guideTargetIds.Add(GuideTargetIds.PlayerHand);
            }
        }

        private void UnregisterGuideTargets()
        {
            if (_guideTargets != null && _guideTargetIds.Count > 0)
            {
                _guideTargets.UnregisterAll(_guideTargetIds);
            }

            _guideTargetIds.Clear();
        }

        private void BindScene()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                _camera = FindObjectOfType<Camera>();
            }

            _cards.Bind(transform, _vm.Resources);
            _hudBg = GetComponentInChildren<HudBackgroundFit>(true);
        }

        private async Task ApplyHudBackgroundAsync()
        {
            if (_hudBg == null || _vm?.Session == null)
            {
                return;
            }

            var isBoss = _vm.Session.Run.HasBoss;
            if (_hudBgBoss == isBoss)
            {
                return;
            }

            if (_hudBg.TryApplyTheme(isBoss))
            {
                _hudBgBoss = isBoss;
                return;
            }

            if (_hudNormalBg == null)
            {
                _hudNormalBg = _hudBg.CurrentSprite;
            }

            var serial = ++_hudBgSerial;
            var sprite = isBoss ? await LoadHudBossBackground() : _hudNormalBg;
            if (serial != _hudBgSerial || _hudBg == null || _vm == null || sprite == null)
            {
                return;
            }

            if (_vm.Session.Run.HasBoss != isBoss)
            {
                _hudBgBoss = null;
                await ApplyHudBackgroundAsync();
                return;
            }

            _hudBg.ApplySprite(sprite);
            _hudBgBoss = isBoss;
        }

        private async Task<Sprite> LoadHudBossBackground()
        {
            if (_hudBossBg != null)
            {
                return _hudBossBg;
            }

            if (_vm?.Resources == null)
            {
                return null;
            }

            try
            {
                _hudBossBg = await _vm.Resources.LoadAsync<Sprite>(ResResourcePaths.GameHudBossBg);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, $"Failed to load hud bg '{ResResourcePaths.GameHudBossBg}': {ex.Message}");
                _hudBossBg = null;
            }

            return _hudBossBg;
        }
    }
}
