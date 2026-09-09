using System;
using System.Collections.Generic;
using App.Config;
using App.Game;
using App.Resources;
using DG.Tweening;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 从 GameHud.dealpoint 发牌到 mineNode / otherNode。
    /// otherNode 只展示 <see cref="GameSession.DisplayedEnemy"/> 的 5 张牌。
    /// </summary>
    public sealed class CardTableAnimator
    {
        private const int CardsPerHand = GameBalance.MaxCardsPerSeat;
        private const float DealMoveDuration = 0.32f;
        private const float DealStagger = 0.08f;
        private const float DealFlipPause = 0.12f;
        private const float ShuffleStagger = 0.015f;
        private const float ShuffleAppear02 = 0.8f;
        private const float FlipDuration = 0.35f;
        private const float RevealFlipDuration = 0.28f;
        private const float RevealCardGap = 0.12f;
        private const float RevealSeatGap = 0.38f;
        private const float SettleClipDuration = 0.3f;
        private const float SelectLift = 0.28f;
        private const float SelectLiftDuration = 0.12f;
        private const float RubReplaceRevealDelay = 1.5f;

        private IResourceService _resources;
        private GameObject _prefab;
        private readonly CardShadowPool _shadows = new CardShadowPool();

        private Transform _hud;
        private Transform _dealPoint;
        private CardDealPoint _dealPile;
        private SeatView _player;
        private SeatView _other;
        private Sequence _dealSeq;
        private Sequence _revealSeq;
        private Sequence _rubSeq;
        private int _shownDeal = -1;
        private int _shownEnemyId = int.MinValue;
        private int _shownReveal;
        private int _dealToken;
        private int _revealToken;
        private int _seeThroughToken;
        private bool _dealing;
        private bool _revealing;
        private GameSession _session;
        private int _rubLockIndex = -1;
        private bool _rubShakeReady;
        private Vector3 _rubRestWorldPos;
        private Vector3 _rubGrabOffset;
        private int _rubSavedOrder;
        private float _rubPeakOffset;
        private float _rubDragStartTime;
        private bool _rubSuccessFxShown;
        private int _rubPlayToken;
        private bool _rubPlaying;
        private const float MaxRubDrag = 10f;
        /// <summary>松手时峰值偏移低于此值视为搓牌失败。</summary>
        public const float MinRubOffset = 2f;
        /// <summary>从开始跟手到松手的最短持续时间（秒）。</summary>
        public const float MinRubDuration = 0.5f;

        public bool IsDealing => _dealing;
        public bool IsBusy => _dealing || _revealing || _rubPlaying;
        public bool IsRubPlaying => _rubPlaying;
        public bool IsRubPreviewActive => _rubLockIndex >= 0;
        public bool IsRubShakeReady => _rubShakeReady;
        public float RubPeakOffset => _rubPeakOffset;
        public float RubDragElapsed => _rubDragStartTime > 0f ? Time.time - _rubDragStartTime : 0f;
        public int RubLockIndex => _rubLockIndex;
        public event System.Action DealFinished;

        public bool TryGetPlayerCard(int index, out CardItem item)
        {
            item = null;
            if (_player == null || index < 0 || index >= _player.Items.Length)
            {
                return false;
            }

            if (!_player.Landed[index])
            {
                return false;
            }

            item = _player.Items[index];
            return item != null;
        }

        private sealed class SeatView
        {
            public Transform Node;
            public Transform[] Points = new Transform[CardsPerHand];
            public CardItem[] Items = new CardItem[CardsPerHand];
            public Transform[] Shadows = new Transform[CardsPerHand];
            public bool[] Landed = new bool[CardsPerHand];
            public bool IsPlayer;
        }

        public void Bind(Transform hud, IResourceService resources)
        {
            _resources = resources;
            _hud = hud;
            _shadows.Bind(hud);
            _dealPoint = FindChild(hud, "dealpoint") ?? FindChild(hud, "DealPoint");
            if (_dealPoint != null)
            {
                _dealPile = _dealPoint.GetComponent<CardDealPoint>();
                if (_dealPile == null)
                {
                    _dealPile = _dealPoint.gameObject.AddComponent<CardDealPoint>();
                }
            }
            else
            {
                _dealPoint = hud;
                _dealPile = null;
            }

            var mine = FindChild(hud, "mineNode");
            _player = BuildSeat(mine, true);
            BindEnemySlots(hud);
            HideLegacyIcons(mine);
            HideLegacyIcons(_other != null ? _other.Node : null);
        }

        public void Sync(GameSession session)
        {
            _session = session;
            if (session == null || _hud == null)
            {
                return;
            }

            ApplySeatVisibility(session);
            if (session.DealSerial <= 0)
            {
                ClearAllItems();
                HideDealPile();
                _shownDeal = 0;
                return;
            }

            if (session.DealSerial != _shownDeal)
            {
                PlayDeal(session);
                return;
            }

            if (_dealing || _revealing)
            {
                if (!_dealing)
                {
                    SyncRubSelectFx(session);
                }

                return;
            }

            EnsureDisplayedEnemy(session);

            if (session.RevealPlaySerial > 0 && session.RevealPlaySerial != _shownReveal)
            {
                SyncRubSelectFx(session);
                SyncSelectLift(session);
                PlayReveal(session);
                return;
            }

            SyncAllFaces(session);
            SyncSelectLift(session);
            SyncSeeThrough(session);
            SyncRubSelectFx(session);
        }

        public int HitPlayerCard(Camera camera)
        {
            if (camera == null || _player == null)
            {
                return -1;
            }

            var mouse = Input.mousePosition;
            mouse.z = Mathf.Abs(camera.transform.position.z);
            var world = camera.ScreenToWorldPoint(mouse);
            var hit = Physics2D.Raycast(world, Vector2.zero);
            if (hit.collider == null)
            {
                return -1;
            }

            var hitGo = hit.collider.gameObject;
            for (var i = 0; i < _player.Items.Length; i++)
            {
                var item = _player.Items[i];
                if (item == null || !_player.Landed[i])
                {
                    continue;
                }

                if (hitGo == item.gameObject || hitGo.transform.IsChildOf(item.transform))
                {
                    return i;
                }
            }

            return -1;
        }

        public int HitEnemySlot(Camera camera)
        {
            if (camera == null)
            {
                return -1;
            }

            var mouse = Input.mousePosition;
            mouse.z = Mathf.Abs(camera.transform.position.z);
            var world = camera.ScreenToWorldPoint(mouse);
            var hit = Physics2D.Raycast(world, Vector2.zero);
            if (hit.collider == null)
            {
                return -1;
            }

            var hitGo = hit.collider.gameObject;
            if (_other == null)
            {
                return -1;
            }

            for (var i = 0; i < _other.Items.Length; i++)
            {
                var item = _other.Items[i];
                if (item == null || !_other.Landed[i])
                {
                    continue;
                }

                if (hitGo == item.gameObject || hitGo.transform.IsChildOf(item.transform))
                {
                    return _session != null ? _session.DisplayedEnemyVisualSlot : -1;
                }
            }

            return -1;
        }

        public void TintPlayerCard(int index, Color color)
        {
            var sr = PlayerRenderer(index);
            if (sr != null)
            {
                sr.color = color;
            }
        }

        public void BeginInstantRub(int index)
        {
            PlayRubReplace(index, null, null);
        }

        /// <summary>
        /// 选中一张牌搓牌：立刻翻到背面并播 ChangeCard01，约 1.5 秒后翻回正面揭示新牌。
        /// </summary>
        public void PlayRubReplace(int index, Func<bool> applyReplace, Action onComplete)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                onComplete?.Invoke();
                return;
            }

            var item = _player.Items[index];
            if (item == null || !_player.Landed[index])
            {
                onComplete?.Invoke();
                return;
            }

            StopRubReplace(restore: false);
            var token = ++_rubPlayToken;
            _rubPlaying = true;
            _rubLockIndex = index;
            _rubShakeReady = true;
            _rubRestWorldPos = _player.Points[index] != null
                ? _player.Points[index].position
                : item.transform.position;
            BringRubCardToFront(index);
            SetPlayerChangeSelectFx(false);
            item.PlayChangeReplaceFx();
            TintPlayerCard(index, Color.white);

            var replaced = false;

            void Done()
            {
                if (token != _rubPlayToken)
                {
                    return;
                }

                item.HideChangeCardFx();
                RestoreRubCardLayer(index);
                _rubLockIndex = -1;
                _rubPlaying = false;
                _rubSeq = null;
                onComplete?.Invoke();
            }

            void FlipFront()
            {
                if (token != _rubPlayToken)
                {
                    return;
                }

                ApplyReplaceIfNeeded();
                item.StopTweenAnimation();
                var flip = item.FlipTo(CardFaceState.Front, FlipDuration);
                if (flip == null)
                {
                    item.SetFace(CardFaceState.Front);
                    Done();
                    return;
                }

                _rubSeq = DOTween.Sequence()
                    .AppendInterval(FlipDuration)
                    .OnComplete(Done);
            }

            void ApplyReplaceIfNeeded()
            {
                if (token != _rubPlayToken || replaced)
                {
                    return;
                }

                if (item.FaceState != CardFaceState.Back)
                {
                    item.SetFace(CardFaceState.Back);
                }

                applyReplace?.Invoke();
                replaced = true;
            }

            var flipBack = item.FlipTo(CardFaceState.Back, FlipDuration);
            if (flipBack != null)
            {
                flipBack.OnComplete(ApplyReplaceIfNeeded);
            }
            else
            {
                ApplyReplaceIfNeeded();
            }

            _rubSeq = DOTween.Sequence()
                .AppendInterval(RubReplaceRevealDelay)
                .OnComplete(FlipFront);
        }

        /// <summary>长按搓牌：抬起并翻到背面，翻完后才可拖拽抖动。</summary>
        public void BeginRubPreview(int index)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                return;
            }

            var item = _player.Items[index];
            if (item == null || !_player.Landed[index] || _player.Points[index] == null)
            {
                return;
            }

            if (_rubLockIndex >= 0 && _rubLockIndex != index)
            {
                CancelRubPreview();
            }

            _rubLockIndex = index;
            _rubShakeReady = false;
            _rubRestWorldPos = _player.Points[index].position;
            var lifted = _rubRestWorldPos + SelectOffset(_player);
            SyncSlotShadow(_player, index, true);
            BringRubCardToFront(index);
            item.MoveTo(lifted, SelectLiftDuration, Ease.OutQuad).OnUpdate(FollowRubShadow);
            FollowRubShadow();
            TintPlayerCard(index, Color.white);
            item.SetDragEffectVisible(true);

            var flip = item.FlipTo(CardFaceState.Back, FlipDuration);
            if (flip == null)
            {
                _rubShakeReady = item.FaceState == CardFaceState.Back;
                return;
            }

            var token = index;
            flip.OnComplete(() =>
            {
                if (_rubLockIndex == token)
                {
                    _rubShakeReady = true;
                }
            });
        }

        /// <summary>开始跟手：牌立刻移到手指位置。</summary>
        public void BeginRubDrag(Camera camera, Vector3 screenPos)
        {
            if (_rubLockIndex < 0 || !_rubShakeReady || _player == null || camera == null)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            item.StopMove();
            _rubPeakOffset = 0f;
            _rubDragStartTime = Time.time;
            _rubSuccessFxShown = false;
            _rubGrabOffset = Vector3.zero;
            DragRubCard(camera, screenPos);
        }

        /// <summary>跟手拖拽，限制在抬起点附近。</summary>
        public void DragRubCard(Camera camera, Vector3 screenPos)
        {
            if (_rubLockIndex < 0 || !_rubShakeReady || _player == null || camera == null)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            var rest = _rubRestWorldPos + SelectOffset(_player);
            var mouse = ScreenOnCardPlane(camera, screenPos, item.transform.position.z);
            var target = mouse + _rubGrabOffset;
            var offset = target - rest;
            offset.z = 0f;
            offset = Vector3.ClampMagnitude(offset, MaxRubDrag);
            item.transform.position = rest + offset;
            FollowRubShadow();
            if (offset.magnitude > _rubPeakOffset)
            {
                _rubPeakOffset = offset.magnitude;
            }

            var t = Mathf.Clamp01(offset.magnitude / MaxRubDrag);
            TintPlayerCard(_rubLockIndex, Color.Lerp(Color.white, new Color(1f, 0.85f, 0.4f), t));
            TryShowRubSuccessEffect();
        }

        private void TryShowRubSuccessEffect()
        {
            if (_rubSuccessFxShown || _rubLockIndex < 0 || _player == null)
            {
                return;
            }

            if (_rubPeakOffset < MinRubOffset || RubDragElapsed < MinRubDuration)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            _rubSuccessFxShown = true;
            item.PlayDragSuccessEffect();
        }

        private static Vector3 ScreenOnCardPlane(Camera camera, Vector3 screenPos, float cardZ)
        {
            screenPos.z = Mathf.Abs(camera.transform.position.z - cardZ);
            return camera.ScreenToWorldPoint(screenPos);
        }

        /// <summary>拖拽未达标：停在抬起的背面，不翻回正面。</summary>
        public void ResetRubShake()
        {
            if (_rubLockIndex < 0 || _player == null)
            {
                return;
            }

            var item = _player.Items[_rubLockIndex];
            if (item == null)
            {
                return;
            }

            item.transform.position = _rubRestWorldPos + SelectOffset(_player);
            FollowRubShadow();
            _rubPeakOffset = 0f;
            _rubDragStartTime = 0f;
            _rubSuccessFxShown = false;
            TintPlayerCard(_rubLockIndex, Color.white);
        }

        /// <summary>取消点选：翻回正面并落回原位。</summary>
        public void CancelRubPreview()
        {
            StopRubReplace(restore: false);
            if (_rubLockIndex < 0 || _player == null)
            {
                return;
            }

            var index = _rubLockIndex;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            _rubSuccessFxShown = false;
            var item = _player.Items[index];
            if (item == null)
            {
                return;
            }

            var dest = _player.Points[index] != null ? _player.Points[index].position : _rubRestWorldPos;
            RestoreRubCardLayer(index);
            item.HideDragEffects();
            item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad)
                .OnUpdate(() => FollowRubShadowAt(index))
                .OnComplete(() =>
                {
                    if (_rubLockIndex != index)
                    {
                        SyncSlotShadow(_player, index, false);
                    }
                });
            item.FlipTo(CardFaceState.Front, FlipDuration);
            TintPlayerCard(index, Color.white);
        }

        /// <summary>
        /// 搓牌成功：手牌已由 Sync SetCard（仍锁在背面），翻回正面揭示新牌。
        /// </summary>
        public void CompleteRubFlip()
        {
            if (_rubLockIndex < 0 || _player == null)
            {
                return;
            }

            var index = _rubLockIndex;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            var item = _player.Items[index];
            if (item == null)
            {
                return;
            }

            var selected = _session != null && _session.Player != null && _session.Player.IsCardSelected(index);
            var dest = _player.Points[index] != null ? _player.Points[index].position : _rubRestWorldPos;
            if (selected)
            {
                dest += SelectOffset(_player);
            }

            RestoreRubCardLayer(index);
            item.SetDragEffectVisible(false);
            if (_rubSuccessFxShown)
            {
                item.HideDragSuccessLater();
            }
            else
            {
                item.PlayDragSuccessEffect(1.2f);
            }

            _rubSuccessFxShown = false;
            item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad)
                .OnUpdate(() => FollowRubShadowAt(index))
                .OnComplete(() =>
                {
                    if (_rubLockIndex != index)
                    {
                        SyncSlotShadow(_player, index, selected);
                    }
                });
            item.FlipTo(CardFaceState.Front, FlipDuration);
            TintPlayerCard(index, Color.white);
        }

        public void CollectSelectedCards(GameSession session, SeatState seat, List<CardItem> dest)
        {
            if (dest == null)
            {
                return;
            }

            dest.Clear();
            var view = ViewOf(session, seat);
            if (view == null || seat == null)
            {
                return;
            }

            var anySelected = seat.CountSelectedCards() > 0;
            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i])
                {
                    continue;
                }

                if (anySelected && !seat.IsCardSelected(i))
                {
                    continue;
                }

                dest.Add(item);
            }
        }

        public void Dispose()
        {
            StopRubReplace(restore: false);
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _dealToken++;
            _revealToken++;
            _seeThroughToken++;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            ClearAllItems();
            _shadows.Dispose();
            if (_resources != null)
            {
                if (_prefab != null)
                {
                    _resources.Release(ResResourcePaths.CardIcon);
                }
            }

            _prefab = null;
            _resources = null;
        }

        private void PlayDeal(GameSession session)
        {
            StopRubReplace(restore: false);
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _revealing = false;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            _shownDeal = session.DealSerial;
            _shownEnemyId = session.DisplayedEnemy != null ? session.DisplayedEnemy.Id : int.MinValue;
            _shownReveal = session.RevealPlaySerial;
            var token = ++_dealToken;
            _seeThroughToken++;
            _dealing = true;
            ClearAllItems();
            ShowDealPile();

            var seats = CollectDealSeats(session);
            var seq = DOTween.Sequence();
            var delay = AppendShuffle(seq, token);
            var order = 0;
            var maxRound = Math.Max(GameBalance.EnemyCardsDealt, session.PlayerDealCount);
            for (var round = 0; round < maxRound; round++)
            {
                for (var s = 0; s < seats.Count; s++)
                {
                    var view = seats[s].View;
                    var seat = seats[s].Seat;
                    if (round >= GameBalance.CardsDealt(seat.IsPlayer, session.Run))
                    {
                        continue;
                    }

                    if (view == null || round >= view.Points.Length || view.Points[round] == null)
                    {
                        continue;
                    }

                    var cardIndex = round;
                    var capturedOrder = order;
                    seq.InsertCallback(delay, () =>
                    {
                        if (token != _dealToken)
                        {
                            return;
                        }

                        SpawnAndFly(view, seat, cardIndex, capturedOrder, token);
                    });
                    delay += DealStagger;
                    order++;
                }
            }

            seq.AppendInterval(DealMoveDuration);
            seq.AppendInterval(DealFlipPause);
            var playerCount = GameBalance.CardsDealt(true, session.Run);
            for (var i = 0; i < playerCount; i++)
            {
                var cardIndex = i;
                seq.AppendCallback(() =>
                {
                    if (token != _dealToken)
                    {
                        return;
                    }

                    FlipSeatCard(session, _player, session.Player, cardIndex);
                });
                if (i < playerCount - 1)
                {
                    seq.AppendInterval(DealStagger);
                }
            }

            seq.AppendInterval(FlipDuration);
            seq.AppendCallback(() =>
            {
                if (token != _dealToken)
                {
                    return;
                }

                HideDealPile();
            });
            seq.OnComplete(() =>
            {
                if (token != _dealToken)
                {
                    return;
                }

                _dealing = false;
                SyncAllFaces(session);
                DealFinished?.Invoke();
            });
            _dealSeq = seq;
        }

        private float AppendShuffle(Sequence seq, int token)
        {
            if (_dealPile == null)
            {
                return 0f;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not found at Res/" + ResResourcePaths.CardIcon);
                return 0f;
            }

            var delay = 0f;
            for (var i = 0; i < Deck.Size; i++)
            {
                var last = i == Deck.Size - 1;
                seq.InsertCallback(delay, () =>
                {
                    if (token != _dealToken)
                    {
                        return;
                    }

                    var item = SpawnPileCard(prefab);
                    if (item == null)
                    {
                        return;
                    }

                    item.SetSpritesVisible(false);
                    _dealPile.Attach(item);
                    item.PlayShuffleAppear(last);
                });
                delay += ShuffleStagger;
            }

            return delay + ShuffleAppear02 - ShuffleStagger;
        }

        private void PlayReveal(GameSession session)
        {
            _revealSeq?.Kill();
            _shownReveal = session.RevealPlaySerial;
            var token = ++_revealToken;
            _revealing = true;
            var seq = DOTween.Sequence();
            var delay = 0.18f;
            for (var s = 0; s < session.RevealSeatIds.Count; s++)
            {
                var seatId = session.RevealSeatIds[s];
                var seat = SeatById(session, seatId);
                var view = ViewOf(session, seat);
                if (view == null || seat == null)
                {
                    continue;
                }

                if (!seat.IsPlayer)
                {
                    ClearSeatSeeThrough(view);
                }

                var count = CardCount(view, seat, session);
                for (var i = 0; i < count; i++)
                {
                    if (SkipEnemyUnselectedFlip(seat, i))
                    {
                        continue;
                    }

                    var cardIndex = i;
                    seq.InsertCallback(delay, () =>
                    {
                        if (token != _revealToken)
                        {
                            return;
                        }

                        FlipSeatCard(session, view, seat, cardIndex);
                    });
                    delay += RevealCardGap;
                }

                var capturedId = seatId;
                var capturedView = view;
                seq.InsertCallback(delay, () =>
                {
                    if (token != _revealToken)
                    {
                        return;
                    }

                    session.AnnounceSeatRevealed(capturedId);
                });
                delay += RevealSeatGap;
            }

            seq.InsertCallback(delay, () =>
            {
                if (token != _revealToken)
                {
                    return;
                }

                HighlightWinner(session);
            });
            delay += 0.08f;

            var winnerView = ViewOf(session, SeatById(session, session.RevealWinnerId));
            var winnerSeat = SeatById(session, session.RevealWinnerId);
            var settleCount = CardCount(winnerView, winnerSeat, session);
            for (var i = 0; i < settleCount; i++)
            {
                if (winnerSeat != null && !winnerSeat.IsCardSelected(i))
                {
                    continue;
                }

                var cardIndex = i;
                seq.InsertCallback(delay, () =>
                {
                    if (token != _revealToken)
                    {
                        return;
                    }

                    PlaySettleCard(winnerView, cardIndex);
                });
                delay += SettleClipDuration;
            }

            delay += SettleClipDuration;
            seq.InsertCallback(delay, () =>
            {
                if (token != _revealToken)
                {
                    return;
                }

                _revealing = false;
                session.FinishRevealPlay();
            });
            _revealSeq = seq;
        }

        private void FlipSeatCard(GameSession session, SeatView view, SeatState seat, int cardIndex)
        {
            if (view == null || seat == null || cardIndex < 0 || cardIndex >= view.Items.Length)
            {
                return;
            }

            var item = view.Items[cardIndex];
            if (item == null || !view.Landed[cardIndex])
            {
                return;
            }

            var card = ShownCard(seat, cardIndex, seat.IsPlayer, session);
            if (!item.Card.Equals(card))
            {
                item.SetCard(card);
            }

            ApplyFace(item, CardFaceState.Front, true);
        }

        private static void ClearSeatSeeThrough(SeatView view)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item != null && item.IsBackSeeThrough)
                {
                    item.SetBackSeeThrough(false);
                }
            }
        }

        /// <summary>敌人开牌只翻已锁定的 3 张；未选中的保持背面。</summary>
        private static bool SkipEnemyUnselectedFlip(SeatState seat, int cardIndex)
        {
            return seat != null &&
                   !seat.IsPlayer &&
                   seat.CountSelectedCards() > 0 &&
                   !seat.IsCardSelected(cardIndex);
        }

        /// <summary>透视阶段才抬敌人选中牌；开牌翻面/已亮牌时落回原位。</summary>
        private static bool EnemyUsesSelectLift(GameSession session, SeatState seat)
        {
            if (seat == null || seat.IsPlayer)
            {
                return true;
            }

            if (session == null)
            {
                return false;
            }

            return !seat.ShowCards &&
                   !session.CardsRevealed &&
                   session.Phase != GamePhase.Showdown;
        }

        private static void PlaySettleCard(SeatView view, int cardIndex)
        {
            if (view == null || cardIndex < 0 || cardIndex >= view.Items.Length)
            {
                return;
            }

            var item = view.Items[cardIndex];
            if (item != null && view.Landed[cardIndex])
            {
                item.PlaySettle();
            }
        }

        private void HighlightWinner(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            var winner = SeatById(session, session.RevealWinnerId);
            var view = ViewOf(session, winner);
            TintSeat(view, winner, new Color(1f, 0.9f, 0.45f));
        }

        private static void TintSeat(SeatView view, SeatState seat, Color color)
        {
            if (view == null)
            {
                return;
            }

            var onlySelected = seat != null && seat.CountSelectedCards() > 0;
            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item == null)
                {
                    continue;
                }

                if (onlySelected && !seat.IsCardSelected(i))
                {
                    continue;
                }

                var sr = item.CurrentRenderer;
                if (sr != null)
                {
                    sr.color = color;
                }
            }
        }

        private SeatView ViewOf(GameSession session, SeatState seat)
        {
            if (session == null || seat == null)
            {
                return null;
            }

            if (seat.IsPlayer)
            {
                return _player;
            }

            var displayed = session.DisplayedEnemy;
            if (displayed != null && displayed.Id == seat.Id)
            {
                return _other;
            }

            return null;
        }

        private static SeatState SeatById(GameSession session, int id)
        {
            if (session == null)
            {
                return null;
            }

            if (session.Player != null && session.Player.Id == id)
            {
                return session.Player;
            }

            for (var i = 0; i < session.Enemies.Length; i++)
            {
                if (session.Enemies[i].Id == id)
                {
                    return session.Enemies[i];
                }
            }

            return null;
        }

        private CardItem SpawnPileCard(GameObject prefab)
        {
            if (prefab == null || _dealPile == null || _dealPoint == null)
            {
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = "DealCard" + (_dealPile.Count + 1);

            var item = go.GetComponent<CardItem>();
            if (item == null)
            {
                item = go.AddComponent<CardItem>();
            }

            item.Initialize(
                default,
                CardFaceState.Back,
                _dealPile.GetWorldPosition(_dealPile.Count),
                _dealPoint.rotation,
                Vector3.one);
            return item;
        }

        private void SpawnAndFly(SeatView view, SeatState seat, int cardIndex, int order, int token)
        {
            if (view == null || view.Points[cardIndex] == null || seat == null || seat.Hand == null)
            {
                return;
            }

            var point = view.Points[cardIndex];
            var card = cardIndex < seat.Hand.Length ? seat.Hand[cardIndex] : default;
            var item = TakeDealCard(card, order);
            if (item == null)
            {
                return;
            }

            item.gameObject.name = view.IsPlayer
                ? "PlayerCard" + (cardIndex + 1)
                : point.parent.name + "_Card" + (cardIndex + 1);

            view.Items[cardIndex] = item;
            view.Landed[cardIndex] = false;

            var duration = DealMoveDuration;
            item.transform.DOScale(point.lossyScale, duration).SetEase(Ease.OutQuad);
            item.RotateTo(point.rotation * CardItem.FaceYaw(CardFaceState.Back), duration, Ease.OutCubic);
            item.MoveTo(point.position, duration, Ease.OutCubic).OnComplete(() =>
            {
                if (token != _dealToken || item == null)
                {
                    return;
                }

                SnapToPoint(item, point);
                view.Landed[cardIndex] = true;
            });
        }

        private CardItem TakeDealCard(Card card, int order)
        {
            var item = _dealPile != null ? _dealPile.Pop() : null;
            if (item == null)
            {
                item = SpawnLooseDealCard(card);
            }

            if (item == null)
            {
                return null;
            }

            item.PlayDeal();
            _dealPile?.Peek()?.PlayDealHighlight();

            item.SetCard(card);
            item.SetFace(CardFaceState.Back);
            item.SetSortingOrder(FlySortingOrder(order));
            item.SetSpritesVisible(true);
            var sr = item.CurrentRenderer;
            if (sr != null)
            {
                sr.color = Color.white;
            }

            EnsureCollider(item);
            return item;
        }

        private int FlySortingOrder(int order)
        {
            var pileTop = _dealPile != null ? _dealPile.GetSortingOrder(_dealPile.Count) : 20;
            return pileTop + 10 + order;
        }

        private CardItem SpawnLooseDealCard(Card card)
        {
            var prefab = LoadPrefab();
            if (prefab == null)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not found at Res/" + ResResourcePaths.CardIcon);
                return null;
            }

            var start = _dealPoint != null ? _dealPoint.position : _hud.position;
            var startRot = _dealPoint != null ? _dealPoint.rotation : Quaternion.identity;
            var go = UnityEngine.Object.Instantiate(prefab);

            var item = go.GetComponent<CardItem>();
            if (item == null)
            {
                item = go.AddComponent<CardItem>();
            }

            item.Initialize(card, CardFaceState.Back, start, startRot, Vector3.one);
            return item;
        }

        private void SyncAllFaces(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            SyncSeatFaces(_player, session.Player, true, session);
            var displayed = session.DisplayedEnemy;
            if (displayed != null)
            {
                SyncSeatFaces(_other, displayed, false, session);
            }
        }

        private void SyncRubSelectFx(GameSession session)
        {
            var show = session != null &&
                       session.SelectingRubTarget &&
                       !_dealing &&
                       !_rubPlaying;
            SetPlayerChangeSelectFx(show);
        }

        private void SetPlayerChangeSelectFx(bool show)
        {
            if (_player == null)
            {
                return;
            }

            for (var i = 0; i < _player.Items.Length; i++)
            {
                var item = _player.Items[i];
                if (item == null || !_player.Landed[i])
                {
                    continue;
                }

                item.SetDragEffectVisible(show);
            }
        }

        private void StopRubReplace(bool restore)
        {
            _rubPlayToken++;
            if (_rubSeq != null && _rubSeq.IsActive())
            {
                _rubSeq.Kill();
            }

            _rubSeq = null;
            _rubPlaying = false;
            if (_player != null && _rubLockIndex >= 0 && _rubLockIndex < _player.Items.Length)
            {
                var item = _player.Items[_rubLockIndex];
                item?.StopTweenAnimation();
                item?.HideChangeCardFx();
            }

            if (!restore)
            {
                return;
            }

            if (_rubLockIndex >= 0)
            {
                CancelRubPreview();
            }
        }

        private void SyncSeatFaces(SeatView view, SeatState seat, bool player, GameSession session)
        {
            if (view == null || seat == null)
            {
                return;
            }

            for (var i = 0; i < CardsPerHand; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i])
                {
                    continue;
                }

                var card = ShownCard(seat, i, player, session);
                if (!item.Card.Equals(card))
                {
                    item.SetCard(card);
                }

                // 搓牌预览锁面：允许换牌数据，但不强制翻回正面。
                if (player && (i == _rubLockIndex || session.PendingRubIndex == i))
                {
                    continue;
                }

                var desired = DesiredFace(session, seat, player, i);
                ApplyFace(item, desired, true);
                var sr = item.CurrentRenderer;
                if (sr == null)
                {
                    continue;
                }

                sr.color = Color.white;
                if (seat.Folded)
                {
                    sr.color = new Color(0.7f, 0.7f, 0.74f, 1f);
                }
                else if (session.RevealWinnerId == seat.Id &&
                         (session.Phase == GamePhase.Showdown ||
                          session.Phase == GamePhase.WaitingAttack ||
                          session.Phase == GamePhase.RoundSettle) &&
                         (seat.CountSelectedCards() == 0 || seat.IsCardSelected(i)))
                {
                    sr.color = new Color(1f, 0.92f, 0.55f, 1f);
                }
                else if (player && session.Run.MagnifierThisRound && session.Run.PeekSuitUsed &&
                    session.Run.PeekSuitIndex == i && item.FaceState == CardFaceState.Back)
                {
                    sr.color = SuitTint(seat.Hand[i].Suit);
                }
            }
        }

        private void SyncSelectLift(GameSession session)
        {
            if (_dealing || session == null)
            {
                return;
            }

            LiftSeat(_player, session.Player, SelectOffset(_player));
            var displayed = session.DisplayedEnemy;
            if (displayed != null)
            {
                LiftSeat(_other, displayed, SelectOffset(_other), EnemyUsesSelectLift(session, displayed));
            }
        }

        /// <summary>透视：先抬牌，抬完再 SetBackSeeThrough。关掉透视则立刻恢复。</summary>
        private void SyncSeeThrough(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            var token = ++_seeThroughToken;
            ApplySeatSeeThrough(_player, session.Player, true, session, token);
            var displayed = session.DisplayedEnemy;
            if (displayed != null)
            {
                ApplySeatSeeThrough(_other, displayed, false, session, token);
            }
        }

        private void ApplySeatSeeThrough(
            SeatView view,
            SeatState seat,
            bool player,
            GameSession session,
            int token)
        {
            if (view == null || seat == null)
            {
                return;
            }

            for (var i = 0; i < CardsPerHand; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i])
                {
                    continue;
                }

                var desired = DesiredFace(session, seat, player, i);
                var want = desired == CardFaceState.Back &&
                           session.IsSpyRevealed(seat.Id, i) &&
                           EnemyUsesSelectLift(session, seat);
                if (want == item.IsBackSeeThrough)
                {
                    continue;
                }

                if (!want)
                {
                    item.SetBackSeeThrough(false);
                    continue;
                }

                var dest = view.Points[i] != null
                    ? view.Points[i].position + (seat.IsCardSelected(i) ? SelectOffset(view) : Vector3.zero)
                    : item.transform.position;
                var lifting = (item.transform.position - dest).sqrMagnitude >= 0.0004f;
                if (!lifting)
                {
                    item.SetBackSeeThrough(true);
                    continue;
                }

                var captured = item;
                DOVirtual.DelayedCall(SelectLiftDuration, () =>
                {
                    if (token != _seeThroughToken || captured == null)
                    {
                        return;
                    }

                    captured.SetBackSeeThrough(true);
                });
            }
        }

        /// <summary>
        /// 选中牌朝玩家方向挪开：玩家向上，敌人（上方 otherNode）向下。
        /// </summary>
        private Vector3 SelectOffset(SeatView view)
        {
            if (view == null || view.IsPlayer)
            {
                return Vector3.up * SelectLift;
            }

            return Vector3.down * SelectLift;
        }

        private void LiftSeat(SeatView view, SeatState seat, Vector3 offset, bool allowLift = true)
        {
            if (view == null || seat == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item == null || !view.Landed[i] || view.Points[i] == null)
                {
                    continue;
                }

                // 搓牌拖拽/换牌动画中自己管位置，避免 Sync 把牌拽回原点。
                if (view.IsPlayer && (i == _rubLockIndex || (_session != null && _session.PendingRubIndex == i)))
                {
                    continue;
                }

                var selected = allowLift &&
                               (seat.IsCardSelected(i) ||
                                (view.IsPlayer && _session != null && _session.SelectingRubTarget));
                var dest = view.Points[i].position + (selected ? offset : Vector3.zero);
                if ((item.transform.position - dest).sqrMagnitude < 0.0004f)
                {
                    SyncSlotShadow(view, i, selected);
                    continue;
                }

                if (selected)
                {
                    SyncSlotShadow(view, i, true);
                    item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad);
                    continue;
                }

                // 取消选中：阴影保留到落位动画结束再回收；期间重新选中则 OnComplete 里跳过回收。
                var index = i;
                var lift = allowLift;
                item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad).OnComplete(() =>
                {
                    var stillLifted = lift &&
                                      (seat.IsCardSelected(index) ||
                                       (view.IsPlayer && _session != null && _session.SelectingRubTarget));
                    SyncSlotShadow(view, index, stillLifted);
                });
            }
        }

        private void SyncSlotShadow(SeatView view, int index, bool lifted)
        {
            if (lifted)
            {
                if (view.Shadows[index] != null)
                {
                    return;
                }

                view.Shadows[index] = _shadows.Rent(view.Points[index]);
                return;
            }

            _shadows.Return(ref view.Shadows[index]);
        }

        private void FollowRubShadow()
        {
            if (_rubLockIndex >= 0)
            {
                FollowRubShadowAt(_rubLockIndex);
            }
        }

        private void FollowRubShadowAt(int index)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                return;
            }

            var shadow = _player.Shadows[index];
            var item = _player.Items[index];
            if (shadow == null || item == null)
            {
                return;
            }

            var pos = item.transform.position;
            shadow.position = new Vector3(pos.x, pos.y, shadow.position.z);
        }

        private void BringRubCardToFront(int index)
        {
            var item = _player.Items[index];
            if (item == null)
            {
                return;
            }

            var sr = item.CurrentRenderer;
            _rubSavedOrder = sr != null ? sr.sortingOrder : 0;
            var top = PlayerHandMaxSorting() + 20;
            item.SetSortingOrder(top);
            SetShadowSorting(_player.Shadows[index], top - 1);
        }

        private void RestoreRubCardLayer(int index)
        {
            if (_player == null || index < 0 || index >= CardsPerHand)
            {
                return;
            }

            var item = _player.Items[index];
            if (item != null)
            {
                item.SetSortingOrder(_rubSavedOrder);
            }

            SetShadowSorting(_player.Shadows[index], 2);
        }

        private int PlayerHandMaxSorting()
        {
            var max = 0;
            if (_player == null)
            {
                return max;
            }

            for (var i = 0; i < CardsPerHand; i++)
            {
                var item = _player.Items[i];
                var sr = item != null ? item.CurrentRenderer : null;
                if (sr != null && sr.sortingOrder > max)
                {
                    max = sr.sortingOrder;
                }
            }

            return max;
        }

        private static void SetShadowSorting(Transform shadow, int order)
        {
            if (shadow == null)
            {
                return;
            }

            var sr = shadow.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingOrder = order;
            }
        }

        private static int CardCount(SeatView view, SeatState seat, GameSession session)
        {
            if (view == null)
            {
                return 0;
            }

            return GameBalance.CardsDealt(seat != null && seat.IsPlayer, session?.Run);
        }

        private static CardFaceState DesiredFace(GameSession session, SeatState seat, bool player, int cardIndex)
        {
            if (session == null || seat == null)
            {
                return CardFaceState.Back;
            }

            if (player &&
                session.Phase == GamePhase.WaitingRub &&
                session.PendingRubIndex == cardIndex)
            {
                return CardFaceState.Back;
            }

            if (seat.ShowCards || session.CardsRevealed)
            {
                if (SkipEnemyUnselectedFlip(seat, cardIndex))
                {
                    return CardFaceState.Back;
                }

                return CardFaceState.Front;
            }

            if (player && session.Phase == GamePhase.WaitingRub && session.PendingRubIndex == cardIndex)
            {
                return CardFaceState.Back;
            }

            if (player && seat.Looked)
            {
                return CardFaceState.Front;
            }

            return CardFaceState.Back;
        }

        private static void ApplyFace(CardItem item, CardFaceState face, bool animate)
        {
            if (item == null || item.FaceState == face)
            {
                return;
            }

            if (animate)
            {
                item.FlipTo(face, FlipDuration);
            }
            else
            {
                item.SetFace(face);
            }
        }

        private void ApplySeatVisibility(GameSession session)
        {
            if (_other != null && _other.Node != null)
            {
                var displayed = session != null ? session.DisplayedEnemy : null;
                _other.Node.gameObject.SetActive(displayed != null && displayed.Alive);
            }

            if (_player != null && _player.Node != null)
            {
                _player.Node.gameObject.SetActive(true);
            }
        }

        private List<(SeatView View, SeatState Seat)> CollectDealSeats(GameSession session)
        {
            var list = new List<(SeatView, SeatState)>();
            if (_player != null && session.Player != null)
            {
                list.Add((_player, session.Player));
            }

            var displayed = session.DisplayedEnemy;
            if (_other != null && displayed != null && displayed.Alive)
            {
                list.Add((_other, displayed));
            }

            return list;
        }

        private void EnsureDisplayedEnemy(GameSession session)
        {
            var displayed = session != null ? session.DisplayedEnemy : null;
            var id = displayed != null ? displayed.Id : int.MinValue;
            if (id == _shownEnemyId)
            {
                return;
            }

            _shownEnemyId = id;
            if (displayed == null || !displayed.Alive)
            {
                ClearSeatItems(_other);
                return;
            }

            PlaceEnemyHand(_other, displayed, session);
        }

        private void PlaceEnemyHand(SeatView view, SeatState seat, GameSession session)
        {
            ClearSeatItems(view);
            if (view == null || seat == null || seat.Hand == null)
            {
                return;
            }

            var count = GameBalance.CardsDealt(false, session != null ? session.Run : null);
            for (var i = 0; i < count && i < view.Points.Length; i++)
            {
                var point = view.Points[i];
                if (point == null)
                {
                    continue;
                }

                var card = i < seat.Hand.Length ? seat.Hand[i] : default;
                var item = SpawnLooseDealCard(card);
                if (item == null)
                {
                    continue;
                }

                item.gameObject.name = "EnemyCard" + (i + 1);
                item.SetSpritesVisible(true);
                EnsureCollider(item);
                SnapToPoint(item, point);
                view.Items[i] = item;
                view.Landed[i] = true;
                ApplyFace(item, DesiredFace(session, seat, false, i), false);
            }
        }

        private void BindEnemySlots(Transform hud)
        {
            _other = BuildSeat(FindChild(hud, "otherNode"), false);
        }

        private static SeatView BuildSeat(Transform node, bool player)
        {
            if (node == null)
            {
                return null;
            }

            var view = new SeatView { Node = node, IsPlayer = player };
            for (var i = 0; i < CardsPerHand; i++)
            {
                view.Points[i] = FindCardPoint(node, i + 1);
            }

            return view;
        }

        private static Transform FindCardPoint(Transform root, int index)
        {
            if (root == null)
            {
                return null;
            }

            var cardNode = FindChild(root, "cardNode") ?? root;
            return FindChild(cardNode, "carpoint" + index)
                   ?? FindChild(cardNode, "cardpoint" + index)
                   ?? FindChild(cardNode, "CardPoint" + index);
        }

        private static void HideLegacyIcons(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var cardNode = FindChild(root, "cardNode") ?? root;
            for (var i = 0; i < cardNode.childCount; i++)
            {
                var child = cardNode.GetChild(i);
                if (child.name.StartsWith("CardIcon") || child.name.StartsWith("Square"))
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private void ShowDealPile()
        {
            if (_dealPile != null && _dealPoint != null)
            {
                _dealPoint.gameObject.SetActive(true);
            }
        }

        private void HideDealPile()
        {
            _dealPile?.Clear();
            if (_dealPile != null && _dealPoint != null)
            {
                _dealPoint.gameObject.SetActive(false);
            }
        }

        private void ClearAllItems()
        {
            _dealPile?.Clear();
            ClearSeatItems(_player);
            ClearSeatItems(_other);
            _shownEnemyId = int.MinValue;
        }

        private void ClearSeatItems(SeatView view)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                if (view.Items[i] != null)
                {
                    UnityEngine.Object.Destroy(view.Items[i].gameObject);
                    view.Items[i] = null;
                }

                _shadows.Return(ref view.Shadows[i]);
                view.Landed[i] = false;
            }
        }

        private static void SnapToPoint(CardItem item, Transform point)
        {
            var t = item.transform;
            t.SetParent(point, true);
            t.localPosition = Vector3.zero;
            t.localScale = Vector3.one;
            item.SetFace(item.FaceState);
        }

        private static void EnsureCollider(CardItem item)
        {
            var sr = item != null ? item.CurrentRenderer : null;
            if (sr == null)
            {
                return;
            }

            var col = sr.GetComponent<BoxCollider2D>();
            if (col == null)
            {
                col = sr.gameObject.AddComponent<BoxCollider2D>();
            }

            col.size = sr.sprite != null ? sr.sprite.bounds.size : new Vector2(1f, 1.5f);
        }

        private SpriteRenderer PlayerRenderer(int index)
        {
            if (_player == null || index < 0 || index >= _player.Items.Length)
            {
                return null;
            }

            var item = _player.Items[index];
            return item != null ? item.CurrentRenderer : null;
        }

        private GameObject LoadPrefab()
        {
            if (_prefab != null)
            {
                return _prefab;
            }

            if (_resources == null)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not loaded: IResourceService is missing.");
                return null;
            }

            try
            {
                _prefab = _resources.LoadAsync<GameObject>(ResResourcePaths.CardIcon).GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not found at Res/" + ResResourcePaths.CardIcon + ": " + ex.Message);
                return null;
            }

            return _prefab;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 老花眼金花：已选三张同色时，少数花色贴图改成多数花色。手里牌数据不改。
        /// </summary>
        private static Card ShownCard(SeatState seat, int index, bool player, GameSession session)
        {
            if (seat?.Hand == null || index < 0 || index >= seat.Hand.Length)
            {
                return default;
            }

            var card = seat.Hand[index];
            if (!player ||
                session?.Run == null ||
                !RelicMechanics.HasMechanism(session.Run, MechanismType.SpecialFlush) ||
                seat.CountSelectedCards() != GameBalance.OpenHandSize ||
                !seat.IsCardSelected(index))
            {
                return card;
            }

            var selected = HandEvaluator.CopySelectedCards(seat.Hand, seat.CardSelected);
            if (!HandEvaluator.TryColorFlushDisplaySuit(selected, out var displaySuit))
            {
                return card;
            }

            return HandEvaluator.WithSuit(card, displaySuit);
        }

        private static Color SuitTint(Suit suit)
        {
            switch (suit)
            {
                case Suit.Heart: return new Color(0.85f, 0.25f, 0.25f);
                case Suit.Diamond: return new Color(0.9f, 0.45f, 0.2f);
                case Suit.Club: return new Color(0.2f, 0.45f, 0.25f);
                default: return new Color(0.2f, 0.25f, 0.55f);
            }
        }
    }
}
