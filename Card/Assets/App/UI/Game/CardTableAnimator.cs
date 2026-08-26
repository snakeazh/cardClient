using System;
using System.Collections.Generic;
using App.Game;
using App.Resources;
using DG.Tweening;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 从 GameHud.dealpoint 发牌到 mineNode / PlayerNode1–3。
    /// PlayerNode1/2/3 对应 GameUI 的 player1/2/3。
    /// </summary>
    public sealed class CardTableAnimator
    {
        private const int CardsPerHand = GameBalance.MaxCardsPerSeat;
        private const float DealMoveDuration = 0.32f;
        private const float DealStagger = 0.08f;
        private const float ShuffleStagger = 0.015f;
        private const float ShuffleAppear02 = 0.8f;
        private const float FlipDuration = 0.35f;
        private const float RevealFlipDuration = 0.28f;
        private const float RevealCardGap = 0.12f;
        private const float RevealSeatGap = 0.38f;
        private const float SettleClipDuration = 0.3f;
        private const float SelectLift = 0.28f;
        private const float SelectLiftDuration = 0.12f;
        private static readonly string[] EnemyNodeNames = { "PlayerNode1", "PlayerNode2", "PlayerNode3" };

        private IResourceService _resources;
        private GameObject _prefab;
        private readonly CardShadowPool _shadows = new CardShadowPool();

        private Transform _hud;
        private Transform _dealPoint;
        private CardDealPoint _dealPile;
        private SeatView _player;
        private readonly SeatView[] _enemies = new SeatView[3];
        private Sequence _dealSeq;
        private Sequence _revealSeq;
        private int _shownDeal = -1;
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
        private const float MaxRubDrag = 10f;
        /// <summary>松手时峰值偏移低于此值视为搓牌失败。</summary>
        public const float MinRubOffset = 2f;
        /// <summary>从开始跟手到松手的最短持续时间（秒）。</summary>
        public const float MinRubDuration = 0.5f;

        public bool IsDealing => _dealing;
        public bool IsBusy => _dealing || _revealing;
        public bool IsRubPreviewActive => _rubLockIndex >= 0;
        public bool IsRubShakeReady => _rubShakeReady;
        public float RubPeakOffset => _rubPeakOffset;
        public float RubDragElapsed => _rubDragStartTime > 0f ? Time.time - _rubDragStartTime : 0f;
        public int RubLockIndex => _rubLockIndex;
        public event System.Action DealFinished;

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
            for (var i = 0; i < _enemies.Length; i++)
            {
                if (_enemies[i] != null)
                {
                    HideLegacyIcons(_enemies[i].Node);
                }
            }
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
                return;
            }

            if (session.RevealPlaySerial > 0 && session.RevealPlaySerial != _shownReveal)
            {
                SyncSelectLift(session);
                PlayReveal(session);
                return;
            }

            SyncAllFaces(session);
            SyncSelectLift(session);
            SyncSeeThrough(session);
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
            for (var slot = 0; slot < _enemies.Length; slot++)
            {
                var view = _enemies[slot];
                if (view == null)
                {
                    continue;
                }

                for (var i = 0; i < view.Items.Length; i++)
                {
                    var item = view.Items[i];
                    if (item == null || !view.Landed[i])
                    {
                        continue;
                    }

                    if (hitGo == item.gameObject || hitGo.transform.IsChildOf(item.transform))
                    {
                        return slot;
                    }
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

            var dest = _player.Points[index] != null ? _player.Points[index].position : _rubRestWorldPos;
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
                        SyncSlotShadow(_player, index, false);
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
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _revealing = false;
            _rubLockIndex = -1;
            _rubShakeReady = false;
            _shownDeal = session.DealSerial;
            _shownReveal = session.RevealPlaySerial;
            var token = ++_dealToken;
            _seeThroughToken++;
            _dealing = true;
            ClearAllItems();

            var seats = CollectDealSeats(session);
            var seq = DOTween.Sequence();
            var delay = AppendShuffle(seq, token);
            var order = 0;
            var maxRound = GameBalance.PlayerCardsDealt;
            for (var round = 0; round < maxRound; round++)
            {
                for (var s = 0; s < seats.Count; s++)
                {
                    var view = seats[s].View;
                    var seat = seats[s].Seat;
                    if (round >= GameBalance.CardsDealt(seat.IsPlayer))
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

                var count = CardCount(view, seat);
                for (var i = 0; i < count; i++)
                {
                    var cardIndex = i;
                    seq.InsertCallback(delay, () =>
                    {
                        if (token != _revealToken)
                        {
                            return;
                        }

                        FlipSeatCard(view, seat, cardIndex);
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
            var settleCount = CardCount(winnerView, winnerSeat);
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

        private void FlipSeatCard(SeatView view, SeatState seat, int cardIndex)
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

            var card = cardIndex < seat.Hand.Length ? seat.Hand[cardIndex] : default;
            if (!item.Card.Equals(card))
            {
                item.SetCard(card);
            }

            ApplyFace(item, CardFaceState.Front, true);
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

            SeatView view = null;
            ForEachActiveEnemy(session, (enemy, slot) =>
            {
                if (view == null && enemy.Id == seat.Id)
                {
                    view = EnemyViewAt(slot);
                }
            });
            return view;
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
                ApplyFace(item, DesiredFace(_session, seat, view.IsPlayer, cardIndex), false);
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
            ForEachActiveEnemy(session, (enemy, slot) =>
            {
                SyncSeatFaces(EnemyViewAt(slot), enemy, false, session);
            });
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

                var card = i < seat.Hand.Length ? seat.Hand[i] : default;
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
            if (session.Enemies == null)
            {
                return;
            }

            for (var i = 0; i < session.Enemies.Length; i++)
            {
                var enemy = session.Enemies[i];
                if (enemy == null || !enemy.ActiveInStage)
                {
                    continue;
                }

                var view = ViewOf(session, enemy);
                LiftSeat(view, enemy, SelectOffset(view));
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
            ForEachActiveEnemy(session, (enemy, slot) =>
            {
                ApplySeatSeeThrough(EnemyViewAt(slot), enemy, false, session, token);
            });
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
                var want = desired == CardFaceState.Back && session.IsSpyRevealed(seat.Id, i);
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
        /// 选中牌朝玩家方向挪开：玩家向上，上方敌人向下，左边敌人向右，右边敌人向左。
        /// </summary>
        private Vector3 SelectOffset(SeatView view)
        {
            if (view == null || view.IsPlayer)
            {
                return Vector3.up * SelectLift;
            }

            if (view == _enemies[0])
            {
                return Vector3.right * SelectLift;
            }

            if (view == _enemies[2])
            {
                return Vector3.left * SelectLift;
            }

            return Vector3.down * SelectLift;
        }

        private void LiftSeat(SeatView view, SeatState seat, Vector3 offset)
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

                // 搓牌拖拽中自己管位置，避免 Sync 把牌拽回原点。
                if (view.IsPlayer && (i == _rubLockIndex || (_session != null && _session.PendingRubIndex == i)))
                {
                    continue;
                }

                var selected = seat.IsCardSelected(i);
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
                item.MoveTo(dest, SelectLiftDuration, Ease.OutQuad).OnComplete(() =>
                {
                    SyncSlotShadow(view, index, seat.IsCardSelected(index));
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

        private static int CardCount(SeatView view, SeatState seat)
        {
            if (view == null)
            {
                return 0;
            }

            return GameBalance.CardsDealt(seat != null && seat.IsPlayer);
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

            if (seat.ShowCards)
            {
                return CardFaceState.Front;
            }

            if (session.CardsRevealed)
            {
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
            for (var slot = 0; slot < _enemies.Length; slot++)
            {
                if (_enemies[slot] == null || _enemies[slot].Node == null)
                {
                    continue;
                }

                _enemies[slot].Node.gameObject.SetActive(false);
            }

            ForEachActiveEnemy(session, (enemy, slot) =>
            {
                if (!enemy.Alive)
                {
                    return;
                }

                var view = EnemyViewAt(slot);
                if (view != null && view.Node != null)
                {
                    view.Node.gameObject.SetActive(true);
                }
            });

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

            ForEachActiveEnemy(session, (enemy, slot) =>
            {
                if (!enemy.Alive)
                {
                    return;
                }

                var view = EnemyViewAt(slot);
                if (view != null)
                {
                    list.Add((view, enemy));
                }
            });

            return list;
        }

        /// <summary>
        /// 上场敌人（含阵亡）按原视觉槽遍历。阵亡仍占位，不能按存活人数压缩，
        /// 否则发牌会落到别人座位，开牌按原槽去翻就翻空。
        /// </summary>
        private void ForEachActiveEnemy(GameSession session, Action<SeatState, int> fn)
        {
            if (session?.Enemies == null || fn == null)
            {
                return;
            }

            var activeCount = CountActive(session);
            var placed = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                var enemy = session.Enemies[i];
                if (enemy == null || !enemy.ActiveInStage)
                {
                    continue;
                }

                var slot = VisualSlot(placed, activeCount);
                placed++;
                fn(enemy, slot);
            }
        }

        private SeatView EnemyViewAt(int slot)
        {
            return slot >= 0 && slot < _enemies.Length ? _enemies[slot] : null;
        }

        private void BindEnemySlots(Transform hud)
        {
            for (var i = 0; i < EnemyNodeNames.Length && i < _enemies.Length; i++)
            {
                _enemies[i] = BuildSeat(FindChild(hud, EnemyNodeNames[i]), false);
            }
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

        private void ClearAllItems()
        {
            _dealPile?.Clear();
            ClearSeatItems(_player);
            for (var i = 0; i < _enemies.Length; i++)
            {
                ClearSeatItems(_enemies[i]);
            }
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

        private static int CountActive(GameSession session)
        {
            var n = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                if (session.Enemies[i].ActiveInStage)
                {
                    n++;
                }
            }

            return n;
        }

        private static int VisualSlot(int enemyIndex, int activeCount)
        {
            if (activeCount <= 1)
            {
                return 1;
            }

            if (activeCount == 2)
            {
                return enemyIndex == 0 ? 0 : 2;
            }

            return enemyIndex;
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
