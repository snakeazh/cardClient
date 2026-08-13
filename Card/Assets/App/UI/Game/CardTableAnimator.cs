using System.Collections.Generic;
using App.Game;
using DG.Tweening;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 从 GameHud.dealpoint 发牌到各 PlayerNode/mineNode 的 carpoint，
    /// 开牌/弃牌/看牌时用 CardItem 播翻面。
    /// </summary>
    public sealed class CardTableAnimator
    {
        private const int CardsPerHand = 3;
        private const string PrefabPath = "Game/icon/CardIcon";
        private const float DealMoveDuration = 0.32f;
        private const float DealStagger = 0.08f;
        private const float FlipDuration = 0.35f;
        private const float RevealFlipDuration = 0.28f;
        private const float RevealCardGap = 0.12f;
        private const float RevealSeatGap = 0.38f;

        private static GameObject _prefab;

        private Transform _hud;
        private Transform _dealPoint;
        private SeatView _player;
        private readonly SeatView[] _enemies = new SeatView[3];
        private Sequence _dealSeq;
        private Sequence _revealSeq;
        private int _shownDeal = -1;
        private int _shownReveal;
        private int _dealToken;
        private int _revealToken;
        private bool _dealing;
        private bool _revealing;
        private GameSession _session;

        public bool IsDealing => _dealing;
        public bool IsBusy => _dealing || _revealing;

        private sealed class SeatView
        {
            public Transform Node;
            public Transform[] Points = new Transform[CardsPerHand];
            public CardItem[] Items = new CardItem[CardsPerHand];
            public bool[] Landed = new bool[CardsPerHand];
            public bool IsPlayer;
        }

        public void Bind(Transform hud)
        {
            _hud = hud;
            _dealPoint = FindChild(hud, "dealpoint") ?? FindChild(hud, "DealPoint") ?? hud;
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
                PlayReveal(session);
                return;
            }

            SyncAllFaces(session);
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

        public void Dispose()
        {
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _dealToken++;
            _revealToken++;
            ClearAllItems();
        }

        private void PlayDeal(GameSession session)
        {
            _dealSeq?.Kill();
            _revealSeq?.Kill();
            _revealing = false;
            _shownDeal = session.DealSerial;
            _shownReveal = session.RevealPlaySerial;
            var token = ++_dealToken;
            _dealing = true;
            ClearAllItems();

            var seats = CollectDealSeats(session);
            var seq = DOTween.Sequence();
            var delay = 0f;
            var order = 0;
            for (var round = 0; round < CardsPerHand; round++)
            {
                for (var s = 0; s < seats.Count; s++)
                {
                    var view = seats[s].View;
                    var seat = seats[s].Seat;
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

            seq.OnComplete(() =>
            {
                if (token != _dealToken)
                {
                    return;
                }

                _dealing = false;
                SyncAllFaces(session);
            });
            _dealSeq = seq;
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

                for (var i = 0; i < CardsPerHand; i++)
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

                    PunchSeat(capturedView, 0.16f);
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
            delay += 0.42f;
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
            item.PunchScale(0.12f, 0.22f);
        }

        private void PunchSeat(SeatView view, float punch)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item != null && view.Landed[i])
                {
                    item.PunchScale(punch, 0.3f);
                }
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
            PunchSeat(view, 0.28f);
            TintSeat(view, new Color(1f, 0.9f, 0.45f));
        }

        private static void TintSeat(SeatView view, Color color)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                var item = view.Items[i];
                if (item == null)
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

            var activeCount = CountActive(session);
            var placed = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                var enemy = session.Enemies[i];
                if (!enemy.ActiveInStage)
                {
                    continue;
                }

                var slot = VisualSlot(placed, activeCount);
                placed++;
                if (enemy.Id == seat.Id)
                {
                    return slot >= 0 && slot < _enemies.Length ? _enemies[slot] : null;
                }
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

        private void SpawnAndFly(SeatView view, SeatState seat, int cardIndex, int order, int token)
        {
            if (view == null || view.Points[cardIndex] == null || seat == null || seat.Hand == null)
            {
                return;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("CardIcon prefab not found at Resources/" + PrefabPath);
                return;
            }

            var point = view.Points[cardIndex];
            var start = _dealPoint != null ? _dealPoint.position : _hud.position;
            var startRot = _dealPoint != null ? _dealPoint.rotation : Quaternion.identity;
            var go = Object.Instantiate(prefab);
            go.name = view.IsPlayer ? "PlayerCard" + (cardIndex + 1) : point.parent.name + "_Card" + (cardIndex + 1);
            HideBackChild(go.transform);

            var item = go.GetComponent<CardItem>();
            if (item == null)
            {
                item = go.AddComponent<CardItem>();
            }

            var card = cardIndex < seat.Hand.Length ? seat.Hand[cardIndex] : default;
            item.Initialize(card, CardFaceState.Back, start, startRot, Vector3.one);
            var sr = item.CurrentRenderer;
            if (sr != null)
            {
                sr.sortingOrder = 20 + order;
                sr.color = Color.white;
            }

            EnsureCollider(item);

            view.Items[cardIndex] = item;
            view.Landed[cardIndex] = false;

            var duration = DealMoveDuration;
            item.transform.DOScale(point.lossyScale, duration).SetEase(Ease.OutQuad);
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

        private void SyncAllFaces(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            SyncSeatFaces(_player, session.Player, true, session);
            var activeCount = CountActive(session);
            var placed = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                var enemy = session.Enemies[i];
                if (!enemy.ActiveInStage)
                {
                    continue;
                }

                var slot = VisualSlot(placed, activeCount);
                placed++;
                if (slot >= 0 && slot < _enemies.Length)
                {
                    SyncSeatFaces(_enemies[slot], enemy, false, session);
                }
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

                var card = i < seat.Hand.Length ? seat.Hand[i] : default;
                if (!item.Card.Equals(card))
                {
                    item.SetCard(card);
                }

                ApplyFace(item, DesiredFace(session, seat, player, i), true);
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
                          session.Phase == GamePhase.RoundSettle))
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

        private static CardFaceState DesiredFace(GameSession session, SeatState seat, bool player, int _)
        {
            if (session == null || seat == null)
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
            var activeCount = CountActive(session);
            for (var slot = 0; slot < _enemies.Length; slot++)
            {
                if (_enemies[slot] == null || _enemies[slot].Node == null)
                {
                    continue;
                }

                _enemies[slot].Node.gameObject.SetActive(false);
            }

            var placed = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                if (!session.Enemies[i].ActiveInStage)
                {
                    continue;
                }

                var slot = VisualSlot(placed, activeCount);
                placed++;
                if (slot >= 0 && slot < _enemies.Length && _enemies[slot] != null && _enemies[slot].Node != null)
                {
                    _enemies[slot].Node.gameObject.SetActive(true);
                }
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

            var activeCount = CountActive(session);
            var placed = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                var enemy = session.Enemies[i];
                if (!enemy.ActiveInStage || !enemy.Alive)
                {
                    continue;
                }

                var slot = VisualSlot(placed, activeCount);
                placed++;
                if (slot >= 0 && slot < _enemies.Length && _enemies[slot] != null)
                {
                    list.Add((_enemies[slot], enemy));
                }
            }

            return list;
        }

        private void BindEnemySlots(Transform hud)
        {
            var slots = new List<Transform>();
            for (var i = 0; i < hud.childCount; i++)
            {
                var child = hud.GetChild(i);
                if (child.name == "bg" || child.name == "mineNode" || child.name == "dealpoint" ||
                    child.name == "DealPoint")
                {
                    continue;
                }

                if (FindChild(child, "cardNode") != null || FindCardPoint(child, 1) != null)
                {
                    slots.Add(child);
                }
            }

            slots.Sort((a, b) =>
            {
                if (Mathf.Abs(a.position.y - b.position.y) > 1.5f)
                {
                    return b.position.y.CompareTo(a.position.y);
                }

                return a.position.x.CompareTo(b.position.x);
            });

            Transform left = null, top = null, right = null;
            if (slots.Count == 1)
            {
                top = slots[0];
            }
            else if (slots.Count == 2)
            {
                left = slots[0].position.x < slots[1].position.x ? slots[0] : slots[1];
                right = left == slots[0] ? slots[1] : slots[0];
            }
            else if (slots.Count >= 3)
            {
                top = slots[0];
                var rest = new List<Transform> { slots[1], slots[2] };
                rest.Sort((a, b) => a.position.x.CompareTo(b.position.x));
                left = rest[0];
                right = rest[1];
            }

            _enemies[0] = BuildSeat(left, false);
            _enemies[1] = BuildSeat(top, false);
            _enemies[2] = BuildSeat(right, false);
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
            ClearSeatItems(_player);
            for (var i = 0; i < _enemies.Length; i++)
            {
                ClearSeatItems(_enemies[i]);
            }
        }

        private static void ClearSeatItems(SeatView view)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 0; i < view.Items.Length; i++)
            {
                if (view.Items[i] != null)
                {
                    Object.Destroy(view.Items[i].gameObject);
                    view.Items[i] = null;
                }

                view.Landed[i] = false;
            }
        }

        private static void SnapToPoint(CardItem item, Transform point)
        {
            var t = item.transform;
            t.SetParent(point, true);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        private static void HideBackChild(Transform root)
        {
            var back = root.Find("Back");
            if (back != null)
            {
                back.gameObject.SetActive(false);
            }
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

        private static GameObject LoadPrefab()
        {
            if (_prefab == null)
            {
                _prefab = UnityEngine.Resources.Load<GameObject>(PrefabPath);
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
