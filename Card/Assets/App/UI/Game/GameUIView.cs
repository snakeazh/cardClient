using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Resources;
using DG.Tweening;
using Framework.Log;
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
        private static readonly string[] EquipSlotKeys = { "equip1", "equip2", "equip3" };

        private GameObject _gameHud;
        private GameBoardController _board;
        private Transform _btns;
        private Transform _shopContent;
        private readonly GameObject[] _enemyInfos = new GameObject[3];
        private readonly PlayerItem[] _enemyItems = new PlayerItem[3];
        private GameObject _playerCardInfo;
        private readonly GameObject[] _enemyCardInfos = new GameObject[3];
        private readonly Image[] _enemyCardTypeIcons = new Image[3];
        private readonly Image[] _enemyCardTypeLabels = new Image[3];
        private readonly TMP_Text[] _enemyCardTypeNums = new TMP_Text[3];
        private readonly Sprite[] _enemyPortraits = new Sprite[3];
        private readonly AttackCutscene _attackFx = new AttackCutscene();
        private readonly SettlePointCutscene _settleFx = new SettlePointCutscene();
        private readonly List<CardItem> _settleCards = new List<CardItem>(GameBalance.OpenHandSize);
        private readonly List<Transform> _equipSlots = new List<Transform>(GameBalance.MaxRelics);
        private readonly List<int> _equipRelicIds = new List<int>(GameBalance.MaxRelics);
        private readonly List<RelicBonusPart> _relicBonuses = new List<RelicBonusPart>(GameBalance.MaxRelics);
        private readonly List<SettlePointCutscene.BonusBeat> _bonusBeats = new List<SettlePointCutscene.BonusBeat>(GameBalance.MaxRelics * 2);
        private int _prefabEquipCount;
        private GameObject _equipTip;
        private TMP_Text _equipTipText;
        private GameObject _equipTipCatcher;
        private Transform _equipTipAnchor;
        private int _shownEquipRelicId;
        private bool _shownPeekTip;
        private Button _peekGoodBtn;
        private Canvas _hudCanvas;
        private readonly Vector3[] _equipTipCorners = new Vector3[4];
        private CameraShakeAnimator _cameraShake;
        private PlayerItem _playerItem;
        private Sprite _playerPortrait;
        private int _playedAttack;
        private TMP_Text _playerCardTypeNum;
        private TMP_Text _beilvNum;
        private bool _holdAttackDisplay;
        private PlayerItem _heldAttackItem;
        private int _heldAttackValue;
        private RectTransform _hpTextRt;
        private Vector2 _hpTextHome;
        private Coroutine _aiDelay;

        protected override void OnBind()
        {
            BindPlayerInfo();
            SpawnEnemyInfos();
            BindPhaseButtons();
            BindCardInfo();
            BindEquips();
            BindAttackHud();
            BindAttackFx();
            BindSettleFx();
            BindBeilvNum();
            BindHudChrome();
            ViewModel.PeekGoodTipRequested -= OnPeekGoodTipRequested;
            ViewModel.PeekGoodTipRequested += OnPeekGoodTipRequested;
            ViewModel.Refresh();
            RefreshPlayerItems();
            RefreshCardInfos();
            RefreshEquips();
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
            await EnsureEquipTip();
            ViewModel.Session.Changed += OnSessionChanged;
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (ViewModel == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                ViewModel.Session.DebugAddGold();
            }
        }
#endif

        protected override Task OnViewClose()
        {
            if (ViewModel != null)
            {
                ViewModel.PeekGoodTipRequested -= OnPeekGoodTipRequested;
                ViewModel.Session.Changed -= OnSessionChanged;
            }

            StopAiDelay();

            if (_board != null)
            {
                _board.Detach();
                _board = null;
            }

            _attackFx.Dispose();
            _settleFx.Dispose();
            ClearAttackHold();
            RestoreHpText();
            if (ViewModel != null)
            {
                ViewModel.ShowMask.Value = false;
                ViewModel.ShowHpText.Value = false;
            }

            HideEquipTip();
            if (_equipTip != null)
            {
                Destroy(_equipTip);
                _equipTip = null;
            }

            if (_equipTipCatcher != null)
            {
                Destroy(_equipTipCatcher);
                _equipTipCatcher = null;
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
            RefreshCardInfos();
            RefreshEquips();
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
            _attackFx.Bind(transform, _playerItem, _enemyItems);
            _cameraShake = Camera.main != null ? Camera.main.GetComponent<CameraShakeAnimator>() : null;
            _playedAttack = ViewModel != null ? ViewModel.Session.AttackPlaySerial : 0;
        }

        private void BindSettleFx()
        {
            if (ViewModel == null)
            {
                return;
            }

            _settleFx.Bind(ViewModel.Resources, transform);
        }

        private void BindBeilvNum()
        {
            var node = ResolveSlot("beilvNum");
            if (node == null)
            {
                return;
            }

            _beilvNum = node.GetComponent<TMP_Text>();
            node.gameObject.SetActive(false);
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
            PlaySettleThenAttack(session);
        }

        private void PlaySettleThenAttack(GameSession session)
        {
            var attacker = session.IncomingAttack
                ? session.EnemyAtVisualSlot(session.AttackVisualSlot)
                : session.Player;
            var attackItem = session.IncomingAttack
                ? AttackItemAtSlot(session.AttackVisualSlot)
                : _playerItem;
            var score = session.EvaluateSeat(attacker);
            var extra = attacker != null && attacker.IsPlayer
                ? RelicMechanics.SumMultiplierExtra(session.Run, score, session.LastRelicContext)
                : 0f;
            var baseAttack = attacker != null ? Math.Max(0, attacker.Attack) : 0;
            _holdAttackDisplay = true;
            _heldAttackItem = attackItem;
            _heldAttackValue = baseAttack;
            if (_beilvNum != null)
            {
                _beilvNum.gameObject.SetActive(false);
            }

            if (_board != null)
            {
                _board.CollectSelectedCards(attacker, _settleCards);
            }
            else
            {
                _settleCards.Clear();
            }

            CollectBonusBeats(extra, attacker, session, score, baseAttack);
            var cardTypeNum = AttackerCardTypeNum(session, attacker);
            _settleFx.Play(
                _settleCards,
                attackItem,
                _beilvNum,
                cardTypeNum,
                _bonusBeats,
                baseAttack,
                score.BaseChips,
                Math.Max(1, session.AttackDamage),
                value => { _heldAttackValue = value; },
                () =>
                {
                    if (ViewModel == null)
                    {
                        ClearAttackHold();
                        return;
                    }

                    _heldAttackValue = Math.Max(1, session.AttackDamage);
                    PlayAttackCutscene(session);
                });
        }

        private void CollectBonusBeats(float extra, SeatState attacker, GameSession session, HandScore score, int baseAttack)
        {
            _relicBonuses.Clear();
            _bonusBeats.Clear();
            if (attacker == null || !attacker.IsPlayer || session == null)
            {
                return;
            }

            RelicMechanics.CollectRelicBonuses(session.Run, score, _relicBonuses, session.LastRelicContext);
            if (_relicBonuses.Count == 0 && extra <= 0f)
            {
                return;
            }

            var mag = GameSession.HandTypeMagnification(score.Type);
            var attackValue = Math.Max(0, baseAttack) + Math.Max(0, score.BaseChips);
            for (var i = 0; i < _relicBonuses.Count; i++)
            {
                var part = _relicBonuses[i];
                var equip = FindEquipAnimator(part.RelicId);
                if (part.MultiplierAdd != 0f)
                {
                    mag += part.MultiplierAdd;
                    _bonusBeats.Add(new SettlePointCutscene.BonusBeat
                    {
                        EquipAnimator = equip,
                        IsAttack = false,
                        BeilvText = GameTableViewModel.FormatBonus(part.MultiplierAdd),
                        CardTypeText = GameTableViewModel.FormatMultiplier(mag),
                        AttackValue = attackValue
                    });
                }

                if (part.AttackAdd != 0f)
                {
                    attackValue += (int)Math.Round(part.AttackAdd);
                    _bonusBeats.Add(new SettlePointCutscene.BonusBeat
                    {
                        EquipAnimator = equip,
                        IsAttack = true,
                        BeilvText = GameTableViewModel.FormatBonus(part.AttackAdd),
                        CardTypeText = null,
                        AttackValue = attackValue
                    });
                }
            }
        }

        private TMP_Text AttackerCardTypeNum(GameSession session, SeatState attacker)
        {
            if (attacker == null)
            {
                return null;
            }

            if (attacker.IsPlayer)
            {
                return _playerCardTypeNum;
            }

            var slot = session != null ? session.AttackVisualSlot : -1;
            if (slot < 0 || slot >= _enemyCardTypeNums.Length)
            {
                return null;
            }

            return _enemyCardTypeNums[slot];
        }

        private Animator FindEquipAnimator(int relicId)
        {
            if (relicId <= 0)
            {
                return null;
            }

            for (var i = 0; i < _equipSlots.Count; i++)
            {
                if (i >= _equipRelicIds.Count || _equipRelicIds[i] != relicId)
                {
                    continue;
                }

                var slot = _equipSlots[i];
                return slot != null ? slot.GetComponent<Animator>() : null;
            }

            return null;
        }

        private int AttackDisplay(PlayerItem item, int logicAttack)
        {
            if (_holdAttackDisplay && item != null && item == _heldAttackItem)
            {
                return _heldAttackValue;
            }

            return logicAttack;
        }

        private void PlayAttackCutscene(GameSession session)
        {
            var mask = ResolveSlot("mask");
            if (mask != null)
            {
                mask.SetAsLastSibling();
            }

            ViewModel.ShowMask.Value = true;
            ViewModel.ShowHpText.Value = false;
            Action onHit = () =>
            {
                _cameraShake?.PlayByLevel(session.AttackLevel);
                ViewModel.HpText.Value = $"-{Math.Max(1, session.AttackDamage)}";
                ViewModel.ShowHpText.Value = true;
                if (session.IncomingAttack)
                {
                    PlaceHpAtPlayer();
                }
                else
                {
                    PlaceHpAtTarget(session.AttackVisualSlot);
                }

                TryDissolveIfLethal(session);
            };
            Action onReturned = () => { ViewModel.ShowMask.Value = false; };
            Action onDone = () =>
            {
                ViewModel.ShowHpText.Value = false;
                RestoreHpText();
                ClearAttackHold();
                if (ViewModel != null)
                {
                    ViewModel.Session.CompletePlayerAttack();
                }
            };
            if (session.IncomingAttack)
            {
                _attackFx.PlayIncoming(session.AttackVisualSlot, session.AttackLevel, onHit, onReturned, onDone);
            }
            else
            {
                _attackFx.Play(session.AttackVisualSlot, session.AttackLevel, onHit, onReturned, onDone);
            }
        }

        private void TryDissolveIfLethal(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            var damage = Math.Max(1, session.AttackDamage);
            if (session.IncomingAttack)
            {
                if (session.Player != null && session.Player.Hp <= damage)
                {
                    _playerItem?.PlayDissolve();
                }

                return;
            }

            var target = session.EnemyAtVisualSlot(session.AttackVisualSlot);
            if (target != null && target.Hp <= damage)
            {
                var item = AttackItemAtSlot(session.AttackVisualSlot);
                item?.PlayDissolve(-1f, () => HideEnemyItem(item));
            }
        }

        private PlayerItem AttackItemAtSlot(int slot)
        {
            if (slot < 0 || slot >= _enemyItems.Length)
            {
                return null;
            }

            return _enemyItems[slot];
        }

        private void ClearAttackHold()
        {
            _holdAttackDisplay = false;
            _heldAttackItem = null;
            _heldAttackValue = 0;
        }

        private void PlaceHpAtPlayer()
        {
            if (_hpTextRt == null)
            {
                return;
            }

            var hit = _attackFx.HitPositionPlayer();
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

            roundInfo.gameObject.SetActive(true);
            roundInfo.SetAsLastSibling();
            var textSlot = ResolveSlot("roundInfoText");
            var text = textSlot != null
                ? textSlot.GetComponent<TMP_Text>()
                : roundInfo.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                Binding.BindText(text, ViewModel.RoundInfo);
            }
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
                clone.SetActive(false);
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
                BindEnemyVisible(i, clone, item);
                BindSeatClick(clone, ViewModel.AttackCommands[i]);
                _enemyItems[i] = item;
                _enemyInfos[i] = clone;
            }
        }

        private void BindEnemyVisible(int slot, GameObject go, PlayerItem item)
        {
            Binding.Add(ViewModel.ShowEnemy[slot].Subscribe(visible =>
            {
                if (visible)
                {
                    go.SetActive(true);
                    item.ResetDissolve();
                    return;
                }

                if (go == null || !go.activeSelf)
                {
                    return;
                }

                item.PlayDissolve(-1f, () => HideEnemyItem(item));
            }));
        }

        private static void HideEnemyItem(PlayerItem item)
        {
            if (item != null)
            {
                item.gameObject.SetActive(false);
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
                if (session.Player != null && session.Player.Hp > 0)
                {
                    var dissolve = _playerItem.GetComponent<UiDissolve>();
                    if (dissolve == null || !dissolve.IsPlaying)
                    {
                        _playerItem.ResetDissolve();
                    }
                }

                _playerItem.Bind(session.Player, _playerPortrait, AttackDisplay(_playerItem, session.Player.Attack));
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

                _enemyItems[slot].Bind(enemy, _enemyPortraits[i], AttackDisplay(_enemyItems[slot], enemy.Attack), session.ActingAiId);
            }
        }

        private void RefreshCardInfos()
        {
            if (ViewModel == null)
            {
                return;
            }

            var session = ViewModel.Session;
            var settling = GameTableViewModel.ShouldShowCardInfo(session);
            if (!settling)
            {
                if (_beilvNum != null)
                {
                    _beilvNum.gameObject.SetActive(false);
                }
            }

            if (_playerCardInfo != null)
            {
                _playerCardInfo.SetActive(settling && !session.IncomingAttack);
            }

            for (var i = 0; i < _enemyCardInfos.Length; i++)
            {
                if (_enemyCardInfos[i] != null)
                {
                    _enemyCardInfos[i].SetActive(false);
                }
            }

            if (!settling)
            {
                return;
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
                if (!enemy.Alive)
                {
                    continue;
                }

                ApplyEnemyCardInfo(slot, session.EvaluateSeat(enemy));
            }
        }

        private void ApplyEnemyCardInfo(int slot, HandScore score)
        {
            if (slot < 0 || slot >= _enemyCardInfos.Length || _enemyCardInfos[slot] == null)
            {
                return;
            }

            _enemyCardInfos[slot].SetActive(true);
            if (_enemyCardTypeIcons[slot] != null)
            {
                _enemyCardTypeIcons[slot].sprite = ViewModel.GetCardTypeIcon(score.Type);
            }

            if (_enemyCardTypeLabels[slot] != null)
            {
                _enemyCardTypeLabels[slot].sprite = ViewModel.GetCardTypeLabel(score.Type);
            }

            if (_enemyCardTypeNums[slot] != null)
            {
                _enemyCardTypeNums[slot].text = GameTableViewModel.FormatHandMultiplier(score.Type);
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
                AppLog.Warn(LogChannel.UI, $"Failed to load portrait '{key}': {ex.Message}");
                return null;
            }
        }

        private void BindHudChrome()
        {
            BindBtn("backBtn", ViewModel.BackCommand);
            BindResourceBar();
        }

        private void BindResourceBar()
        {
            var bar = ResolveSlot("ResourceBar") ?? transform.Find("ResourceBar") ?? FindDeep(transform, "ResourceBar");
            if (bar == null)
            {
                return;
            }

            bar.gameObject.SetActive(true);
            var top = bar.Find("TopArea") ?? FindDeep(bar, "TopArea") ?? bar;
            Transform goldItem = null;
            for (var i = 0; i < top.childCount; i++)
            {
                var child = top.GetChild(i);
                if (!child.name.StartsWith("ResourceItem"))
                {
                    continue;
                }

                if (goldItem == null)
                {
                    goldItem = child;
                    child.gameObject.SetActive(true);
                    continue;
                }

                child.gameObject.SetActive(false);
            }

            if (goldItem == null)
            {
                return;
            }

            var num = goldItem.Find("Num") ?? FindDeep(goldItem, "Num");
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                Binding.BindText(text, ViewModel.GoldText);
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

            BindBtn("BlindBtn", ViewModel.BlindBetCommand, ViewModel.ShowBlind);
            BindBtn("LookBtn", ViewModel.LookCommand, ViewModel.ShowLook);
            BindBtn("RaiseBtn", ViewModel.RaiseCommand, ViewModel.ShowActions);
            BindBtn("FoldBtn", ViewModel.FoldCommand, ViewModel.ShowFold);
            BindBtn("CompareBtn", ViewModel.OpenCommand, ViewModel.ShowCompare);
            BindBtn("AllInBtn", ViewModel.AllInCommand, ViewModel.ShowAllIn);
            BindBtn("PeekGood", ViewModel.PeekGoodCommand);
            _peekGoodBtn = FindBtn("PeekGood");
            BindBtn("ChaKanGood", ViewModel.ChaKanGoodCommand);
            BindBtn("TiHuanGood", ViewModel.TiHuanGoodCommand);
            BindBtn("PeekBtn", ViewModel.RubCommand, ViewModel.ShowRub);
            BindBtn("CancelBtn", ViewModel.SkipRubCommand, ViewModel.ShowCancel);
            BindBtn("NextRoundBtn", ViewModel.ContinueCommand, ViewModel.ShowContinue);
            SetBtnLabel("CompareBtn", "开牌");
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

        private void BindCardInfo()
        {
            var root = ResolveSlot("cardinfoItem");
            if (root != null)
            {
                _playerCardInfo = root.gameObject;
                _playerCardInfo.SetActive(false);
                Binding.BindActive(_playerCardInfo, ViewModel.ShowCardInfo);
            }

            BindImage("cardtype", ViewModel.CardTypeIcon);
            BindImage("cardtype2", ViewModel.CardTypeLabel);
            var num = ResolveSlot("cardtypeNum");
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            _playerCardTypeNum = text;
            if (text != null)
            {
                Binding.BindText(text, ViewModel.CardTypeNum);
            }

            SpawnEnemyCardInfos(root);
        }

        private void SpawnEnemyCardInfos(Transform template)
        {
            if (template == null)
            {
                return;
            }

            for (var i = 0; i < EnemySlotKeys.Length; i++)
            {
                var slot = ResolveSlot(EnemySlotKeys[i]);
                if (slot == null)
                {
                    continue;
                }

                var parent = slot.Find("cardInfoParent") ?? FindDeep(slot, "cardInfoParent");
                if (parent == null)
                {
                    continue;
                }

                var clone = Instantiate(template.gameObject, parent, false);
                clone.name = "cardinfoItem";
                clone.SetActive(false);
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
                    rt.localRotation = Quaternion.identity;
                    rt.localScale = Vector3.one;
                }

                StripBindKeys(clone);
                _enemyCardInfos[i] = clone;
                _enemyCardTypeIcons[i] = FindUiImage(clone.transform, "cardtype");
                _enemyCardTypeLabels[i] = FindUiImage(clone.transform, "cardtype2");
                _enemyCardTypeNums[i] = FindUiText(clone.transform, "cardtypeNum");
            }
        }

        private static void StripBindKeys(GameObject root)
        {
            var binds = root.GetComponentsInChildren<Framework.UI.Binding.UIBind>(true);
            for (var i = 0; i < binds.Length; i++)
            {
                Destroy(binds[i]);
            }
        }

        private void BindImage(string key, ObservableProperty<Sprite> source)
        {
            var node = ResolveSlot(key);
            var image = node != null ? node.GetComponent<Image>() : null;
            if (image == null)
            {
                return;
            }

            Binding.BindImageSprite(image, source);
        }

        private void BindEquips()
        {
            _equipSlots.Clear();
            _equipRelicIds.Clear();
            for (var i = 0; i < EquipSlotKeys.Length; i++)
            {
                var slot = ResolveSlot(EquipSlotKeys[i]);
                if (slot != null)
                {
                    HookEquipClick(slot, _equipSlots.Count);
                    _equipSlots.Add(slot);
                    _equipRelicIds.Add(0);
                }
            }

            _prefabEquipCount = _equipSlots.Count;
        }

        private void HookEquipClick(Transform slot, int index)
        {
            if (slot == null)
            {
                return;
            }

            var button = slot.GetComponent<Button>();
            if (button == null)
            {
                button = slot.gameObject.AddComponent<Button>();
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnEquipClicked(index));
        }

        private void OnEquipClicked(int index)
        {
            if (index < 0 || index >= _equipRelicIds.Count || _equipRelicIds[index] <= 0)
            {
                HideEquipTip();
                return;
            }

            var relic = RelicConfig.Get(_equipRelicIds[index]);
            if (relic == null)
            {
                HideEquipTip();
                return;
            }

            if (_equipTip != null && _equipTip.activeSelf && _shownEquipRelicId == relic.Id)
            {
                HideEquipTip();
                return;
            }

            _ = ShowEquipTip(_equipSlots[index], relic);
        }

        private void OnPeekGoodTipRequested()
        {
            if (_shownPeekTip && _equipTip != null && _equipTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            _ = ShowPeekGoodTip();
        }

        private async Task ShowPeekGoodTip()
        {
            await EnsureEquipTip();
            if (_equipTip == null || _peekGoodBtn == null)
            {
                return;
            }

            _shownPeekTip = true;
            _shownEquipRelicId = 0;
            _equipTipAnchor = _peekGoodBtn.transform;
            if (_equipTipText != null)
            {
                _equipTipText.text = "长按牌即可拖拽来搓牌";
            }

            EnsureEquipTipCatcher();
            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(true);
                _equipTipCatcher.transform.SetAsLastSibling();
            }

            _equipTip.SetActive(true);
            Canvas.ForceUpdateCanvases();
            var tipRt = _equipTip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }

            _equipTip.transform.SetAsLastSibling();
            PositionItemTip(_peekGoodBtn.transform, placeRight: true);
        }

        private async Task EnsureEquipTip()
        {
            if (_equipTip != null || ViewModel == null || ViewModel.Resources == null)
            {
                return;
            }

            try
            {
                var prefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.ItemTip);
                if (prefab == null)
                {
                    return;
                }

                _equipTip = Instantiate(prefab, transform, false);
                _equipTip.name = "ItemTip";
                _equipTipText = _equipTip.GetComponentInChildren<TMP_Text>(true);
                var group = _equipTip.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = _equipTip.AddComponent<CanvasGroup>();
                }

                group.blocksRaycasts = false;
                group.interactable = false;
                _equipTip.SetActive(false);
                _hudCanvas = GetComponentInParent<Canvas>();
            }
            catch (Exception)
            {
            }
        }

        private async Task ShowEquipTip(Transform slot, RelicConfig relic)
        {
            await EnsureEquipTip();
            if (_equipTip == null || relic == null)
            {
                return;
            }

            _shownEquipRelicId = relic.Id;
            _shownPeekTip = false;
            _equipTipAnchor = slot;
            if (_equipTipText != null)
            {
                _equipTipText.text = string.IsNullOrEmpty(relic.Desc) ? relic.Name : relic.Desc;
            }

            EnsureEquipTipCatcher();
            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(true);
                _equipTipCatcher.transform.SetAsLastSibling();
            }

            _equipTip.SetActive(true);
            Canvas.ForceUpdateCanvases();
            var tipRt = _equipTip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }

            _equipTip.transform.SetAsLastSibling();
            PositionItemTip(slot, placeRight: false);
        }

        private void HideEquipTip()
        {
            _shownEquipRelicId = 0;
            _shownPeekTip = false;
            _equipTipAnchor = null;
            if (_equipTip != null)
            {
                _equipTip.SetActive(false);
            }

            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(false);
            }
        }

        private void EnsureEquipTipCatcher()
        {
            if (_equipTipCatcher != null)
            {
                return;
            }

            var go = new GameObject("ItemTipCatcher", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(HideEquipTip);
            go.SetActive(false);
            _equipTipCatcher = go;
        }

        private void PositionItemTip(Transform slot, bool placeRight)
        {
            var tipRt = _equipTip != null ? _equipTip.GetComponent<RectTransform>() : null;
            var itemRt = slot != null ? slot.transform as RectTransform : null;
            var parent = transform as RectTransform;
            if (tipRt == null || itemRt == null || parent == null)
            {
                return;
            }

            var cam = _hudCanvas != null ? _hudCanvas.worldCamera : null;
            itemRt.GetWorldCorners(_equipTipCorners);
            var edge = placeRight
                ? (_equipTipCorners[2] + _equipTipCorners[3]) * 0.5f
                : (_equipTipCorners[0] + _equipTipCorners[1]) * 0.5f;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, edge);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local))
            {
                return;
            }

            var tipWidth = tipRt.rect.width;
            var tipHeight = tipRt.rect.height;
            tipRt.anchoredPosition = placeRight
                ? new Vector2(
                    local.x + 8f + tipRt.pivot.x * tipWidth,
                    local.y + (0.5f - tipRt.pivot.y) * tipHeight)
                : new Vector2(
                    local.x - 8f - (1f - tipRt.pivot.x) * tipWidth,
                    local.y + (0.5f - tipRt.pivot.y) * tipHeight);
        }

        private void RefreshEquips()
        {
            if (ViewModel == null || _equipSlots.Count == 0)
            {
                return;
            }

            var owned = ViewModel.Session.Run.RelicConfigIds;
            var template = _equipSlots[0];
            var parent = template.parent;
            while (_equipSlots.Count < owned.Count && _equipSlots.Count < GameBalance.MaxRelics)
            {
                var clone = Instantiate(template.gameObject, parent);
                clone.name = $"equip{_equipSlots.Count + 1}";
                var bind = clone.GetComponent<Framework.UI.Binding.UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                HookEquipClick(clone.transform, _equipSlots.Count);
                _equipSlots.Add(clone.transform);
                _equipRelicIds.Add(0);
            }

            while (_equipRelicIds.Count < _equipSlots.Count)
            {
                _equipRelicIds.Add(0);
            }

            var atlas = ViewModel.Atlas;
            var shownStillOwned = false;
            for (var i = 0; i < _equipSlots.Count; i++)
            {
                var slot = _equipSlots[i];
                if (slot == null)
                {
                    continue;
                }

                RelicConfig relic = null;
                if (i < owned.Count)
                {
                    relic = RelicConfig.Get(owned[i]);
                }

                _equipRelicIds[i] = relic != null ? relic.Id : 0;
                if (relic != null && relic.Id == _shownEquipRelicId)
                {
                    shownStillOwned = true;
                }

                Sprite sprite = null;
                if (relic != null && atlas != null && !string.IsNullOrEmpty(relic.Icon))
                {
                    atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out sprite);
                }

                var icon = FindUiImage(slot, "icon");
                if (icon != null)
                {
                    if (sprite != null)
                    {
                        icon.sprite = sprite;
                    }

                    icon.enabled = sprite != null;
                }

                var bg = FindUiImage(slot, "bgcolor") ?? FindUiImage(slot, "Image (1)");
                if (bg != null)
                {
                    bg.color = ThemeColors.EquipSlot(relic != null, relic != null ? relic.Type : QualityType.Ordinary);
                }

                var nohave = slot.Find("nohave") ?? FindDeep(slot, "nohave");
                if (nohave != null)
                {
                    nohave.gameObject.SetActive(relic == null);
                }

                var keepEmptyFrame = i < _prefabEquipCount;
                slot.gameObject.SetActive(keepEmptyFrame || sprite != null);
            }

            if (_shownEquipRelicId > 0 && !shownStillOwned)
            {
                HideEquipTip();
            }
        }

        private static Image FindUiImage(Transform root, string name)
        {
            var child = root.Find(name);
            if (child == null)
            {
                child = FindDeep(root, name);
            }

            return child != null ? child.GetComponent<Image>() : null;
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
            if (_shopContent != null)
            {
                _shopContent.gameObject.SetActive(false);
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
