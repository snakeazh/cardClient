using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using App.Game;
using App.Guide;
using App.Resources;
using DG.Tweening;
using Framework.UI.Binding;
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
        private GameObject _equipTipUse;
        private Button _equipTipUseBtn;
        private GameObject _equipTipCatcher;
        private Transform _equipTipAnchor;
        private int _shownEquipRelicId;
        private bool _shownRoundBuff;
        private Canvas _hudCanvas;
        private readonly Vector3[] _equipTipCorners = new Vector3[4];
        private CameraShakeAnimator _cameraShake;
        private PlayerItem _playerItem;
        private int _portraitLoadSerial;
        private int _playedAttack;
        private TMP_Text _playerCardTypeNum;
        private TMP_Text _beilvNum;
        private bool _holdAttackDisplay;
        private PlayerItem _heldAttackItem;
        private int _heldAttackValue;
        private RectTransform _hpTextRt;
        private Vector2 _hpTextHome;
        private Animation _hpTextAnim;
        private Coroutine _aiDelay;
        private readonly Dictionary<PlayerItem, Tween> _deathDissolves = new Dictionary<PlayerItem, Tween>(4);
        private readonly List<string> _guideTargetIds = new List<string>(4);
        private GuideTargetRegistry _guideTargets;
        private IDisposable _peekGoodArmedSub;
        private Button _peekGoodBtn;
        private ColorBlock _peekGoodColors;

        protected override void OnBind()
        {
            BindPlayerInfo();
            BindRoundBuff();
            SpawnEnemyInfos();
            BindPhaseButtons();
            BindCardInfo();
            BindEquips();
            BindAttackHud();
            BindAttackFx();
            BindSettleFx();
            BindBeilvNum();
            BindHudChrome();
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
            await PortraitLoader.EnsureBattleStatesAsync(ViewModel.Session.Player, ViewModel.Session.Enemies);
            RefreshPlayerItems();
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
                ViewModel.Session.Changed -= OnSessionChanged;
            }

            UnregisterGuideTargets();

            _peekGoodArmedSub?.Dispose();
            _peekGoodArmedSub = null;
            if (_peekGoodBtn != null)
            {
                _peekGoodBtn.colors = _peekGoodColors;
                _peekGoodBtn.transform.localScale = Vector3.one;
                _peekGoodBtn = null;
            }

            StopAiDelay();
            CancelAllDeathDissolves();

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
                _equipTipText = null;
                _equipTipUse = null;
                _equipTipUseBtn = null;
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
            _ = EnsureBattlePortraits();
            RefreshCardInfos();
            RefreshEquips();
            TryPlayAttack();
            TryScheduleAiDelay();
        }

        private async Task EnsureBattlePortraits()
        {
            var serial = ++_portraitLoadSerial;
            var session = ViewModel?.Session;
            var loaded = await PortraitLoader.EnsureBattleStatesAsync(session?.Player, session?.Enemies);
            if (serial != _portraitLoadSerial || ViewModel == null || !loaded)
            {
                return;
            }

            RefreshPlayerItems();
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

            var hpRoot = ResolveSlot("hptextdi") ?? ResolveSlot("hptext");
            if (hpRoot == null)
            {
                return;
            }

            _hpTextRt = hpRoot as RectTransform ?? hpRoot.GetComponent<RectTransform>();
            if (_hpTextRt != null)
            {
                _hpTextHome = _hpTextRt.anchoredPosition;
            }

            var animRoot = hpRoot.Find("ani_hptextdi") ?? FindDeep(hpRoot, "ani_hptextdi") ?? hpRoot;
            _hpTextAnim = animRoot.GetComponent<Animation>();

            var hpNum = ResolveSlot("hptext") ?? hpRoot;
            var hpText = hpNum.GetComponent<TMP_Text>() ?? hpNum.GetComponentInChildren<TMP_Text>(true);
            if (hpText != null)
            {
                Binding.BindText(hpText, ViewModel.HpText);
            }

            Binding.BindActive(hpRoot.gameObject, ViewModel.ShowHpText);
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
                ViewModel.HpText.Value = $"-{Math.Max(1, session.TakenDamage)}";
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
                session.ApplyPendingAttackHits();
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

            var damage = session.IncomingAttack
                ? Math.Max(1, session.TakenDamage)
                : Math.Max(1, session.AttackDamage);
            if (session.IncomingAttack)
            {
                if (session.Player != null && session.Player.Hp <= damage)
                {
                    _attackFx.PlayDeathEffect(_attackFx.HitPositionPlayer());
                    ScheduleDeathDissolve(_playerItem, hideWhenDone: false);
                }

                return;
            }

            var target = session.EnemyAtVisualSlot(session.AttackVisualSlot);
            if (target != null && target.Hp <= damage)
            {
                _attackFx.PlayDeathEffect(_attackFx.HitPosition(session.AttackVisualSlot));
                ScheduleDeathDissolve(AttackItemAtSlot(session.AttackVisualSlot), hideWhenDone: true);
            }
        }

        /// <summary>
        /// 致死溶解按 DeathDissolveDelay 延后播，卡片先站着不动。受击开始就会扣血，
        /// ShowEnemy 立刻翻 false —— BindEnemyVisible 得让位给这里，否则会抢在延迟结束前把卡溶掉。
        /// 怪溶完要隐藏节点，玩家卡留着。
        /// </summary>
        private void ScheduleDeathDissolve(PlayerItem item, bool hideWhenDone)
        {
            if (item == null)
            {
                return;
            }

            CancelDeathDissolve(item);
            var delay = AttackTuningConfig.Instance.DeathDissolveDelay;
            if (delay <= 0f)
            {
                PlayDeathDissolve(item, hideWhenDone);
                return;
            }

            _deathDissolves[item] = DOVirtual
                .DelayedCall(delay, () => PlayDeathDissolve(item, hideWhenDone), false)
                .SetLink(item.gameObject);
        }

        private void PlayDeathDissolve(PlayerItem item, bool hideWhenDone)
        {
            if (item == null)
            {
                return;
            }

            _deathDissolves.Remove(item);
            if (!hideWhenDone)
            {
                item.PlayDissolve();
                return;
            }

            item.PlayDissolve(-1f, () => HideEnemyItem(item));
        }

        private void CancelDeathDissolve(PlayerItem item)
        {
            if (item == null || !_deathDissolves.TryGetValue(item, out var tween))
            {
                return;
            }

            _deathDissolves.Remove(item);
            if (tween != null && tween.IsActive())
            {
                tween.Kill();
            }
        }

        private void CancelAllDeathDissolves()
        {
            foreach (var pair in _deathDissolves)
            {
                var tween = pair.Value;
                if (tween != null && tween.IsActive())
                {
                    tween.Kill();
                }
            }

            _deathDissolves.Clear();
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
            PlaceHpAt(_attackFx.HitPositionPlayer());
        }

        private void PlaceHpAtTarget(int slot)
        {
            PlaceHpAt(_attackFx.HitPosition(slot));
        }

        private void PlaceHpAt(Vector3 hit)
        {
            if (_hpTextRt == null || hit == Vector3.zero)
            {
                return;
            }

            _hpTextRt.SetAsLastSibling();
            _hpTextRt.position = hit;
            PlayHpTextAnim();
        }

        private void PlayHpTextAnim()
        {
            if (_hpTextAnim == null)
            {
                return;
            }

            _hpTextAnim.Rewind();
            _hpTextAnim.Play();
        }

        private void RestoreHpText()
        {
            if (_hpTextRt == null)
            {
                return;
            }

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

        private void BindRoundBuff()
        {
            var root = ResolveSlot("roundbuff") ?? transform.Find("roundbuff");
            if (root == null)
            {
                return;
            }

            Binding.BindActive(root.gameObject, ViewModel.RoundBuffVisible);
            var textSlot = ResolveSlot("roundbuffText");
            var text = textSlot != null
                ? textSlot.GetComponent<TMP_Text>()
                : root.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                Binding.BindText(text, ViewModel.RoundBuffName);
            }

            var button = root.GetComponent<Button>();
            if (button == null)
            {
                button = root.gameObject.AddComponent<Button>();
            }

            var image = root.GetComponent<Image>();
            if (image != null)
            {
                button.targetGraphic = image;
            }

            button.transition = Selectable.Transition.None;
            button.onClick.RemoveListener(OnRoundBuffClicked);
            button.onClick.AddListener(OnRoundBuffClicked);
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
                    CancelDeathDissolve(item);
                    item.ResetDissolve();
                    return;
                }

                if (go == null || !go.activeSelf)
                {
                    return;
                }

                if (_deathDissolves.ContainsKey(item))
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

                _playerItem.Bind(
                    session.Player,
                    PortraitLoader.Get(session.Player),
                    AttackDisplay(_playerItem, session.Player.Attack));
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

                _enemyItems[slot].Bind(
                    enemy,
                    PortraitLoader.Get(enemy),
                    AttackDisplay(_enemyItems[slot], enemy.Attack),
                    session.ActingAiId);
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
                Binding.BindRollingText(text, ViewModel.GoldText);
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
            BindPeekGoodArmed();
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
                RegisterGuideTargets();
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
            RegisterGuideTargets();
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

        private void OnRoundBuffClicked()
        {
            if (ViewModel == null || !ViewModel.RoundBuffVisible.Value)
            {
                HideEquipTip();
                return;
            }

            if (_shownRoundBuff && _equipTip != null && _equipTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            _ = ShowRoundBuffTip();
        }

        private async Task ShowRoundBuffTip()
        {
            await EnsureEquipTip();
            if (_equipTip == null || ViewModel == null)
            {
                return;
            }

            HideEquipTip();
            _shownRoundBuff = true;
            _equipTipAnchor = ResolveSlot("roundbuff") ?? transform.Find("roundbuff");
            SetEquipTipUseVisible(false);
            if (_equipTipText != null)
            {
                var desc = ViewModel.RoundBuffDesc.Value;
                _equipTipText.text = string.IsNullOrEmpty(desc) ? ViewModel.RoundBuffName.Value : desc;
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
            if (_equipTipAnchor != null)
            {
                PositionItemTip(_equipTipAnchor, placeRight: false);
            }
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
                BindEquipTipNodes(_equipTip);
                var group = _equipTip.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = _equipTip.AddComponent<CanvasGroup>();
                }

                group.blocksRaycasts = true;
                group.interactable = true;
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
            _shownRoundBuff = false;
            _equipTipAnchor = slot;
            if (_equipTipText != null)
            {
                _equipTipText.text = string.IsNullOrEmpty(relic.Desc) ? relic.Name : relic.Desc;
            }

            SetEquipTipUseVisible(RelicMechanics.IsConsumable(relic));

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
            _shownRoundBuff = false;
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

        private void BindEquipTipNodes(GameObject tip)
        {
            _equipTipText = null;
            _equipTipUse = null;
            _equipTipUseBtn = null;
            var ui = tip != null ? tip.GetComponent<UIReference>() : null;
            if (ui != null && ui.TryGet<Component>("tipContext", out var context) && context != null)
            {
                _equipTipText = context.GetComponent<TMP_Text>() ?? context.GetComponentInChildren<TMP_Text>(true);
            }

            if (_equipTipText == null && tip != null)
            {
                _equipTipText = tip.GetComponentInChildren<TMP_Text>(true);
            }

            if (ui != null && ui.TryGet<Component>("use", out var useNode) && useNode != null)
            {
                _equipTipUse = useNode.gameObject;
                _equipTipUseBtn = useNode.GetComponent<Button>() ?? useNode.GetComponentInChildren<Button>(true);
            }

            if (_equipTipUseBtn != null)
            {
                _equipTipUseBtn.onClick.RemoveAllListeners();
                _equipTipUseBtn.onClick.AddListener(OnEquipTipUseClicked);
            }
        }

        private void SetEquipTipUseVisible(bool visible)
        {
            if (_equipTipUse != null)
            {
                _equipTipUse.SetActive(visible);
            }
        }

        private void OnEquipTipUseClicked()
        {
            if (_shownEquipRelicId <= 0 || ViewModel?.Session == null)
            {
                return;
            }

            ViewModel.Session.UseRelic(_shownEquipRelicId);
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
            while (_equipSlots.Count < owned.Count)
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

        private void BindPeekGoodArmed()
        {
            _peekGoodArmedSub?.Dispose();
            _peekGoodBtn = FindBtn("PeekGood");
            if (_peekGoodBtn == null)
            {
                return;
            }

            _peekGoodColors = _peekGoodBtn.colors;
            var glow = EnsureSkillArmedGlow(_peekGoodBtn.transform);
            Binding.BindActive(glow, ViewModel.PeekGoodArmed);
            _peekGoodArmedSub = ViewModel.PeekGoodArmed.Subscribe(ApplyPeekGoodArmedVisual);
        }

        private void ApplyPeekGoodArmedVisual(bool armed)
        {
            if (_peekGoodBtn == null)
            {
                return;
            }

            var colors = _peekGoodColors;
            if (armed)
            {
                var gold = new Color(1f, 0.86f, 0.38f, 1f);
                colors.normalColor = gold;
                colors.highlightedColor = gold;
                colors.selectedColor = gold;
                colors.pressedColor = new Color(0.9f, 0.7f, 0.2f, 1f);
                _peekGoodBtn.transform.localScale = new Vector3(1.1f, 1.1f, 1f);
            }
            else
            {
                _peekGoodBtn.transform.localScale = Vector3.one;
            }

            _peekGoodBtn.colors = colors;
        }

        private static GameObject EnsureSkillArmedGlow(Transform button)
        {
            var existing = button.Find("ArmedGlow");
            if (existing != null)
            {
                return existing.gameObject;
            }

            var go = new GameObject("ArmedGlow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(button, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-10f, -10f);
            rt.offsetMax = new Vector2(10f, 10f);
            var image = go.GetComponent<Image>();
            var src = button.GetComponent<Image>();
            if (src != null)
            {
                image.sprite = src.sprite;
                image.type = src.type;
            }

            image.color = new Color(1f, 0.82f, 0.25f, 0.85f);
            image.raycastTarget = false;
            go.SetActive(false);
            return go;
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

        private void RegisterGuideTargets()
        {
            UnregisterGuideTargets();
            if (!AppServices.IsReady)
            {
                return;
            }

            _guideTargets = AppServices.Resolve<GuideTargetRegistry>();
            RegisterGuideBtn(GuideTargetIds.CompareBtn, "CompareBtn");
            RegisterGuideBtn(GuideTargetIds.PeekGood, "PeekGood");
            RegisterGuideBtn(GuideTargetIds.NextRoundBtn, "NextRoundBtn");
        }

        private void RegisterGuideBtn(string id, string name)
        {
            var button = FindBtn(name);
            if (button == null || _guideTargets == null)
            {
                return;
            }

            _guideTargets.RegisterUi(id, (RectTransform)button.transform);
            _guideTargetIds.Add(id);
        }

        private void UnregisterGuideTargets()
        {
            if (_guideTargets != null && _guideTargetIds.Count > 0)
            {
                _guideTargets.UnregisterAll(_guideTargetIds);
            }

            _guideTargetIds.Clear();
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
