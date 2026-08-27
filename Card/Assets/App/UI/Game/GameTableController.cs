using System.Collections.Generic;
using App.Game;
using Framework.Log;
using Framework.UI.Binding;
using Framework.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Binds SampleScene GameHud / GameUI to the match session and builds action HUD at runtime.
    /// </summary>
    public sealed class GameTableController : MonoBehaviour
    {
        private const int CardsPerHand = 3;
        private const float PlayerCardScale = 1f;
        private const float AiCardScale = 0.6f;
        private const float PlayerCardSpacing = 1.2f;
        private const float AiCardSpacing = 0.8f;
        private const string CardIconResourcePath = "Game/icon/CardIcon";
        private static GameObject _cardIconPrefab;

        private GameTableViewModel _vm;
        private readonly CardTableAnimator _cards = new CardTableAnimator();
        private readonly TMP_Text[] _enemyInfos = new TMP_Text[3];
        private TMP_Text _chipText;
        private TMP_Text _betText;
        private TMP_Text _stateText;
        private Text _hint;
        private Text _title;
        private Text _gold;
        private Text _pot;
        private Text _log;
        private GameObject _actionBar;
        private GameObject _continueBar;
        private GameObject _shopPanel;
        private GameObject _failPanel;
        private Transform _shopContent;
        private Camera _camera;
        private int _dragCard = -1;
        private float _rubAcc;
        private bool _bound;
        private Font _font;

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
                BuildHud();
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

            enabled = false;
        }

        private void Update()
        {
            if (_vm == null || _cards.IsDealing)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
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

            _vm.Refresh();
            RefreshBoard();
            RefreshShop();
        }

        private void BindScene()
        {
            _font = ResolveFont();
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

            _cards.Bind(hud.transform, _vm.Resources);

            var playerInfo = GameObject.Find("PlayerInfo");
            if (playerInfo != null)
            {
                var rt = playerInfo.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchoredPosition = new Vector2(0f, -720f);
                }

                _chipText = FindUiText(playerInfo.transform, "chip");
                _betText = FindUiText(playerInfo.transform, "Text (2)");
                _stateText = FindUiText(playerInfo.transform, "state");
                if (_chipText != null) { _chipText.raycastTarget = false; }
                if (_betText != null) { _betText.raycastTarget = false; }
                if (_stateText != null) { _stateText.raycastTarget = false; }
            }

            WireEnemyInfo("player1", 0, new Vector2(-360f, 80f));
            WireEnemyInfo("player2", 1, new Vector2(0f, 760f));
            WireEnemyInfo("player3", 2, new Vector2(360f, 80f));
        }

        private void BuildHud()
        {
            _font = ResolveFont();
            var canvas = GameObject.Find("Canvas");
            var root = canvas != null ? canvas.transform.Find("GameUI") : null;
            if (root == null && canvas != null)
            {
                root = canvas.transform;
            }

            if (root == null)
            {
                return;
            }

            _title = CreateText(root, "HudTitle", new Vector2(0f, 880f), new Vector2(900f, 60f), 36, TextAnchor.MiddleCenter);
            _gold = CreateText(root, "HudGold", new Vector2(-360f, 820f), new Vector2(320f, 48f), 28, TextAnchor.MiddleLeft);
            _pot = CreateText(root, "HudPot", new Vector2(360f, 820f), new Vector2(320f, 48f), 28, TextAnchor.MiddleRight);
            _hint = CreateText(root, "HudHint", new Vector2(0f, -620f), new Vector2(980f, 100f), 26, TextAnchor.MiddleCenter);
            _log = CreateText(root, "HudLog", new Vector2(0f, 560f), new Vector2(980f, 160f), 20, TextAnchor.UpperCenter);
            _log.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            _actionBar = CreatePanel(root, "ActionBar", new Vector2(0f, -820f), new Vector2(1000f, 220f));
            CreateButton(_actionBar.transform, "闷注", new Vector2(-380f, 50f), _vm.BlindBetCommand, 170f);
            CreateButton(_actionBar.transform, "看牌", new Vector2(-190f, 50f), _vm.LookCommand, 170f);
            CreateButton(_actionBar.transform, "x2下注", new Vector2(0f, 50f), _vm.RaiseCommand, 170f);
            CreateButton(_actionBar.transform, "x4下注", new Vector2(190f, 50f), _vm.RaiseHighCommand, 170f);
            CreateButton(_actionBar.transform, "开牌", new Vector2(380f, 50f), _vm.OpenCommand, 160f);
            CreateButton(_actionBar.transform, "弃牌", new Vector2(380f, -50f), _vm.FoldCommand, 160f);
            CreateButton(_actionBar.transform, "-", new Vector2(-120f, -50f), _vm.MinusBetCommand, 90f);
            CreateButton(_actionBar.transform, "+", new Vector2(120f, -50f), _vm.PlusBetCommand, 90f);

            _continueBar = CreatePanel(root, "ContinueBar", new Vector2(0f, -820f), new Vector2(700f, 120f));
            CreateButton(_continueBar.transform, "下一局 / 进入商店", new Vector2(0f, 0f), _vm.ContinueCommand, 420f);

            _failPanel = CreatePanel(root, "FailPanel", new Vector2(0f, -200f), new Vector2(700f, 360f));
            CreateButton(_failPanel.transform, "看广告借贷", new Vector2(0f, 80f), _vm.LoanCommand, 360f);
            CreateButton(_failPanel.transform, "看广告复活", new Vector2(0f, 0f), _vm.ReviveCommand, 360f);
            CreateButton(_failPanel.transform, "重开本关", new Vector2(0f, -80f), _vm.RestartCommand, 360f);

            _shopPanel = CreatePanel(root, "ShopPanel", new Vector2(0f, 40f), new Vector2(980f, 1200f));
            CreateText(_shopPanel.transform, "ShopTitle", new Vector2(0f, 540f), new Vector2(900f, 50f), 34, TextAnchor.MiddleCenter).text = "通关商店";
            CreateButton(_shopPanel.transform, "广告+1搓牌", new Vector2(-220f, 470f), _vm.ExtraRubAdCommand, 280f);
            CreateButton(_shopPanel.transform, "广告双倍金币", new Vector2(220f, 470f), _vm.DoubleGoldAdCommand, 280f);
            CreateButton(_shopPanel.transform, "离开商店", new Vector2(0f, -540f), _vm.LeaveShopCommand, 280f);
            _shopContent = new GameObject("ShopItems", typeof(RectTransform)).transform;
            _shopContent.SetParent(_shopPanel.transform, false);
            var contentRt = (RectTransform)_shopContent;
            contentRt.anchorMin = contentRt.anchorMax = contentRt.pivot = new Vector2(0.5f, 0.5f);
            contentRt.anchoredPosition = new Vector2(0f, -20f);
            contentRt.sizeDelta = new Vector2(920f, 900f);

            var binding = new BindingContext();
            binding.BindText(_title, _vm.Title);
            binding.BindText(_gold, _vm.GoldText);
            binding.BindText(_pot, _vm.PotText);
            binding.BindText(_hint, _vm.Hint);
            binding.BindText(_log, _vm.LogText);
            binding.BindActive(_actionBar, _vm.ShowActionBar);
            binding.BindActive(_continueBar, _vm.ShowContinue);
            binding.BindActive(_shopPanel, _vm.ShowShop);
            binding.BindActive(_failPanel, _vm.ShowFail);
        }

        private void RefreshBoard()
        {
            var session = _vm.Session;
            if (_chipText != null)
            {
                _chipText.text = _vm.PlayerChips.Value;
            }

            if (_betText != null)
            {
                _betText.text = _vm.PlayerBet.Value;
            }

            if (_stateText != null)
            {
                _stateText.gameObject.SetActive(false);
            }

            _cards.Sync(session);

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
                if (_enemyInfos[slot] != null)
                {
                    _enemyInfos[slot].gameObject.SetActive(false);
                }
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
                if (_enemyInfos[slot] != null)
                {
                    _enemyInfos[slot].gameObject.SetActive(true);
                    var banner = string.IsNullOrEmpty(enemy.Banner) ? enemy.Status : enemy.Banner;
                    _enemyInfos[slot].text = $"{enemy.Name}\nHP {enemy.Hp}  勇气 {enemy.Courage}\n{banner}";
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

            var reveal = seat.ShowCards ||
                         _vm.Session.CardsRevealed ||
                         (player && seat.Looked);
            for (var i = 0; i < renders.Length; i++)
            {
                var sr = renders[i];
                if (sr == null)
                {
                    continue;
                }

                var card = seat.Hand != null && i < seat.Hand.Length ? seat.Hand[i] : default;
                var revealThis = reveal;
                if (revealThis && !player && seat.CountSelectedCards() > 0 && !seat.IsCardSelected(i))
                {
                    revealThis = false;
                }

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

        private void RefreshShop()
        {
            if (_shopContent == null)
            {
                return;
            }

            for (var i = _shopContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_shopContent.GetChild(i).gameObject);
            }
        }

        private void WireEnemyInfo(string objectName, int index, Vector2 pos)
        {
            var go = GameObject.Find(objectName);
            if (go == null)
            {
                return;
            }

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(280f, 160f);
            }

            _enemyInfos[index] = go.GetComponent<TMP_Text>() ?? go.GetComponentInChildren<TMP_Text>(true);
        }

        private static SpriteRenderer[] FindCardRenderers(Transform root, float scale, float spacing)
        {
            if (root == null)
            {
                return new SpriteRenderer[0];
            }

            var cardNode = FindChild(root, "cardNode") ?? root;
            EnsureCardIcons(cardNode, scale, spacing);

            var list = new List<SpriteRenderer>();
            for (var n = 1; n <= CardsPerHand; n++)
            {
                var named = FindChild(cardNode, "CardIcon" + n) ?? FindChild(cardNode, "Square" + n);
                if (named != null)
                {
                    var sr = named.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        list.Add(sr);
                    }
                }
            }

            if (list.Count == 0)
            {
                list.AddRange(cardNode.GetComponentsInChildren<SpriteRenderer>(true));
                if (list.Count > CardsPerHand)
                {
                    list.RemoveRange(CardsPerHand, list.Count - CardsPerHand);
                }
            }

            return list.ToArray();
        }

        private static void EnsureCardIcons(Transform cardNode, float scale, float spacing)
        {
            if (cardNode == null)
            {
                return;
            }

            var existing = CountCardSprites(cardNode);
            if (existing >= CardsPerHand)
            {
                ApplyCardLayout(cardNode, scale, spacing);
                return;
            }

            var prefab = LoadCardIconPrefab();
            if (prefab == null)
            {
                AppLog.Warn(LogChannel.UI, "CardIcon prefab not found at Resources/" + CardIconResourcePath);
                return;
            }

            for (var i = existing; i < CardsPerHand; i++)
            {
                var go = UnityEngine.Object.Instantiate(prefab, cardNode, false);
                go.name = "CardIcon" + (i + 1);
            }

            ApplyCardLayout(cardNode, scale, spacing);
        }

        private static void ApplyCardLayout(Transform cardNode, float scale, float spacing)
        {
            var prefab = LoadCardIconPrefab();
            var baseScale = prefab != null ? prefab.transform.localScale : new Vector3(1f, 1.5f, 1f);
            var baseRotation = prefab != null ? prefab.transform.localRotation : Quaternion.identity;
            var index = 0;
            for (var i = 0; i < cardNode.childCount; i++)
            {
                var child = cardNode.GetChild(i);
                if (child.GetComponent<SpriteRenderer>() == null)
                {
                    continue;
                }

                child.localRotation = baseRotation;
                child.localScale = baseScale * scale;
                child.localPosition = new Vector3((index - 1) * spacing, 0f, 0f);
                index++;
                if (index >= CardsPerHand)
                {
                    break;
                }
            }
        }

        private static int CountCardSprites(Transform cardNode)
        {
            var count = 0;
            for (var i = 0; i < cardNode.childCount; i++)
            {
                if (cardNode.GetChild(i).GetComponent<SpriteRenderer>() != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static GameObject LoadCardIconPrefab()
        {
            if (_cardIconPrefab == null)
            {
                _cardIconPrefab = UnityEngine.Resources.Load<GameObject>(CardIconResourcePath);
            }

            return _cardIconPrefab;
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
                    if (_font != null)
                    {
                        tm.font = _font;
                    }
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

        private static TMP_Text FindUiText(Transform root, string name)
        {
            var child = FindChild(root, name);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private Text CreateText(Transform parent, string name, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private GameObject CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.95f, 0.93f, 0.88f, 0.92f);
            return go;
        }

        private Button CreateButton(Transform parent, string label, Vector2 pos, IRelayCommand command, float width = 200f)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 56f);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.25f, 0.45f, 0.85f, 1f);
            var button = go.GetComponent<Button>();
            var text = CreateText(go.transform, "Label", Vector2.zero, new Vector2(width - 8f, 50f), 24, TextAnchor.MiddleCenter);
            text.color = Color.white;
            text.text = label;
            if (command != null)
            {
                var binding = new BindingContext();
                binding.BindCommand(button, command);
            }

            return button;
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

        private static Font ResolveFont()
        {
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
            if (font != null)
            {
                return font;
            }

            return UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                   ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
