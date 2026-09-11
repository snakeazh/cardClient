using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using App.Game;
using App.Guide;
using App.Resources;
using App.Talent;
using DG.Tweening;
using Framework.UI.Binding;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using HandType = App.Game.HandType;

namespace App.UI
{
    /// <summary>
    /// 对局 HUD。player1/2/3 是人物卡槽；敌人手牌在 GameHud.otherNode。
    /// </summary>
    [AutoScreen(AppScreenIds.GameUI, UILayer.Page, ResResourcePaths.GameUI)]
    public sealed class GameUIView : ViewBase<GameTableViewModel>
    {
        private const float ExtraAttackTimeScale = 1.5f;
        private static readonly string[] EnemySlotKeys = { "player1", "player2", "player3" };
        private static readonly string[] EquipSlotKeys = { "equip1", "equip2", "equip3" };
        private const float EnemySlideDuration = 0.3f;
        private const float HudFlyDuration = 0.4f;
        private const float HudFadeDuration = 0.25f;

        private GameObject _gameHud;
        private GameBoardController _board;
        private Transform _btns;
        private Transform _shopContent;
        private readonly GameObject[] _enemyInfos = new GameObject[3];
        private readonly PlayerItem[] _enemyItems = new PlayerItem[3];
        private readonly Transform[] _enemyHomes = new Transform[3];
        private readonly Tween[] _enemySlides = new Tween[3];
        private bool _enemyStandReady;
        private Vector3 _enemyItemLocalScale = Vector3.one;
        private GameObject _playerCardInfo;
        private GameObject _enemyCardInfo;
        private Image _enemyCardTypeIcon;
        private Transform _enemyCardTypeNum;
        private readonly AttackCutscene _attackFx = new AttackCutscene();
        private readonly OpeningCutscene _openingFx = new OpeningCutscene();
        private readonly SettlePointCutscene _settleFx = new SettlePointCutscene();
        private bool _openingPlaying;
        private int _openingForSerial = -1;
        private bool _waitHudForOpening;
        private readonly List<OpeningCutscene.Talk> _openingTalks = new List<OpeningCutscene.Talk>(3);
        private bool _lethalTauntThisAttack;
        private readonly List<CardItem> _settleCards = new List<CardItem>(GameBalance.OpenHandSize);
        private readonly List<Transform> _equipSlots = new List<Transform>(GameBalance.MaxRelics);
        private readonly List<int> _equipRelicIds = new List<int>(GameBalance.MaxRelics);
        private readonly List<RelicBonusPart> _relicBonuses = new List<RelicBonusPart>(GameBalance.MaxRelics);
        private readonly List<SettlePointCutscene.BonusBeat> _bonusBeats = new List<SettlePointCutscene.BonusBeat>(GameBalance.MaxRelics * 2);
        private GameObject _equipTip;
        private TMP_Text _equipTipTitle;
        private TMP_Text _equipTipText;
        private GameObject _equipTipUse;
        private Button _equipTipUseBtn;
        private GameObject _equipTipCatcher;
        private Transform _equipTipAnchor;
        private int _shownEquipRelicId;
        private int _shownRoundBuffId;
        private bool _shownPlayerTip;
        private int _shownEnemySlot = -1;
        private Transform _roundBuffGrid;
        private GameObject _roundBuffTemplate;
        private readonly List<GameObject> _roundBuffs = new List<GameObject>(4);
        private readonly List<int> _roundBuffEntryIds = new List<int>(4);
        private GameObject _winTip;
        private Transform _winTipTemplate;
        private readonly List<GameObject> _winTipRows = new List<GameObject>(6);
        private bool _shownWinTip;
        private bool _shownPeekGoodTip;
#if UNITY_EDITOR
        private string _debugRelicIdText = string.Empty;
        private string _debugRelicPreview = string.Empty;
        private string _debugEntryIdText = "104";
        private string _debugEntryPreview = string.Empty;
#endif
        private Coroutine _peekHoldCo;
        private bool _peekHoldFired;
        private Canvas _hudCanvas;
        private readonly Vector3[] _equipTipCorners = new Vector3[4];
        private CameraShakeAnimator _cameraShake;
        private PlayerItem _playerItem;
        private int _portraitLoadSerial;
        private int _playedAttack;
        private Transform _playerCardTypeNum;
        private RectTransform _beilvInfo;
        private Image _beilvIcon;
        private TMP_Text _attackNum;
        private Transform _beilvNumRoot;
        private bool _holdAttackDisplay;
        private PlayerItem _heldAttackItem;
        private int _heldAttackValue;
        private RectTransform _hpTextRt;
        private Vector2 _hpTextHome;
        private Animation _hpTextAnim;
        private readonly List<RectTransform> _splashHpClones = new List<RectTransform>(2);
        private Coroutine _aiDelay;
        private readonly Dictionary<PlayerItem, Tween> _deathDissolves = new Dictionary<PlayerItem, Tween>(4);
        private bool _attackCutsceneDone;
        private PlayerItem _waitLethalItem;
        private readonly bool[] _pendingEnemyDeathFx = new bool[3];
        private readonly List<string> _guideTargetIds = new List<string>(4);
        private GuideTargetRegistry _guideTargets;
        private IDisposable _peekGoodArmedSub;
        private Button _peekGoodBtn;
        private ColorBlock _peekGoodColors;
        private readonly List<HudFly> _hudFlies = new List<HudFly>(4);
        private readonly Dictionary<GameObject, Tween> _hudFades = new Dictionary<GameObject, Tween>(8);
        private Sequence _hudEntrance;

        protected override void OnBind()
        {
            BindPlayerInfo();
            BindRoundBuff();
            SpawnEnemyInfos();
            BindPhaseButtons();
            BindCardInfo();
            BindAttackHud();
            BindAttackFx();
            BindSettleFx();
            BindBeilvInfo();
            BindHudChrome();
            BindBeisu();
            ViewModel.Refresh();
            RefreshPlayerItems();
            RefreshCardInfos();
            RefreshEquips();
            RefreshRoundBuffs();
            _waitHudForOpening = true;
            PlayHudEntrance(() =>
            {
                _waitHudForOpening = false;
                TryStartOpening();
            });
        }

        protected override async Task OnViewOpen()
        {
            SetInBattleFit(true);
            PrepareHudEntrance();
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
            RefreshEquips();
            await EnsureEquipTip();
            await EnsureWinTip();
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

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(8f, 8f, 260f, 78f), GUI.skin.box);
            GUILayout.Label("测试加圣物（仅编辑器）");
            GUILayout.BeginHorizontal();
            var next = GUILayout.TextField(_debugRelicIdText ?? string.Empty, GUILayout.Width(88f));
            if (next != _debugRelicIdText)
            {
                _debugRelicIdText = next;
                RefreshDebugRelicPreview();
            }

            var canGrant = int.TryParse(_debugRelicIdText, out var relicId) && relicId > 0;
            var wasEnabled = GUI.enabled;
            GUI.enabled = canGrant;
            if (GUILayout.Button("添加", GUILayout.Width(56f)) && canGrant)
            {
                ViewModel?.Session?.DebugGrantRelic(relicId);
            }

            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(_debugRelicPreview) ? "输入 RelicConfig.Id" : _debugRelicPreview);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(8f, 90f, 260f, 78f), GUI.skin.box);
            GUILayout.Label("测试加关卡词缀（仅编辑器）");
            GUILayout.BeginHorizontal();
            var entryNext = GUILayout.TextField(_debugEntryIdText ?? string.Empty, GUILayout.Width(88f));
            if (entryNext != _debugEntryIdText)
            {
                _debugEntryIdText = entryNext;
                RefreshDebugEntryPreview();
            }

            var canGrantEntry = int.TryParse(_debugEntryIdText, out var entryId) && entryId > 0;
            GUI.enabled = canGrantEntry;
            if (GUILayout.Button("添加", GUILayout.Width(56f)) && canGrantEntry)
            {
                ViewModel?.Session?.DebugGrantBossEntry(entryId);
            }

            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(_debugEntryPreview) ? "输入 BossEntryConfig.Id" : _debugEntryPreview);
            GUILayout.EndArea();
        }

        private void RefreshDebugRelicPreview()
        {
            if (!int.TryParse(_debugRelicIdText, out var relicId) || relicId <= 0)
            {
                _debugRelicPreview = string.Empty;
                return;
            }

            var relic = RelicConfig.Get(relicId);
            _debugRelicPreview = relic != null ? $"{relic.Name}  {relic.Desc}" : $"没有 Id={relicId}";
        }

        private void RefreshDebugEntryPreview()
        {
            if (!int.TryParse(_debugEntryIdText, out var entryId) || entryId <= 0)
            {
                _debugEntryPreview = string.Empty;
                return;
            }

            var entry = BossEntryConfig.Get(entryId);
            _debugEntryPreview = entry != null ? $"{entry.Name}  {entry.Desc}" : $"没有 Id={entryId}";
        }
#endif

        protected override Task OnViewClose()
        {
            SetInBattleFit(false);
            if (ViewModel != null)
            {
                ViewModel.Session.Changed -= OnSessionChanged;
            }

            GameTableViewModel.RestorePlaybackSpeed();
            KillHudMotion(restoreHome: true);
            _openingFx.Dispose();
            _openingPlaying = false;
            _openingForSerial = -1;
            _waitHudForOpening = false;
            _lethalTauntThisAttack = false;
            _playerItem?.HideDialogImmediate();
            for (var i = 0; i < _enemyItems.Length; i++)
            {
                _enemyItems[i]?.HideDialogImmediate();
            }

            UnregisterGuideTargets();
            StopPeekHold();

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
            ClearPendingEnemyDeathFx();
            RestoreHpText();
            _waitLethalItem = null;
            if (ViewModel != null && ViewModel.Session.AttackPlaying)
            {
                _attackCutsceneDone = false;
                ViewModel.Session.CompletePlayerAttack();
            }

            if (_board != null)
            {
                _board.Detach();
                _board = null;
            }

            KillEnemySlides();
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
            ClearRoundBuffs();
            if (_winTip != null)
            {
                Destroy(_winTip);
                _winTip = null;
                _winTipTemplate = null;
            }

            _winTipRows.Clear();
            if (_equipTip != null)
            {
                Destroy(_equipTip);
                _equipTip = null;
                _equipTipTitle = null;
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

        private void SetInBattleFit(bool inBattle)
        {
            var root = GetComponentInParent<UIRoot>(true);
            if (root == null)
            {
                root = FindObjectOfType<UIRoot>();
            }

            root?.SetInBattleFit(inBattle);
        }

        private void OnSessionChanged()
        {
            RefreshShop();
            RefreshPlayerItems();
            _ = EnsureBattlePortraits();
            RefreshCardInfos();
            RefreshEquips();
            RefreshRoundBuffs();
            TryStartOpening();
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
            TryStartOpening();
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

        private void BindBeilvInfo()
        {
            var info = ResolveSlot("beilvInfo");
            _beilvInfo = info as RectTransform;
            if (_beilvInfo == null && info != null)
            {
                _beilvInfo = info.GetComponent<RectTransform>();
            }

            if (_beilvInfo != null)
            {
                _beilvInfo.gameObject.SetActive(false);
            }

            var icon = ResolveSlot("beilvIcon");
            _beilvIcon = icon != null ? icon.GetComponent<Image>() : null;
            var attack = ResolveSlot("attackNum");
            _attackNum = attack != null ? attack.GetComponent<TMP_Text>() : null;
            _beilvNumRoot = ResolveSlot("beilvNum");
            if (_beilvNumRoot != null)
            {
                CardTypeValueSprites.Prepare(_beilvNumRoot);
            }
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
            HideBeilvInfo();

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
                _beilvInfo,
                _beilvIcon,
                _attackNum,
                _beilvNumRoot,
                cardTypeNum,
                ViewModel.Atlas,
                _bonusBeats,
                baseAttack,
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
                    PlayAttackCutscene(session, session.ExtraAttackPending ? ExtraAttackTimeScale : 1f);
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
            var attackValue = Math.Max(0, baseAttack);
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
                        RelicId = part.RelicId,
                        IsAttack = false,
                        BeilvText = GameTableViewModel.FormatMultiplier(part.MultiplierAdd),
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
                        RelicId = part.RelicId,
                        IsAttack = true,
                        BeilvText = GameTableViewModel.FormatBonus(part.AttackAdd),
                        CardTypeText = null,
                        AttackValue = attackValue
                    });
                }
            }
        }

        private Transform AttackerCardTypeNum(GameSession session, SeatState attacker)
        {
            if (attacker == null)
            {
                return null;
            }

            if (attacker.IsPlayer)
            {
                return _playerCardTypeNum;
            }

            return _enemyCardTypeNum;
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

        private void PlayAttackCutscene(GameSession session, float timeScale = 1f)
        {
            if (session.IncomingAttackWouldKill)
            {
                _lethalTauntThisAttack = true;
                var speaker = AttackItemAtSlot(session.AttackVisualSlot);
                _openingFx.PlayLine(
                    speaker,
                    top: false,
                    OpeningCutscene.LethalLine,
                    gameObject,
                    () =>
                    {
                        if (ViewModel == null || ViewModel.Session != session)
                        {
                            return;
                        }

                        PlayAttackCharge(session, timeScale);
                    });
                return;
            }

            _lethalTauntThisAttack = false;
            PlayAttackCharge(session, timeScale);
        }

        private void PlayAttackCharge(GameSession session, float timeScale = 1f)
        {
            var mask = ResolveSlot("mask");
            if (mask != null)
            {
                mask.SetAsLastSibling();
            }

            _attackCutsceneDone = false;
            ClearPendingEnemyDeathFx();
            ViewModel.ShowMask.Value = true;
            ViewModel.ShowHpText.Value = false;
            RestoreSplashHpClones();
            Func<bool> onHit = () =>
            {
                _cameraShake?.PlayByLevel(session.AttackLevel);
                session.ApplyPendingAttackHits();
                TryDissolveIfLethal(session);
                ShowAttackHpTexts(session);
                if (!session.IncomingAttack)
                {
                    var mainSlot = session.AttackVisualSlot;
                    for (var slot = 0; slot < _enemyItems.Length; slot++)
                    {
                        if (slot == mainSlot || !session.LastAttackHitApplied(slot))
                        {
                            continue;
                        }

                        _attackFx.ReactSlot(slot, session.LastAttackHitMissed(slot));
                    }
                }

                session.NotifyUi();
                return session.LastAttackMissed;
            };
            Action onCollisionDone = PlayPendingEnemyDeathEffects;
            Action onReturned = () =>
            {
                if (session.ExtraAttackPending)
                {
                    return;
                }

                ViewModel.ShowMask.Value = false;
            };
            Action onDone = () =>
            {
                ViewModel.ShowHpText.Value = false;
                RestoreHpText();
                ClearAttackHold();
                if (_lethalTauntThisAttack && session.LastAttackMissed)
                {
                    _lethalTauntThisAttack = false;
                    _openingFx.PlayLine(
                        _playerItem,
                        top: true,
                        OpeningCutscene.DodgeReplyLine,
                        gameObject,
                        FinishAttackAfterDodgeReply);
                    return;
                }

                _lethalTauntThisAttack = false;
                _attackCutsceneDone = true;
                FinishAttackIfReady();
            };
            if (session.IncomingAttack)
            {
                _attackFx.PlayIncoming(
                    session.AttackVisualSlot,
                    session.AttackLevel,
                    onHit,
                    onCollisionDone,
                    onReturned,
                    onDone);
            }
            else
            {
                _attackFx.Play(
                    session.AttackVisualSlot,
                    session.AttackLevel,
                    onHit,
                    onCollisionDone,
                    onReturned,
                    onDone,
                    timeScale);
            }
        }

        private void FinishAttackAfterDodgeReply()
        {
            if (ViewModel == null)
            {
                return;
            }

            _attackCutsceneDone = true;
            FinishAttackIfReady();
        }

        private void TryDissolveIfLethal(GameSession session)
        {
            if (session == null)
            {
                return;
            }

            if (session.Player != null && session.Player.Hp <= 0)
            {
                _attackFx.PlayDeathEffect(_attackFx.HitPositionPlayer());
                ScheduleDeathDissolve(_playerItem, hideWhenDone: false);
            }

            var mainSlot = session.AttackVisualSlot;
            for (var slot = 0; slot < _enemyItems.Length; slot++)
            {
                if (!session.LastAttackHitKilled(slot))
                {
                    continue;
                }

                var item = AttackItemAtSlot(slot);
                _pendingEnemyDeathFx[slot] = true;
                if (item == null)
                {
                    continue;
                }

                if (IsDissolvePlaying(item) || _deathDissolves.ContainsKey(item))
                {
                    continue;
                }

                if (slot == mainSlot || _waitLethalItem == null)
                {
                    _waitLethalItem = item;
                }

                ScheduleDeathDissolve(item, hideWhenDone: true);
            }
        }

        /// <summary>
        /// 敌人致死烟等碰撞完成（命中定格结束、开始退回）再播。
        /// </summary>
        private void PlayPendingEnemyDeathEffects()
        {
            for (var slot = 0; slot < _pendingEnemyDeathFx.Length; slot++)
            {
                if (!_pendingEnemyDeathFx[slot])
                {
                    continue;
                }

                _pendingEnemyDeathFx[slot] = false;
                _attackFx.PlayDeathEffect(_attackFx.HitPosition(slot));
            }
        }

        private void ClearPendingEnemyDeathFx()
        {
            for (var i = 0; i < _pendingEnemyDeathFx.Length; i++)
            {
                _pendingEnemyDeathFx[i] = false;
            }
        }

        /// <summary>
        /// 致死溶解按 DeathDissolveDelay 延后播，卡片先站着不动。扣血后 Hp 为 0 才预约。
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
                FinishAttackIfReady();
                return;
            }

            _deathDissolves.Remove(item);
            if (!hideWhenDone)
            {
                item.PlayDissolve();
                return;
            }

            item.PlayDissolve(-1f, () =>
            {
                HideEnemyItem(item);
                FinishAttackIfReady();
            });
        }

        private void FinishAttackIfReady()
        {
            if (!_attackCutsceneDone)
            {
                return;
            }

            if (ViewModel != null && ViewModel.Session.TryBeginExtraAttack())
            {
                _attackCutsceneDone = false;
                PlayAttackCutscene(ViewModel.Session, ExtraAttackTimeScale);
                return;
            }

            if (IsWaitingLethalDissolve())
            {
                return;
            }

            _attackCutsceneDone = false;
            _waitLethalItem = null;
            if (ViewModel != null)
            {
                ViewModel.Session.CompletePlayerAttack();
            }
        }

        private bool IsWaitingLethalDissolve()
        {
            foreach (var pair in _deathDissolves)
            {
                if (pair.Key == _playerItem)
                {
                    continue;
                }

                if (pair.Value != null && pair.Value.IsActive())
                {
                    return true;
                }
            }

            if (IsDissolvePlaying(_waitLethalItem))
            {
                return true;
            }

            for (var i = 0; i < _enemyItems.Length; i++)
            {
                var item = _enemyItems[i];
                if (item != null && item != _waitLethalItem && IsDissolvePlaying(item))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDissolvePlaying(PlayerItem item)
        {
            if (item == null)
            {
                return false;
            }

            var dissolve = item.GetComponent<UiDissolve>();
            return dissolve != null && dissolve.IsPlaying;
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
            HideBeilvInfo();
        }

        private void HideBeilvInfo()
        {
            if (_beilvInfo != null)
            {
                _beilvInfo.gameObject.SetActive(false);
            }
        }

        private void ShowAttackHpTexts(GameSession session)
        {
            RestoreSplashHpClones();
            if (session.IncomingAttack)
            {
                var missed = session.LastAttackMissed;
                ViewModel.HpText.Value = missed ? "MISS" : $"-{Math.Max(1, session.TakenDamage)}";
                ViewModel.ShowHpText.Value = true;
                PlaceHpAtPlayer(!missed);
                return;
            }

            var mainSlot = session.AttackVisualSlot;
            var mainShown = ShowMainHpText(session);
            ViewModel.ShowHpText.Value = mainShown;
            if (mainShown)
            {
                PlaceHpAtTarget(mainSlot, !session.LastAttackMissed && session.LastAttackHitDealt(mainSlot) > 0);
            }

            for (var slot = 0; slot < _enemyItems.Length; slot++)
            {
                if (slot == mainSlot || !session.LastAttackHitApplied(slot))
                {
                    continue;
                }

                var missed = session.LastAttackHitMissed(slot);
                var dealt = session.LastAttackHitDealt(slot);
                if (!missed && dealt <= 0)
                {
                    continue;
                }

                PlaceSplashHpAt(slot, missed ? "MISS" : $"-{Math.Max(1, dealt)}", !missed);
            }
        }

        private bool ShowMainHpText(GameSession session)
        {
            if (session.LastAttackMissed)
            {
                ViewModel.HpText.Value = "MISS";
                return true;
            }

            if (session.TakenDamage > 0)
            {
                ViewModel.HpText.Value = $"-{Math.Max(1, session.TakenDamage)}";
                return true;
            }

            ViewModel.HpText.Value = string.Empty;
            return false;
        }

        private void PlaceSplashHpAt(int slot, string text, bool playAnim)
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

            var clone = UnityEngine.Object.Instantiate(_hpTextRt, _hpTextRt.parent, false);
            clone.gameObject.SetActive(true);
            clone.SetAsLastSibling();
            clone.position = hit;
            var label = clone.GetComponent<TMP_Text>() ?? clone.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = text;
            }

            var animRoot = clone.Find("ani_hptextdi") ?? FindDeep(clone, "ani_hptextdi") ?? clone;
            var anim = animRoot.GetComponent<Animation>();
            if (playAnim && anim != null)
            {
                anim.Rewind();
                anim.Play();
            }

            _splashHpClones.Add(clone);
        }

        private void RestoreSplashHpClones()
        {
            for (var i = 0; i < _splashHpClones.Count; i++)
            {
                var clone = _splashHpClones[i];
                if (clone != null)
                {
                    UnityEngine.Object.Destroy(clone.gameObject);
                }
            }

            _splashHpClones.Clear();
        }

        private void PlaceHpAtPlayer(bool playAnim)
        {
            PlaceHpAt(_attackFx.HitPositionPlayer(), playAnim);
        }

        private void PlaceHpAtTarget(int slot, bool playAnim)
        {
            PlaceHpAt(_attackFx.HitPosition(slot), playAnim);
        }

        private void PlaceHpAt(Vector3 hit, bool playAnim)
        {
            if (_hpTextRt == null || hit == Vector3.zero)
            {
                return;
            }

            _hpTextRt.SetAsLastSibling();
            _hpTextRt.position = hit;
            if (playAnim)
            {
                PlayHpTextAnim();
                return;
            }

            StopHpTextAnim();
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

        private void StopHpTextAnim()
        {
            if (_hpTextAnim == null)
            {
                return;
            }

            _hpTextAnim.Stop();
            _hpTextAnim.Rewind();
            _hpTextAnim.Sample();
            _hpTextAnim.Stop();
        }

        private void RestoreHpText()
        {
            StopHpTextAnim();
            RestoreSplashHpClones();
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
            if (_playerItem != null)
            {
                BindSeatClick(_playerItem.gameObject, new RelayCommand(OnPlayerClicked, () => true));
            }

            BindRoundInfo();
            RefreshPlayerItems();
        }

        private void BindRoundInfo()
        {
            BindStageInfo();
            var roundInfo = ResolveSlot("roundInfo") ?? transform.Find("roundInfo");
            if (roundInfo == null)
            {
                return;
            }

            BindFadeActive(roundInfo.gameObject, ViewModel.ShowRoundInfo);
            roundInfo.SetAsFirstSibling();
            var textSlot = ResolveSlot("roundInfoText");
            var text = textSlot != null
                ? textSlot.GetComponent<TMP_Text>()
                : roundInfo.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                Binding.BindText(text, ViewModel.RoundInfo);
            }
        }

        private void BindStageInfo()
        {
            var stageInfo = ResolveSlot("stageInfo") ?? transform.Find("stageInfo");
            if (stageInfo != null)
            {
                stageInfo.gameObject.SetActive(true);
            }

            var textSlot = ResolveSlot("stageInfoText");
            var text = textSlot != null
                ? textSlot.GetComponent<TMP_Text>()
                : stageInfo != null
                    ? stageInfo.GetComponentInChildren<TMP_Text>(true)
                    : null;
            if (text != null)
            {
                Binding.BindText(text, ViewModel.StageInfoText);
            }
        }

        private void BindRoundBuff()
        {
            _roundBuffGrid = ResolveSlot("roundbuffGrid") ?? transform.Find("roundbuffGrid") ??
                             FindDeep(transform, "roundbuffGrid");
            var template = ResolveSlot("roundbuff") ?? transform.Find("roundbuff") ?? FindDeep(transform, "roundbuff");
            if (template != null)
            {
                _roundBuffTemplate = template.gameObject;
                _roundBuffTemplate.SetActive(false);
            }

            if (_roundBuffGrid != null)
            {
                Binding.BindActive(_roundBuffGrid.gameObject, ViewModel.RoundBuffVisible);
            }
        }

        private void RefreshRoundBuffs()
        {
            if (_roundBuffTemplate == null || ViewModel == null)
            {
                return;
            }

            var parent = _roundBuffGrid != null ? _roundBuffGrid : _roundBuffTemplate.transform.parent;
            var entries = BossMechanics.ResolveAll(ViewModel.Session.Run);
            while (_roundBuffs.Count < entries.Count)
            {
                var clone = Instantiate(_roundBuffTemplate, parent, false);
                clone.name = $"roundbuff{_roundBuffs.Count + 1}";
                var binds = clone.GetComponentsInChildren<Framework.UI.Binding.UIBind>(true);
                for (var b = 0; b < binds.Length; b++)
                {
                    Destroy(binds[b]);
                }

                HookRoundBuffClick(clone, _roundBuffs.Count);
                _roundBuffs.Add(clone);
                _roundBuffEntryIds.Add(0);
            }

            var shownStillPresent = false;
            for (var i = 0; i < _roundBuffs.Count; i++)
            {
                var go = _roundBuffs[i];
                if (go == null)
                {
                    continue;
                }

                BossEntryConfig entry = null;
                if (i < entries.Count)
                {
                    entry = entries[i];
                }

                _roundBuffEntryIds[i] = entry != null ? entry.Id : 0;
                if (entry != null && entry.Id == _shownRoundBuffId)
                {
                    shownStillPresent = true;
                }

                go.SetActive(entry != null);
            }

            if (_shownRoundBuffId > 0 && !shownStillPresent)
            {
                HideEquipTip();
            }
        }

        private void HookRoundBuffClick(GameObject go, int index)
        {
            if (go == null)
            {
                return;
            }

            var button = go.GetComponent<Button>();
            if (button == null)
            {
                button = go.AddComponent<Button>();
            }

            var image = go.GetComponent<Image>();
            if (image != null)
            {
                button.targetGraphic = image;
            }

            button.transition = Selectable.Transition.None;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnRoundBuffClicked(index));
        }

        private void ClearRoundBuffs()
        {
            for (var i = 0; i < _roundBuffs.Count; i++)
            {
                if (_roundBuffs[i] != null)
                {
                    Destroy(_roundBuffs[i]);
                }
            }

            _roundBuffs.Clear();
            _roundBuffEntryIds.Clear();
            _roundBuffGrid = null;
            _roundBuffTemplate = null;
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
            _enemyItemLocalScale = templateScale;
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
                item.ApplyTheme();
                item.SetAttack(0);
                BindEnemyVisible(i, clone, item);
                var visualSlot = i;
                BindSeatClick(clone, new RelayCommand(() => OnEnemyClicked(visualSlot), () => true));
                _enemyItems[i] = item;
                _enemyInfos[i] = clone;
                _enemyHomes[i] = slot;
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

                var item = _enemyItems[slot];
                if (enemy.Alive)
                {
                    CancelDeathDissolve(item);
                    item.ResetDissolve();
                    item.gameObject.SetActive(true);
                }

                item.Bind(
                    enemy,
                    PortraitLoader.Get(enemy),
                    AttackDisplay(item, enemy.Attack));
            }

            SyncEnemyCompareStand(session);
        }

        private void SyncEnemyCompareStand(GameSession session)
        {
            var centerSlot = session.CenterStandVisualSlot;
            var center = ResolveSlot("player1");
            var occupyCenter = session.CenterStandEnemy != null;
            var vacatedSide = occupyCenter && centerSlot > 0 && centerSlot < _enemyHomes.Length
                ? _enemyHomes[centerSlot]
                : null;
            for (var i = 0; i < _enemyItems.Length; i++)
            {
                var item = _enemyItems[i];
                if (item == null)
                {
                    continue;
                }

                var rt = item.transform as RectTransform;
                if (rt == null)
                {
                    continue;
                }

                if (occupyCenter && i == centerSlot && center != null)
                {
                    PlaceEnemyAt(rt, center, i);
                    continue;
                }

                if (vacatedSide != null && i == 0)
                {
                    PlaceEnemyAt(rt, vacatedSide, i);
                    continue;
                }

                PlaceEnemyAt(rt, _enemyHomes[i], i);
            }

            _enemyStandReady = true;
        }

        private void PlaceEnemyAt(RectTransform rt, Transform parent, int index)
        {
            if (rt == null || parent == null)
            {
                return;
            }

            var instant = !_enemyStandReady || !rt.gameObject.activeInHierarchy;
            if (rt.parent == parent)
            {
                if (!IsEnemySliding(index))
                {
                    SnapEnemyLocal(rt);
                }

                return;
            }

            if (instant)
            {
                KillEnemySlide(index);
                rt.SetParent(parent, false);
                SnapEnemyLocal(rt);
                return;
            }

            KillEnemySlide(index);
            var world = rt.position;
            var worldScale = rt.lossyScale;
            rt.SetParent(parent, true);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.position = world;
            if (parent.lossyScale.x != 0f && parent.lossyScale.y != 0f && parent.lossyScale.z != 0f)
            {
                rt.localScale = new Vector3(
                    worldScale.x / parent.lossyScale.x,
                    worldScale.y / parent.lossyScale.y,
                    worldScale.z / parent.lossyScale.z);
            }

            _enemySlides[index] = DOTween.Sequence()
                .Join(rt.DOAnchorPos(Vector2.zero, EnemySlideDuration).SetEase(Ease.OutCubic))
                .Join(rt.DOScale(_enemyItemLocalScale, EnemySlideDuration).SetEase(Ease.OutCubic))
                .SetTarget(rt);
        }

        private static void SnapEnemyLocal(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.identity;
        }

        private bool IsEnemySliding(int index)
        {
            return index >= 0 &&
                   index < _enemySlides.Length &&
                   _enemySlides[index] != null &&
                   _enemySlides[index].IsActive();
        }

        private void KillEnemySlide(int index)
        {
            if (index < 0 || index >= _enemySlides.Length)
            {
                return;
            }

            _enemySlides[index]?.Kill();
            _enemySlides[index] = null;
        }

        private void KillEnemySlides()
        {
            for (var i = 0; i < _enemySlides.Length; i++)
            {
                KillEnemySlide(i);
            }

            _enemyStandReady = false;
        }

        private void RefreshCardInfos()
        {
            if (ViewModel == null)
            {
                return;
            }

            var session = ViewModel.Session;
            var settling = GameTableViewModel.ShouldShowCardInfo(session);
            var preview = GameTableViewModel.ShouldShowPlayerHandPreview(session);
            if (!settling && !preview)
            {
                HideBeilvInfo();

                if (_playerCardInfo != null)
                {
                    _playerCardInfo.SetActive(false);
                }

                if (_enemyCardInfo != null)
                {
                    _enemyCardInfo.SetActive(false);
                }

                return;
            }

            if (_playerCardInfo != null)
            {
                _playerCardInfo.SetActive(true);
                if (session.Player != null)
                {
                    ApplyCardInfoFx(_playerCardInfo, session.EvaluateSeat(session.Player).Level);
                }
            }

            if (!settling)
            {
                if (_enemyCardInfo != null)
                {
                    _enemyCardInfo.SetActive(false);
                }

                return;
            }

            var displayed = session.DisplayedEnemy;
            if (displayed != null && displayed.Alive)
            {
                ApplyEnemyCardInfo(session.EvaluateSeat(displayed));
            }
            else if (_enemyCardInfo != null)
            {
                _enemyCardInfo.SetActive(false);
            }
        }

        private void ApplyEnemyCardInfo(HandScore score)
        {
            if (_enemyCardInfo == null)
            {
                return;
            }

            _enemyCardInfo.SetActive(true);
            var shownType = score.CompareType;
            ApplyCardInfoFx(_enemyCardInfo, score.CompareLevel);
            if (_enemyCardTypeIcon != null)
            {
                _enemyCardTypeIcon.sprite = ViewModel.GetCardTypeIcon(shownType);
                _enemyCardTypeIcon.enabled = _enemyCardTypeIcon.sprite != null;
                _enemyCardTypeIcon.preserveAspect = true;
            }

            if (_enemyCardTypeNum != null)
            {
                CardTypeValueSprites.Apply(
                    ViewModel.Atlas,
                    _enemyCardTypeNum,
                    GameTableViewModel.FormatHandMultiplier(shownType));
            }
        }

        private static int VisualSlot(int enemyIndex, int activeCount)
        {
            return GameSession.TableVisualSlot(enemyIndex, activeCount);
        }

        private void BindHudChrome()
        {
            var rootBack = transform.Find("backBtn");
            if (rootBack != null)
            {
                rootBack.gameObject.SetActive(false);
            }

            BindRuleBtn();
        }

        private void BindBeisu()
        {
            var slot = ResolveSlot("beisu") ?? transform.Find("beisu") ?? FindDeep(transform, "beisu");
            if (slot == null)
            {
                return;
            }

            slot.gameObject.SetActive(true);
            var button = slot.GetComponent<Button>();
            if (button == null)
            {
                button = slot.gameObject.AddComponent<Button>();
            }

            Binding.BindCommand(button, ViewModel.ToggleSpeedCommand);
            var text = slot.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                Binding.BindText(text, ViewModel.SpeedLabel);
            }

            Binding.Add(ViewModel.PlaybackSpeed.Subscribe(speed => Time.timeScale = speed));
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

            var group = EnsureCanvasGroup(_btns.gameObject);
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            BindRemainList();
            BindEquips();

            BindBtn("BlindBtn", ViewModel.BlindBetCommand, ViewModel.ShowBlind);
            BindBtn("LookBtn", ViewModel.LookCommand, ViewModel.ShowLook);
            BindBtn("RaiseBtn", ViewModel.RaiseCommand, ViewModel.ShowActions);
            BindBtn("FoldBtn", ViewModel.FoldCommand, ViewModel.ShowFold);
            BindBtn("CompareBtn", ViewModel.OpenCommand, ViewModel.ShowCompare);
            BindBtn("AllInBtn", ViewModel.AllInCommand, ViewModel.ShowAllIn);
            BindBtn("PeekGood", ViewModel.PeekGoodCommand);
            BindPeekGoodArmed();
            BindPeekGoodHoldTip();
            BindBtn("ChaKanGood", ViewModel.ChaKanGoodCommand);
            BindBtn("TiHuanGood", ViewModel.TiHuanGoodCommand);
            BindBtn("PeekBtn", ViewModel.RubCommand, ViewModel.ShowRub);
            BindBtn("CancelBtn", ViewModel.SkipRubCommand, ViewModel.ShowCancel);
            BindBtn("NextRoundBtn", ViewModel.ContinueCommand, ViewModel.ShowContinue);
            SetBtnLabel("CompareBtn", "开战");
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
            RegisterGuideTargets();
        }

        private void PrepareHudEntrance()
        {
            KillHudMotion(restoreHome: false);
            _hudFlies.Clear();
            BindOpeningVs();
            ParkHudFly("horEquipBtns2", 1f, 0f);
            ParkHudFly("horBtns2", -1f, 0f);
            ParkHudFly("stageInfo", 1f, 0f);
            var player = ResolveSlot("PlayerItem") ?? transform.Find("PlayerItem");
            ParkHudFly(player, 0f, -1f);
            var roundInfo = ResolveSlot("roundInfo") ?? transform.Find("roundInfo");
            if (roundInfo != null)
            {
                roundInfo.gameObject.SetActive(false);
            }

            var btns = transform.Find("horBtns") ?? transform.Find("btns");
            if (btns != null)
            {
                var group = EnsureCanvasGroup(btns.gameObject);
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }

        private void PlayHudEntrance(Action onComplete = null)
        {
            if (_hudFlies.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            if (_hudEntrance != null && _hudEntrance.IsActive())
            {
                _hudEntrance.Kill();
            }

            var seq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            for (var i = 0; i < _hudFlies.Count; i++)
            {
                var fly = _hudFlies[i];
                if (fly.Rt == null)
                {
                    continue;
                }

                seq.Join(fly.Rt.DOAnchorPos(fly.Home, HudFlyDuration)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .SetLink(fly.Rt.gameObject, LinkBehaviour.KillOnDestroy));
            }

            if (onComplete != null)
            {
                seq.OnComplete(() => onComplete());
            }

            _hudEntrance = seq;
        }

        private void BindOpeningVs()
        {
            var vs = ResolveSlot("VS") ?? transform.Find("VS");
            var rt = vs as RectTransform ?? (vs != null ? vs.GetComponent<RectTransform>() : null);
            _openingFx.Bind(rt);
        }

        private void TryStartOpening()
        {
            if (_waitHudForOpening || ViewModel == null || _board == null || !ViewModel.ShouldHoldDealVisual())
            {
                return;
            }

            var serial = ViewModel.Session.DealSerial;
            if (_openingPlaying && _openingForSerial == serial)
            {
                return;
            }

            if (!TryCollectOpeningTalks(_openingTalks))
            {
                return;
            }

            _openingPlaying = true;
            _openingForSerial = serial;
            BindOpeningVs();
            _openingFx.Play(_openingTalks, _playerItem, gameObject, OnOpeningComplete);
        }

        /// <summary>
        /// 中间先、其余视觉槽 0→1→2。有存活敌人但卡片未就绪则 false，等下次再试。
        /// </summary>
        private bool TryCollectOpeningTalks(List<OpeningCutscene.Talk> talks)
        {
            talks.Clear();
            var session = ViewModel != null ? ViewModel.Session : null;
            if (session == null)
            {
                return true;
            }

            ResolveOpeningCenter(session, out var centerSeat, out var centerSlot);
            if (centerSeat != null && !TryAddOpeningTalk(talks, centerSeat, centerSlot))
            {
                talks.Clear();
                return false;
            }

            for (var slot = 0; slot < _enemyItems.Length; slot++)
            {
                var seat = session.EnemyAtVisualSlot(slot);
                if (seat == null || seat == centerSeat || !seat.Alive)
                {
                    continue;
                }

                if (!TryAddOpeningTalk(talks, seat, slot))
                {
                    talks.Clear();
                    return false;
                }
            }

            return true;
        }

        private static void ResolveOpeningCenter(GameSession session, out SeatState seat, out int slot)
        {
            var center = session.CenterStandEnemy;
            if (center != null && center.Alive)
            {
                seat = center;
                slot = session.CenterStandVisualSlot;
                return;
            }

            seat = session.EnemyAtVisualSlot(0);
            slot = seat != null && seat.Alive ? 0 : -1;
            if (slot < 0)
            {
                seat = null;
            }
        }

        private bool TryAddOpeningTalk(List<OpeningCutscene.Talk> talks, SeatState seat, int slot)
        {
            var item = EnemyItemAtSlot(slot);
            if (!EnsureEnemyItemReady(item))
            {
                return false;
            }

            DialogueGroupLookup.TryPickTalk(seat.MonsterId, out var monsterLine, out var playerLine);
            talks.Add(new OpeningCutscene.Talk(item, monsterLine, playerLine));
            return true;
        }

        private PlayerItem EnemyItemAtSlot(int slot)
        {
            if (slot < 0 || slot >= _enemyItems.Length)
            {
                return null;
            }

            return _enemyItems[slot];
        }

        private bool EnsureEnemyItemReady(PlayerItem item)
        {
            if (item == null)
            {
                return false;
            }

            CancelDeathDissolve(item);
            item.ResetDissolve();
            item.gameObject.SetActive(true);
            return item.gameObject.activeInHierarchy;
        }

        private void OnOpeningComplete()
        {
            _openingPlaying = false;
            if (ViewModel != null)
            {
                ViewModel.CompleteOpening();
            }

            _board?.SyncCards();
        }

        private void ParkHudFly(string key, float xSign, float ySign)
        {
            ParkHudFly(ResolveSlot(key) ?? transform.Find(key), xSign, ySign);
        }

        private void ParkHudFly(Transform node, float xSign, float ySign)
        {
            var rt = node as RectTransform ?? (node != null ? node.GetComponent<RectTransform>() : null);
            if (rt == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            var home = rt.anchoredPosition;
            _hudFlies.Add(new HudFly { Rt = rt, Home = home });
            rt.anchoredPosition = home + FlyDelta(rt, xSign, ySign);
        }

        private static Vector2 FlyDelta(RectTransform rt, float xSign, float ySign)
        {
            var size = rt.rect.size;
            var w = Mathf.Max(size.x, 160f) + 48f;
            var h = Mathf.Max(size.y, 220f) + 48f;
            return new Vector2(xSign * w, ySign * h);
        }

        private void BindFadeActive(GameObject go, ObservableProperty<bool> visible)
        {
            if (go == null || visible == null)
            {
                return;
            }

            var group = EnsureCanvasGroup(go);
            var first = true;
            Binding.Add(visible.Subscribe(show =>
            {
                PlayHudFade(go, group, show, instant: first);
                first = false;
            }));
        }

        private void PlayHudFade(GameObject go, CanvasGroup group, bool show, bool instant)
        {
            if (go == null || group == null)
            {
                return;
            }

            if (_hudFades.TryGetValue(go, out var running) && running != null && running.IsActive())
            {
                running.Kill();
            }

            _hudFades.Remove(go);
            if (instant)
            {
                go.SetActive(show);
                group.alpha = show ? 1f : 0f;
                group.interactable = show;
                group.blocksRaycasts = show;
                return;
            }

            if (show)
            {
                var wasActive = go.activeSelf;
                go.SetActive(true);
                if (!wasActive)
                {
                    group.alpha = 0f;
                }

                group.interactable = true;
                group.blocksRaycasts = true;
                var tween = group.DOFade(1f, HudFadeDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(go, LinkBehaviour.KillOnDestroy);
                _hudFades[go] = tween;
                return;
            }

            group.interactable = false;
            group.blocksRaycasts = false;
            if (!go.activeSelf)
            {
                group.alpha = 0f;
                return;
            }

            var hide = group.DOFade(0f, HudFadeDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(go, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    if (go != null)
                    {
                        go.SetActive(false);
                    }

                    _hudFades.Remove(go);
                });
            _hudFades[go] = hide;
        }

        private void KillHudMotion(bool restoreHome)
        {
            if (_hudEntrance != null && _hudEntrance.IsActive())
            {
                _hudEntrance.Kill();
            }

            _hudEntrance = null;
            foreach (var pair in _hudFades)
            {
                if (pair.Value != null && pair.Value.IsActive())
                {
                    pair.Value.Kill();
                }
            }

            _hudFades.Clear();
            if (restoreHome)
            {
                for (var i = 0; i < _hudFlies.Count; i++)
                {
                    var fly = _hudFlies[i];
                    if (fly.Rt != null)
                    {
                        fly.Rt.anchoredPosition = fly.Home;
                    }
                }
            }

            _hudFlies.Clear();
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
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

            BindImage("cardinfoItem", ViewModel.CardTypeIcon);
            var num = ResolveSlot("cardtypeNum");
            _playerCardTypeNum = num;
            if (num != null)
            {
                CardTypeValueSprites.Prepare(num);
                Binding.Add(ViewModel.CardTypeNum.Subscribe(
                    text => CardTypeValueSprites.Apply(ViewModel.Atlas, num, text),
                    emitCurrent: true));
            }

            BindEnemyCardInfo();
        }

        private void BindEnemyCardInfo()
        {
            var root = ResolveSlot("cardinfoEnemyItem");
            if (root != null)
            {
                _enemyCardInfo = root.gameObject;
                _enemyCardInfo.SetActive(false);
                _enemyCardTypeIcon = root.GetComponent<Image>();
            }

            var num = ResolveSlot("cardinfoEnemyNum") ?? ResolveSlot("cardtypeEnemyNum");
            if (num == null && root != null)
            {
                num = root.Find("cardinfoEnemyNum") ??
                      root.Find("cardtypeEnemyNum") ??
                      FindDeep(root, "cardinfoEnemyNum") ??
                      FindDeep(root, "cardtypeEnemyNum");
            }

            _enemyCardTypeNum = num;
            if (num != null)
            {
                CardTypeValueSprites.Prepare(num);
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

            Binding.Add(source.Subscribe(sprite =>
            {
                image.sprite = sprite;
                image.enabled = sprite != null;
                image.preserveAspect = true;
            }));
        }

        private void BindRemainList()
        {
            var node = ResolveSlot("horEquipBtns2") ?? transform.Find("horEquipBtns2");
            if (node != null)
            {
                node.gameObject.SetActive(true);
            }

            var slot = ResolveSlot("yiwuBtn");
            if (slot == null)
            {
                return;
            }

            Binding.BindActive(slot.gameObject, ViewModel.ShowYiwuBtn);
            var button = slot.GetComponent<Button>();
            if (button == null)
            {
                button = slot.gameObject.AddComponent<Button>();
            }

            Binding.BindCommand(button, ViewModel.OpenRemainListCommand);
        }

        private void BindRuleBtn()
        {
            var slot = ResolveSlot("ruleBtn") ?? transform.Find("ruleBtn") ?? FindDeep(transform, "ruleBtn");
            if (slot == null)
            {
                return;
            }

            slot.gameObject.SetActive(true);
            var button = slot.GetComponent<Button>();
            if (button == null)
            {
                button = slot.gameObject.AddComponent<Button>();
            }

            button.onClick.RemoveListener(OnRuleClicked);
            button.onClick.AddListener(OnRuleClicked);
        }

        private void BindEquips()
        {
            _equipSlots.Clear();
            _equipRelicIds.Clear();
            var node = ResolveSlot("horEquipBtns2") ?? transform.Find("horEquipBtns2");
            if (node != null)
            {
                node.gameObject.SetActive(true);
            }

            for (var i = 0; i < EquipSlotKeys.Length; i++)
            {
                var slot = ResolveSlot(EquipSlotKeys[i]);
                if (slot != null)
                {
                    HideChild(slot, "Text (TMP)");
                    HookEquipClick(slot, _equipSlots.Count);
                    _equipSlots.Add(slot);
                    _equipRelicIds.Add(0);
                }
            }
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

        private void OnPlayerClicked()
        {
            if (ViewModel?.Session?.Player == null || !ViewModel.Session.Player.Alive)
            {
                HideEquipTip();
                return;
            }

            if (_shownPlayerTip && _equipTip != null && _equipTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            _ = ShowPlayerTip();
        }

        private void OnEnemyClicked(int slot)
        {
            var session = ViewModel?.Session;
            if (session == null)
            {
                return;
            }

            if (session.CanAttackSlot(slot))
            {
                session.AttackEnemyAtSlot(slot);
                return;
            }

            var enemy = session.EnemyAtVisualSlot(slot);
            if (enemy == null || !enemy.Alive)
            {
                HideEquipTip();
                return;
            }

            if (_shownEnemySlot == slot && _equipTip != null && _equipTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            _ = ShowEnemyTip(slot);
        }

        private void OnRoundBuffClicked(int index)
        {
            if (index < 0 || index >= _roundBuffEntryIds.Count || _roundBuffEntryIds[index] <= 0)
            {
                HideEquipTip();
                return;
            }

            var entryId = _roundBuffEntryIds[index];
            if (_shownRoundBuffId == entryId && _equipTip != null && _equipTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            var entry = BossEntryConfig.Get(entryId);
            var slot = index < _roundBuffs.Count ? _roundBuffs[index] : null;
            if (entry == null || slot == null)
            {
                HideEquipTip();
                return;
            }

            _ = ShowRoundBuffTip(slot.transform, entry);
        }

        private void OnRuleClicked()
        {
            if (_shownWinTip && _winTip != null && _winTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            HideEquipTip();
            _ = ShowWinTip();
        }

        private async Task ShowRoundBuffTip(Transform slot, BossEntryConfig entry)
        {
            await EnsureEquipTip();
            if (_equipTip == null || entry == null)
            {
                return;
            }

            _shownEquipRelicId = 0;
            _shownRoundBuffId = entry.Id;
            _shownPlayerTip = false;
            _shownEnemySlot = -1;
            var name = entry.Name ?? string.Empty;
            var desc = string.IsNullOrEmpty(entry.Desc) ? name : entry.Desc;
            if (string.Equals(desc, name, StringComparison.Ordinal))
            {
                desc = string.Empty;
            }

            await PresentItemTip(slot, name, desc, showUse: false, placeRight: false);
        }

        private async Task ShowWinTip()
        {
            await EnsureWinTip();
            if (_winTip == null)
            {
                return;
            }

            FillWinTipRows();
            _shownWinTip = true;
            EnsureEquipTipCatcher();
            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(true);
                _equipTipCatcher.transform.SetAsLastSibling();
            }

            _winTip.SetActive(true);
            Canvas.ForceUpdateCanvases();
            var tipRt = _winTip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }

            _winTip.transform.SetAsLastSibling();
            var slot = ResolveSlot("ruleBtn") ?? transform.Find("ruleBtn") ?? FindDeep(transform, "ruleBtn");
            if (slot != null)
            {
                PositionTip(_winTip, slot, placeRight: false);
            }
        }

        private async Task EnsureWinTip()
        {
            if (_winTip != null || ViewModel == null || ViewModel.Resources == null)
            {
                return;
            }

            try
            {
                var prefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.WinTip);
                if (prefab == null)
                {
                    return;
                }

                _winTip = Instantiate(prefab, transform, false);
                _winTip.name = "WinTip";
                var ui = _winTip.GetComponent<UIReference>();
                if (ui != null && ui.TryGet<Component>("cardinfoEnemyItem", out var item) && item != null)
                {
                    _winTipTemplate = item.transform;
                }

                if (_winTipTemplate == null)
                {
                    _winTipTemplate = _winTip.transform.childCount > 0 ? _winTip.transform.GetChild(0) : null;
                }

                if (_winTipTemplate != null)
                {
                    _winTipTemplate.gameObject.SetActive(false);
                }

                var group = _winTip.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = _winTip.AddComponent<CanvasGroup>();
                }

                group.blocksRaycasts = true;
                group.interactable = true;
                _winTip.SetActive(false);
                _hudCanvas = GetComponentInParent<Canvas>();
            }
            catch (Exception)
            {
            }
        }

        private void FillWinTipRows()
        {
            if (_winTip == null || _winTipTemplate == null || ViewModel == null)
            {
                return;
            }

            var types = CollectWinTipHandTypes();
            while (_winTipRows.Count < types.Count)
            {
                var clone = Instantiate(_winTipTemplate.gameObject, _winTip.transform, false);
                clone.name = $"cardinfoItem{_winTipRows.Count + 1}";
                var binds = clone.GetComponentsInChildren<Framework.UI.Binding.UIBind>(true);
                for (var b = 0; b < binds.Length; b++)
                {
                    Destroy(binds[b]);
                }

                clone.SetActive(true);
                _winTipRows.Add(clone);
            }

            for (var i = 0; i < _winTipRows.Count; i++)
            {
                var row = _winTipRows[i];
                if (row == null)
                {
                    continue;
                }

                if (i >= types.Count)
                {
                    row.SetActive(false);
                    continue;
                }

                row.SetActive(true);
                ApplyWinTipRow(row.transform, types[i]);
            }
        }

        private static List<HandType> CollectWinTipHandTypes()
        {
            var types = new List<HandType>(6);
            var rows = new List<HandScoreConfig>(6);
            foreach (var kv in HandScoreConfig.All)
            {
                if (kv.Value != null)
                {
                    rows.Add(kv.Value);
                }
            }

            rows.Sort((a, b) => b.Level.CompareTo(a.Level));
            for (var i = 0; i < rows.Count; i++)
            {
                var type = HandEvaluator.FromConfigHandType((int)rows[i].Type);
                if (!types.Contains(type))
                {
                    types.Add(type);
                }
            }

            if (types.Count > 0)
            {
                return types;
            }

            types.Add(HandType.ThreeOfAKind);
            types.Add(HandType.StraightFlush);
            types.Add(HandType.Flush);
            types.Add(HandType.Straight);
            types.Add(HandType.Pair);
            types.Add(HandType.HighCard);
            return types;
        }

        private void ApplyWinTipRow(Transform row, HandType type)
        {
            if (row == null)
            {
                return;
            }

            var icon = row.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = ViewModel.GetCardTypeIcon(type);
                icon.enabled = icon.sprite != null;
                icon.preserveAspect = true;
            }

            var num = row.Find("cardtypeEnemyNum") ?? FindDeep(row, "cardtypeEnemyNum");
            if (num == null)
            {
                return;
            }

            var anim = num.GetComponent<Animator>();
            if (anim != null)
            {
                anim.enabled = false;
            }

            var mag = HandEvaluator.TypeMultiplier(type);
            var run = ViewModel.Session != null ? ViewModel.Session.Run : null;
            if (run != null)
            {
                mag += run.HandTypeMagBonus(type);
            }

            CardTypeValueSprites.Prepare(num);
            CardTypeValueSprites.Apply(ViewModel.Atlas, num, GameTableViewModel.FormatMultiplier(mag));
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
            _shownRoundBuffId = 0;
            _shownPlayerTip = false;
            _shownEnemySlot = -1;
            await PresentItemTip(slot, relic.Name, relic.Desc, RelicMechanics.IsConsumable(relic), placeRight: false);
        }

        private async Task ShowPlayerTip()
        {
            var hero = HeroMechanics.Resolve(ViewModel?.Session?.Run);
            if (hero == null || _playerItem == null)
            {
                return;
            }

            _shownEquipRelicId = 0;
            _shownRoundBuffId = 0;
            _shownPlayerTip = true;
            _shownEnemySlot = -1;
            await PresentItemTip(_playerItem.transform, hero.Name, BuildHeroTipBody(hero), showUse: false, placeRight: true);
        }

        private async Task ShowEnemyTip(int slot)
        {
            var enemy = ViewModel?.Session?.EnemyAtVisualSlot(slot);
            var anchor = slot >= 0 && slot < _enemyInfos.Length ? _enemyInfos[slot] : null;
            if (enemy == null || !enemy.Alive || anchor == null)
            {
                return;
            }

            _shownEquipRelicId = 0;
            _shownRoundBuffId = 0;
            _shownPlayerTip = false;
            _shownEnemySlot = slot;
            await PresentItemTip(
                anchor.transform,
                enemy.Name ?? string.Empty,
                BuildEnemyTipBody(enemy),
                showUse: false,
                placeRight: false);
        }

        private async Task PresentItemTip(Transform slot, string title, string body, bool showUse, bool placeRight)
        {
            await EnsureEquipTip();
            if (_equipTip == null)
            {
                return;
            }

            HideWinTip();
            _equipTipAnchor = slot;
            SetEquipTipTexts(title, body);
            SetEquipTipUseVisible(showUse);
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
            if (slot != null)
            {
                PositionTip(_equipTip, slot, placeRight);
            }
        }

        private void HideEquipTip()
        {
            _shownEquipRelicId = 0;
            _shownRoundBuffId = 0;
            _shownPlayerTip = false;
            _shownEnemySlot = -1;
            _shownPeekGoodTip = false;
            _equipTipAnchor = null;
            if (_equipTip != null)
            {
                _equipTip.SetActive(false);
            }

            HideWinTip();
            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(false);
            }
        }

        private void HideWinTip()
        {
            _shownWinTip = false;
            if (_winTip != null)
            {
                _winTip.SetActive(false);
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

        private void PositionTip(GameObject tip, Transform slot, bool placeRight)
        {
            var tipRt = tip != null ? tip.GetComponent<RectTransform>() : null;
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
            ItemTipPlacement.ClampToParent(tipRt, parent);
        }

        private void BindEquipTipNodes(GameObject tip)
        {
            _equipTipTitle = null;
            _equipTipText = null;
            _equipTipUse = null;
            _equipTipUseBtn = null;
            var ui = tip != null ? tip.GetComponent<UIReference>() : null;
            if (ui != null && ui.TryGet<Component>("title", out var title) && title != null)
            {
                _equipTipTitle = title.GetComponent<TMP_Text>() ?? title.GetComponentInChildren<TMP_Text>(true);
            }

            if (ui != null && ui.TryGet<Component>("tipContext", out var context) && context != null)
            {
                _equipTipText = context.GetComponent<TMP_Text>() ?? context.GetComponentInChildren<TMP_Text>(true);
            }

            if (_equipTipText == null && tip != null)
            {
                var texts = tip.GetComponentsInChildren<TMP_Text>(true);
                for (var i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == _equipTipTitle)
                    {
                        continue;
                    }

                    _equipTipText = texts[i];
                    break;
                }
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

        private void SetEquipTipTexts(string title, string body)
        {
            if (_equipTipTitle != null)
            {
                var text = title ?? string.Empty;
                _equipTipTitle.text = text;
                _equipTipTitle.gameObject.SetActive(!string.IsNullOrEmpty(text));
            }

            if (_equipTipText != null)
            {
                _equipTipText.text = body ?? string.Empty;
            }
        }

        private void SetEquipTipUseVisible(bool visible)
        {
            if (_equipTipUse != null)
            {
                _equipTipUse.SetActive(visible);
            }
        }

        private string BuildHeroTipBody(HeroConfig hero)
        {
            var desc = string.Empty;
            if (hero != null && !string.IsNullOrEmpty(hero.Desc))
            {
                desc = hero.Desc;
            }
            else if (hero != null)
            {
                var parts = new List<string>();
                HeroMechanics.ForEachEntry(hero, entry =>
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Desc))
                    {
                        parts.Add(entry.Desc);
                    }
                });
                desc = string.Join("\n", parts);
            }

            var session = ViewModel?.Session;
            var stats = session != null
                ? session.ResolveLivePlayerPanel()
                : TalentBonusManager.EvaluateBase(hero);
            return TalentBonusManager.AppendPanelLines(desc, stats);
        }

        private string BuildEnemyTipBody(SeatState enemy)
        {
            if (enemy == null)
            {
                return string.Empty;
            }

            var monster = FindMonster(enemy.MonsterId);
            var desc = monster != null ? monster.Desc ?? string.Empty : string.Empty;
            var session = ViewModel?.Session;
            var stats = session != null
                ? session.ResolveLiveEnemyPanel(enemy)
                : new HeroPanelStats(enemy.Attack, 0f, 0f, enemy.MaxHp);
            return TalentBonusManager.AppendPanelLines(desc, stats);
        }

        private static MonsterConfig FindMonster(int monsterId)
        {
            if (monsterId <= 0)
            {
                return null;
            }

            MonsterConfig best = null;
            foreach (var kv in MonsterConfig.All)
            {
                var row = kv.Value;
                if (row == null || row.MonsterId != monsterId)
                {
                    continue;
                }

                if (best == null || row.MonsterLevel < best.MonsterLevel)
                {
                    best = row;
                }
            }

            return best;
        }

        private void OnEquipTipUseClicked()
        {
            if (_shownEquipRelicId <= 0 || ViewModel?.Session == null)
            {
                return;
            }

            ViewModel.Session.UseRelic(_shownEquipRelicId);
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
                HideChild(clone.transform, "Text (TMP)");
                _equipSlots.Add(clone.transform);
                _equipRelicIds.Add(0);
            }

            while (_equipRelicIds.Count < _equipSlots.Count)
            {
                _equipRelicIds.Add(0);
            }

            if (_beilvInfo != null)
            {
                _beilvInfo.SetAsLastSibling();
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

                slot.gameObject.SetActive(relic != null);
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

        private void BindPeekGoodHoldTip()
        {
            if (_peekGoodBtn == null)
            {
                return;
            }

            var trigger = _peekGoodBtn.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = _peekGoodBtn.gameObject.AddComponent<EventTrigger>();
            }

            AddEventTrigger(trigger, EventTriggerType.PointerDown, OnPeekGoodPointerDown);
            AddEventTrigger(trigger, EventTriggerType.PointerUp, OnPeekGoodPointerUp);
            AddEventTrigger(trigger, EventTriggerType.PointerExit, OnPeekGoodPointerUp);
        }

        private static void AddEventTrigger(
            EventTrigger trigger,
            EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        private void OnPeekGoodPointerDown(BaseEventData _)
        {
            _peekHoldFired = false;
            StopPeekHold();
            _peekHoldCo = StartCoroutine(PeekGoodHoldRoutine());
        }

        private void OnPeekGoodPointerUp(BaseEventData data)
        {
            StopPeekHold();
            if (_peekHoldFired && data is PointerEventData pointer)
            {
                pointer.eligibleForClick = false;
            }
        }

        private IEnumerator PeekGoodHoldRoutine()
        {
            yield return new WaitForSecondsRealtime(0.45f);
            _peekHoldFired = true;
            _peekHoldCo = null;
            _ = ShowPeekGoodTip();
        }

        private void StopPeekHold()
        {
            if (_peekHoldCo == null)
            {
                return;
            }

            StopCoroutine(_peekHoldCo);
            _peekHoldCo = null;
        }

        private async Task ShowPeekGoodTip()
        {
            if (_peekGoodBtn == null)
            {
                return;
            }

            if (_shownPeekGoodTip && _equipTip != null && _equipTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            _shownEquipRelicId = 0;
            _shownRoundBuffId = 0;
            _shownPlayerTip = false;
            _shownEnemySlot = -1;
            await PresentItemTip(
                _peekGoodBtn.transform,
                "搓牌",
                "点选一张手牌，将其替换为牌堆中的一张新牌。",
                showUse: false,
                placeRight: false);
            _shownPeekGoodTip = true;
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
            BindFadeActive(button.gameObject, visible);
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
            BindFadeActive(button.gameObject, visible);
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
                var skills = transform.Find("horBtns2") ?? ResolveSlot("horBtns2");
                if (skills != null)
                {
                    node = skills.Find(name) ?? FindDeep(skills, name);
                }
            }

            if (node == null)
            {
                node = FindDeep(transform, name);
                if (node != null)
                {
                    var equip = transform.Find("horEquipBtns2") ?? ResolveSlot("horEquipBtns2");
                    if (equip != null && (node == equip || node.IsChildOf(equip)))
                    {
                        node = null;
                    }
                }
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

        private static void ApplyCardInfoFx(GameObject root, int level)
        {
            if (root == null)
            {
                return;
            }

            var flow = root.GetComponent<UiFlowLight>();
            if (flow != null)
            {
                if (level > 3)
                {
                    flow.Play();
                }
                else
                {
                    flow.Stop();
                }
            }

            SetChildActive(root.transform, "PokerHandStraightFlush01", level == 5);
            SetChildActive(root.transform, "PokerHandBomb01", level == 6);
        }

        private static void SetChildActive(Transform root, string name, bool active)
        {
            var child = root.Find(name);
            if (child == null)
            {
                child = FindDeep(root, name);
            }

            if (child != null)
            {
                child.gameObject.SetActive(active);
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

        private struct HudFly
        {
            public RectTransform Rt;
            public Vector2 Home;
        }
    }
}
