using System;
using System.Collections;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Resources;
using DG.Tweening;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 对局 HUD。player1/2/3 对应 GameHud 的 PlayerNode1/2/3。
    /// </summary>
    [AutoScreen(AppScreenIds.GameUI, UILayer.Page, ResResourcePaths.GameUI)]
    public sealed class GameUIView : ViewBase<GameTableViewModel>
    {
        private static readonly string[] EnemySlotKeys = { "player1", "player2", "player3" };

        private GameObject _gameHud;
        private GameBoardController _board;
        private Transform _btns;
        private Transform _shopContent;
        private readonly GameObject[] _enemyInfos = new GameObject[3];
        private readonly PlayerItem[] _enemyItems = new PlayerItem[3];
        private readonly Sprite[] _enemyPortraits = new Sprite[3];
        private readonly AttackCutscene _attackFx = new AttackCutscene();
        private PlayerItem _playerItem;
        private Sprite _playerPortrait;
        private int _playedAttack;
        private RectTransform _hpTextRt;
        private Vector2 _hpTextHome;
        private Coroutine _aiDelay;

        protected override void OnBind()
        {
            BindPlayerInfo();
            SpawnEnemyInfos();
            BindPhaseButtons();
            EnsureHint();
            BindAttackHud();
            BindAttackFx();
            ViewModel.Refresh();
            RefreshPlayerItems();
        }

        protected override async Task OnViewOpen()
        {
            var prefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.GameHud);
            _gameHud = Instantiate(prefab);
            _gameHud.name = "GameHud";

            _board = _gameHud.GetComponent<GameBoardController>();
            if (_board == null)
            {
                _board = _gameHud.AddComponent<GameBoardController>();
            }

            _board.Attach(ViewModel);
            await LoadPortraits();
            ViewModel.Session.Changed += OnSessionChanged;
        }

        protected override Task OnViewClose()
        {
            if (ViewModel != null)
            {
                ViewModel.Session.Changed -= OnSessionChanged;
            }

            StopAiDelay();

            if (_board != null)
            {
                _board.Detach();
                _board = null;
            }

            _attackFx.Dispose();
            RestoreHpText();
            if (ViewModel != null)
            {
                ViewModel.ShowMask.Value = false;
                ViewModel.ShowHpText.Value = false;
            }

            if (_gameHud != null)
            {
                Destroy(_gameHud);
                _gameHud = null;
            }

            return Task.CompletedTask;
        }

        private void OnSessionChanged()
        {
            RefreshShop();
            RefreshPlayerItems();
            TryPlayAttack();
            TryScheduleAiDelay();
        }

        private void TryScheduleAiDelay()
        {
            StopAiDelay();
            if (ViewModel == null || !ViewModel.Session.AiActing)
            {
                return;
            }

            _aiDelay = StartCoroutine(CoAdvanceAi());
        }

        private IEnumerator CoAdvanceAi()
        {
            yield return new WaitForSeconds(GameSession.AiActionDelay);
            _aiDelay = null;
            if (ViewModel != null && ViewModel.Session.AiActing)
            {
                ViewModel.Session.AdvanceAiAction();
            }
        }

        private void StopAiDelay()
        {
            if (_aiDelay != null)
            {
                StopCoroutine(_aiDelay);
                _aiDelay = null;
            }
        }

        private void BindAttackHud()
        {
            var mask = ResolveSlot("mask");
            if (mask != null)
            {
                Binding.BindActive(mask.gameObject, ViewModel.ShowMask);
            }

            var hp = ResolveSlot("hptext");
            if (hp == null)
            {
                return;
            }

            _hpTextRt = hp as RectTransform ?? hp.GetComponent<RectTransform>();
            if (_hpTextRt != null)
            {
                _hpTextHome = _hpTextRt.anchoredPosition;
            }

            var hpText = hp.GetComponent<TMP_Text>();
            if (hpText != null)
            {
                Binding.BindText(hpText, ViewModel.HpText);
            }

            Binding.BindActive(hp.gameObject, ViewModel.ShowHpText);
        }

        private void BindAttackFx()
        {
            var playerRoot = _playerItem != null ? _playerItem.transform : ResolveSlot("PlayerItem");
            _attackFx.Bind(transform, playerRoot, _enemyInfos);
            _playedAttack = ViewModel != null ? ViewModel.Session.AttackPlaySerial : 0;
        }

        private void TryPlayAttack()
        {
            if (ViewModel == null)
            {
                return;
            }

            var session = ViewModel.Session;
            if (session.AttackPlaySerial <= 0 || session.AttackPlaySerial == _playedAttack)
            {
                return;
            }

            _playedAttack = session.AttackPlaySerial;
            var mask = ResolveSlot("mask");
            if (mask != null)
            {
                mask.SetAsLastSibling();
            }

            ViewModel.ShowMask.Value = true;
            ViewModel.ShowHpText.Value = false;
            _attackFx.Play(session.AttackVisualSlot,
                () =>
                {
                    ViewModel.HpText.Value = $"-{Math.Max(1, session.AttackDamage)}";
                    ViewModel.ShowHpText.Value = true;
                    PlaceHpAtTarget(session.AttackVisualSlot);
                },
                () => { ViewModel.ShowMask.Value = false; },
                () =>
                {
                    ViewModel.ShowHpText.Value = false;
                    RestoreHpText();
                    if (ViewModel != null)
                    {
                        ViewModel.Session.CompletePlayerAttack();
                    }
                });
        }

        private void PlaceHpAtTarget(int slot)
        {
            if (_hpTextRt == null)
            {
                return;
            }

            var hit = _attackFx.HitPosition(slot);
            if (hit == Vector3.zero)
            {
                return;
            }

            _hpTextRt.SetAsLastSibling();
            _hpTextRt.position = hit;
            _hpTextRt.DOKill();
            _hpTextRt.localScale = Vector3.one * 0.6f;
            _hpTextRt.DOScale(1f, 0.18f).SetEase(Ease.OutBack);
        }

        private void RestoreHpText()
        {
            if (_hpTextRt == null)
            {
                return;
            }

            _hpTextRt.DOKill();
            _hpTextRt.anchoredPosition = _hpTextHome;
            _hpTextRt.localScale = Vector3.one;
        }

        private void BindPlayerInfo()
        {
            _playerItem = ResolvePlayerItem();
            if (_playerItem != null)
            {
                BindSeatClick(_playerItem.gameObject, ViewModel.XRayPlayerCommand);
            }

            BindRoundInfo();
            RefreshPlayerItems();
        }

        private void BindRoundInfo()
        {
            var roundInfo = ResolveSlot("roundInfo") ?? transform.Find("roundInfo");
            if (roundInfo == null)
            {
                return;
            }

            var text = roundInfo.GetComponentInChildren<TMP_Text>(true);
            Binding.BindText(text, ViewModel.RoundInfo);
            Binding.BindActive(roundInfo.gameObject, ViewModel.ShowTableButtons);
        }

        private PlayerItem ResolvePlayerItem()
        {
            var slot = ResolveSlot("PlayerItem") ?? transform.Find("PlayerItem");
            if (slot == null)
            {
                return null;
            }

            var item = slot.GetComponent<PlayerItem>();
            if (item == null)
            {
                item = slot.gameObject.AddComponent<PlayerItem>();
            }

            return item;
        }

        private void SpawnEnemyInfos()
        {
            var template = _playerItem != null ? _playerItem.transform : ResolveSlot("PlayerItem");
            if (template == null)
            {
                return;
            }

            var templateScale = template.localScale;
            for (var i = 0; i < EnemySlotKeys.Length; i++)
            {
                var slot = ResolveSlot(EnemySlotKeys[i]);
                if (slot == null)
                {
                    continue;
                }

                var clone = Instantiate(template.gameObject, slot);
                clone.name = "PlayerItem";
                clone.SetActive(true);
                var bind = clone.GetComponent<Framework.UI.Binding.UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                var rt = clone.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = templateScale;
                }

                var item = clone.GetComponent<PlayerItem>() ?? clone.AddComponent<PlayerItem>();
                item.ApplyTheme(true);
                item.SetAttack(0);
                Binding.BindActive(clone, ViewModel.ShowEnemy[i]);
                BindSeatClick(clone, ViewModel.AttackCommands[i]);
                _enemyItems[i] = item;
                _enemyInfos[i] = clone;
            }
        }

        private void RefreshPlayerItems()
        {
            if (ViewModel == null)
            {
                return;
            }

            var session = ViewModel.Session;
            if (_playerItem != null)
            {
                _playerItem.Bind(session.Player, _playerPortrait, PlayerAttackValue(session));
            }

            var activeCount = 0;
            for (var i = 0; i < session.Enemies.Length; i++)
            {
                if (session.Enemies[i].ActiveInStage)
                {
                    activeCount++;
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
                if (slot < 0 || slot >= _enemyItems.Length || _enemyItems[slot] == null)
                {
                    continue;
                }

                _enemyItems[slot].Bind(enemy, _enemyPortraits[i], 0, session.ActingAiId);
            }
        }

        private static int PlayerAttackValue(GameSession session)
        {
            if (session == null)
            {
                return 0;
            }

            if (session.Phase == GamePhase.WaitingAttack || session.AttackPlaying)
            {
                return Math.Max(0, session.PendingAttackDamage > 0
                    ? session.PendingAttackDamage
                    : session.AttackDamage);
            }

            return 0;
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

        private async Task LoadPortraits()
        {
            if (ViewModel == null)
            {
                return;
            }

            _playerPortrait = await LoadSprite(ResolvePlayerPortraitKey());
            for (var i = 0; i < _enemyPortraits.Length; i++)
            {
                _enemyPortraits[i] = await LoadSprite(ResResourcePaths.EnemyAttack(i + 1));
            }
        }

        private string ResolvePlayerPortraitKey()
        {
            var heroId = ViewModel.Progress != null ? ViewModel.Progress.LastHeroId : 0;
            var hero = HeroConfig.Get(heroId);
            if (hero == null)
            {
                hero = HeroConfig.Get(LevelUIViewModel.GetDefaultHeroId());
            }

            return ResResourcePaths.RoleIcon(hero != null ? hero.Icon : null);
        }

        private async Task<Sprite> LoadSprite(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                return await ViewModel.Resources.LoadAsync<Sprite>(key);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load portrait '{key}': {ex.Message}");
                return null;
            }
        }

        private void BindSeatClick(GameObject target, IRelayCommand command)
        {
            if (target == null || command == null)
            {
                return;
            }

            var image = target.GetComponent<Image>();
            if (image == null)
            {
                image = target.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.01f);
            }

            var button = target.GetComponent<Button>();
            if (button == null)
            {
                button = target.AddComponent<Button>();
            }

            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            Binding.BindCommand(button, command);
        }

        private void BindPhaseButtons()
        {
            _btns = transform.Find("horBtns") ?? transform.Find("btns");
            if (_btns == null)
            {
                return;
            }

            Binding.BindActive(_btns.gameObject, ViewModel.ShowTableButtons);
            BindDealHidden("horBtns2");
            BindDealHidden("horEquipBtns2");
            BindDealHidden("roundInfo");

            BindBtn("BlindBtn", ViewModel.BlindBetCommand, ViewModel.ShowBlind);
            BindBtn("LookBtn", ViewModel.LookCommand, ViewModel.ShowLook);
            BindBtn("RaiseBtn", ViewModel.RaiseCommand, ViewModel.ShowActions);
            BindBtn("FoldBtn", ViewModel.FoldCommand, ViewModel.ShowFold);
            BindBtn("CompareBtn", ViewModel.OpenCommand, ViewModel.ShowCompare);
            BindBtn("AllInBtn", ViewModel.AllInCommand, ViewModel.ShowAllIn);
            BindBtn("PeekGood", ViewModel.PeekGoodCommand);
            BindBtn("ChaKanGood", ViewModel.ChaKanGoodCommand);
            BindBtn("TiHuanGood", ViewModel.TiHuanGoodCommand);
            BindBtn("PeekBtn", ViewModel.RubCommand, ViewModel.ShowRub);
            BindBtn("CancelBtn", ViewModel.SkipRubCommand, ViewModel.ShowCancel);
            BindBtn("NextRoundBtn", ViewModel.ContinueCommand, ViewModel.ShowContinue);
            SetBtnLabel("CompareBtn", "比牌");
            SetBtnLabel("PeekBtn", "搓牌");
            SetBtnLabel("LookBtn", "看牌");
            SetBtnLabel("CancelBtn", "取消");
            SetBtnLabel("NextRoundBtn", "下一局");
            SetBtnLabel("FoldBtn", "弃牌");
            BindBlindLabel();
            BindBtnLabel("RaiseBtn", ViewModel.RaiseLabel);
            BindBtnLabel("AllInBtn", ViewModel.AllInLabel);

            var template = FindBtn("BlindBtn");
            if (template == null)
            {
                return;
            }

            EnsureBtn(template, "RaiseHighBtn", "x4下注", ViewModel.RaiseHighCommand, ViewModel.ShowActions);
            BindBtnLabel("RaiseHighBtn", ViewModel.RaiseHighLabel);
            BindBtnLabel("PeekGood", ViewModel.PeekGoodLabel);
            BindBtnLabel("ChaKanGood", ViewModel.ChaKanGoodLabel);
            BindBtnLabel("TiHuanGood", ViewModel.TiHuanGoodLabel);
            OrderActionButtons();
            EnsureBtn(template, "ExtraRubBtn", "广告+1搓牌", ViewModel.ExtraRubAdCommand, ViewModel.ShowShop);
            EnsureBtn(template, "DoubleGoldBtn", "广告双倍金币", ViewModel.DoubleGoldAdCommand, ViewModel.ShowShop);
            EnsureBtn(template, "LeaveShopBtn", "离开商店", ViewModel.LeaveShopCommand, ViewModel.ShowShop);
        }

        private void BindDealHidden(string name)
        {
            var node = ResolveSlot(name) ?? transform.Find(name);
            if (node != null)
            {
                Binding.BindActive(node.gameObject, ViewModel.ShowTableButtons);
            }
        }

        private void BindBtn(string name, IRelayCommand command)
        {
            var button = FindBtn(name);
            if (button == null)
            {
                return;
            }

            button.gameObject.SetActive(true);
            Binding.BindCommand(button, command);
        }

        private void BindBtn(string name, IRelayCommand command, ObservableProperty<bool> visible)
        {
            var button = FindBtn(name);
            if (button == null)
            {
                return;
            }

            Binding.BindCommand(button, command);
            Binding.BindActive(button.gameObject, visible);
        }

        private void EnsureBtn(Button template, string name, string label, IRelayCommand command,
            ObservableProperty<bool> visible)
        {
            var existing = _btns.Find(name);
            Button button;
            if (existing != null)
            {
                button = existing.GetComponent<Button>();
            }
            else
            {
                var go = Instantiate(template.gameObject, _btns);
                go.name = name;
                var bind = go.GetComponent<Framework.UI.Binding.UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                var text = go.GetComponentInChildren<TMP_Text>(true);
                if (text != null)
                {
                    text.text = label;
                }

                button = go.GetComponent<Button>();
            }

            if (button == null)
            {
                return;
            }

            Binding.BindCommand(button, command);
            Binding.BindActive(button.gameObject, visible);
        }

        private void OrderActionButtons()
        {
            if (_btns == null)
            {
                return;
            }

            var names = new[]
            {
                "BlindBtn", "LookBtn", "RaiseBtn", "RaiseHighBtn", "AllInBtn",
                "FoldBtn", "CompareBtn", "PeekBtn", "CancelBtn", "NextRoundBtn"
            };
            for (var i = 0; i < names.Length; i++)
            {
                var node = _btns.Find(names[i]);
                if (node != null)
                {
                    node.SetSiblingIndex(i);
                }
            }
        }

        private void RefreshShop()
        {
            if (ViewModel == null || ViewModel.Session.Phase != GamePhase.Shop)
            {
                if (_shopContent != null)
                {
                    _shopContent.gameObject.SetActive(false);
                }

                return;
            }

            if (_shopContent == null)
            {
                var go = new GameObject("ShopItems", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 80f);
                rt.sizeDelta = new Vector2(920f, 900f);
                _shopContent = go.transform;
            }

            _shopContent.gameObject.SetActive(true);
            for (var i = _shopContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_shopContent.GetChild(i).gameObject);
            }

            var template = FindBtn("BlindBtn");
            var catalog = GameBalance.Catalog;
            for (var i = 0; i < catalog.Count; i++)
            {
                var item = catalog[i];
                GameObject go;
                if (template != null)
                {
                    go = Instantiate(template.gameObject, _shopContent);
                    var bind = go.GetComponent<Framework.UI.Binding.UIBind>();
                    if (bind != null)
                    {
                        Destroy(bind);
                    }
                }
                else
                {
                    go = new GameObject(item.Id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                        typeof(Button));
                    go.transform.SetParent(_shopContent, false);
                }

                go.name = item.Id;
                go.SetActive(true);
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0f, 360f - i * 72f);
                    rt.sizeDelta = new Vector2(860f, 64f);
                }

                var label = go.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = $"{item.Name}  {item.Price}金  {item.Effect}";
                    label.fontSize = 22;
                }

                var button = go.GetComponent<Button>();
                var id = item.Id;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => ViewModel.Session.Buy(id));
                    button.interactable = true;
                }
            }
        }

        private void EnsureHint()
        {
            var existing = transform.Find("duelHint");
            TMP_Text text;
            if (existing != null)
            {
                text = existing.GetComponent<TMP_Text>();
            }
            else
            {
                var go = new GameObject("duelHint", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                go.transform.SetParent(transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 210f);
                rt.sizeDelta = new Vector2(980f, 120f);
                text = go.GetComponent<TextMeshProUGUI>();
                var sample = transform.Find("roundInfo")?.GetComponent<TMP_Text>() ??
                             GetComponentInChildren<TMP_Text>(true);
                if (sample != null)
                {
                    text.font = sample.font;
                }

                text.fontSize = 36;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(1f, 0.93f, 0.55f, 1f);
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
            }

            if (text != null)
            {
                Binding.BindText(text, ViewModel.Hint);
            }
        }

        private void BindBlindLabel()
        {
            BindBtnLabel("BlindBtn", ViewModel.BlindLabel);
        }

        private void BindBtnLabel(string name, ObservableProperty<string> source)
        {
            var button = FindBtn(name);
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                Binding.BindText(text, source);
            }
        }

        private void SetBtnLabel(string name, string label)
        {
            var button = FindBtn(name);
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        private Transform ResolveSlot(string key)
        {
            if (UI != null && UI.TryGet<Component>(key, out var component) && component != null)
            {
                return component.transform;
            }

            return FindDeep(transform, key);
        }

        private Button FindBtn(string name)
        {
            Transform node = null;
            if (_btns != null)
            {
                node = _btns.Find(name);
                if (node == null)
                {
                    node = FindDeep(_btns, name);
                }
            }

            if (node == null)
            {
                node = FindDeep(transform, name);
            }

            return node != null ? node.GetComponent<Button>() : null;
        }

        private static void HideChild(Transform root, string name)
        {
            var child = root.Find(name);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        private static TMP_Text FindUiText(Transform root, string name)
        {
            var child = root.Find(name);
            if (child == null)
            {
                child = FindDeep(root, name);
            }

            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private static Transform FindDeep(Transform root, string name)
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
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
