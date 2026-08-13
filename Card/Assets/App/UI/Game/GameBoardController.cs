using System.Collections.Generic;
using App.Game;
using UnityEngine;
using UnityEngine.EventSystems;

namespace App.UI
{
    /// <summary>
    /// 牌桌世界表现（GameHud 上的 Sprite 牌面、搓牌/放大镜输入）。
    /// 不含任何 Canvas HUD 文本/按钮——那些已拆到 <see cref="GameUIView"/>。
    /// </summary>
    public sealed class GameBoardController : MonoBehaviour
    {
        private GameTableViewModel _vm;
        private SpriteRenderer[] _playerCards;
        private TextMesh[] _playerRankLabels;
        private readonly SpriteRenderer[][] _enemyCards = new SpriteRenderer[3][];
        private readonly Transform[] _enemyNodes = new Transform[3];
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
            if (_vm == null)
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
                var peek = HitPlayerCard();
                if (peek >= 0)
                {
                    _vm.Session.PeekMagnifier(peek);
                    return;
                }
            }

            if (_vm.Session.Phase != GamePhase.WaitingRub)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                var index = HitPlayerCard();
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
                if (_playerCards != null && _dragCard < _playerCards.Length && _playerCards[_dragCard] != null)
                {
                    var glow = Mathf.PingPong(Time.time * 6f, 1f);
                    _playerCards[_dragCard].color = Color.Lerp(Color.white, new Color(1f, 0.85f, 0.4f), glow);
                }

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
        }

        private void OnSessionChanged()
        {
            if (_vm == null)
            {
                return;
            }

            RefreshBoard();
        }

        private void BindScene()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                _camera = FindObjectOfType<Camera>();
            }

            var hud = GameObject.Find("GameHud");
            if (hud == null)
            {
                hud = gameObject;
            }

            var mine = FindChild(hud.transform, "mineNode");
            if (mine != null)
            {
                _playerCards = FindCardRenderers(mine);
                _playerRankLabels = EnsureRankLabels(_playerCards);
                for (var i = 0; i < _playerCards.Length; i++)
                {
                    EnsureCollider(_playerCards[i]);
                }
            }

            BindEnemySlots(hud.transform);
        }

        private void BindEnemySlots(Transform hud)
        {
            var slots = new List<Transform>();
            for (var i = 0; i < hud.childCount; i++)
            {
                var child = hud.GetChild(i);
                if (child.name == "bg" || child.name == "mineNode")
                {
                    continue;
                }

                if (FindChild(child, "cardNode") != null || child.GetComponentInChildren<SpriteRenderer>() != null)
                {
                    slots.Add(child);
                }
            }

            slots.Sort((a, b) =>
            {
                var ax = a.position.x;
                var bx = b.position.x;
                var ay = a.position.y;
                var by = b.position.y;
                if (Mathf.Abs(ay - by) > 1.5f)
                {
                    return by.CompareTo(ay);
                }

                return ax.CompareTo(bx);
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

            _enemyNodes[0] = left;
            _enemyNodes[1] = top;
            _enemyNodes[2] = right;
            _enemyCards[0] = FindCardRenderers(left);
            _enemyCards[1] = FindCardRenderers(top);
            _enemyCards[2] = FindCardRenderers(right);
        }

        private void RefreshBoard()
        {
            var session = _vm.Session;
            RefreshSeatCards(_playerCards, _playerRankLabels, session.Player, true);

            var activeCount = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                if (session.Enemies[i].ActiveInStage)
                {
                    activeCount++;
                }
            }

            for (var slot = 0; slot < 3; slot++)
            {
                if (_enemyNodes[slot] != null)
                {
                    _enemyNodes[slot].gameObject.SetActive(false);
                }

                SetActiveCards(_enemyCards[slot], false);
            }

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
                SetActiveCards(_enemyCards[slot], true);
                RefreshSeatCards(_enemyCards[slot], null, enemy, false);
                if (_enemyNodes[slot] != null)
                {
                    _enemyNodes[slot].gameObject.SetActive(true);
                }
            }
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

        private void RefreshSeatCards(SpriteRenderer[] renders, TextMesh[] labels, SeatState seat, bool player)
        {
            if (renders == null)
            {
                return;
            }

            var reveal = _vm.Session.CardsRevealed || (player && seat.Looked);
            for (var i = 0; i < renders.Length; i++)
            {
                var sr = renders[i];
                if (sr == null)
                {
                    continue;
                }

                var card = seat.Hand != null && i < seat.Hand.Length ? seat.Hand[i] : default;
                var revealThis = reveal || (player && i < _vm.Session.Run.RubbedReveal.Length &&
                                           _vm.Session.Run.RubbedReveal[i]);
                if (revealThis)
                {
                    sr.sprite = CardSpriteLibrary.GetFace(card);
                    sr.color = Color.white;
                    sr.sortingOrder = 2;
                }
                else
                {
                    sr.sprite = CardSpriteLibrary.Back;
                    sr.color = Color.white;
                    sr.sortingOrder = 1;
                    if (player && _vm.Session.Run.MagnifierThisRound && _vm.Session.Run.PeekSuitUsed &&
                        _vm.Session.Run.PeekSuitIndex == i)
                    {
                        sr.color = SuitTint(card.Suit);
                    }
                }

                if (player && _vm.Session.Phase == GamePhase.WaitingRub)
                {
                    sr.color = Color.Lerp(sr.color, new Color(1f, 0.92f, 0.65f), 0.25f);
                }

                if (labels != null && i < labels.Length && labels[i] != null)
                {
                    labels[i].gameObject.SetActive(false);
                }
            }
        }

        private int HitPlayerCard()
        {
            if (_camera == null || _playerCards == null)
            {
                return -1;
            }

            var mouse = Input.mousePosition;
            mouse.z = Mathf.Abs(_camera.transform.position.z);
            var world = _camera.ScreenToWorldPoint(mouse);
            var hit = Physics2D.Raycast(world, Vector2.zero);
            if (hit.collider == null)
            {
                return -1;
            }

            for (var i = 0; i < _playerCards.Length; i++)
            {
                if (_playerCards[i] != null && hit.collider.gameObject == _playerCards[i].gameObject)
                {
                    return i;
                }
            }

            return -1;
        }

        private static SpriteRenderer[] FindCardRenderers(Transform root)
        {
            if (root == null)
            {
                return new SpriteRenderer[0];
            }

            var cardNode = FindChild(root, "cardNode") ?? root;
            var list = new List<SpriteRenderer>();
            for (var n = 1; n <= 3; n++)
            {
                var square = FindChild(cardNode, "Square" + n);
                if (square != null)
                {
                    var sr = square.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        list.Add(sr);
                    }
                }
            }

            if (list.Count == 0)
            {
                list.AddRange(cardNode.GetComponentsInChildren<SpriteRenderer>(true));
            }

            return list.ToArray();
        }

        private TextMesh[] EnsureRankLabels(SpriteRenderer[] cards)
        {
            if (cards == null)
            {
                return new TextMesh[0];
            }

            var labels = new TextMesh[cards.Length];
            for (var i = 0; i < cards.Length; i++)
            {
                if (cards[i] == null)
                {
                    continue;
                }

                var child = cards[i].transform.Find("RankLabel");
                if (child == null)
                {
                    var go = new GameObject("RankLabel");
                    go.transform.SetParent(cards[i].transform, false);
                    go.transform.localPosition = new Vector3(0f, 0.55f, -0.1f);
                    go.transform.localScale = new Vector3(0.12f, 0.08f, 1f);
                    child = go.transform;
                }

                var tm = child.GetComponent<TextMesh>();
                if (tm == null)
                {
                    tm = child.gameObject.AddComponent<TextMesh>();
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    tm.fontSize = 64;
                    tm.characterSize = 0.5f;
                    tm.color = Color.black;
                }

                tm.gameObject.SetActive(false);
                labels[i] = tm;
            }

            return labels;
        }

        private static void EnsureCollider(SpriteRenderer sr)
        {
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

        private static void SetActiveCards(SpriteRenderer[] renders, bool active)
        {
            if (renders == null)
            {
                return;
            }

            for (var i = 0; i < renders.Length; i++)
            {
                if (renders[i] != null)
                {
                    renders[i].gameObject.SetActive(active);
                }
            }
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
