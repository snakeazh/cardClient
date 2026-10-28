using System;
using App.Atlas;
using CardShare.Contracts.Config;
using App.UI;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>
    /// 局内角色/敌人信息卡。人物用 card，敌人用 enemycard，头像与数值在 <see cref="Bind"/> 时赋值。
    /// 人物 attack / heart 底图用预制体；敌人 enemycardattack / enemycardheart 按 MonsterType
    /// 取 HealthBarBigBaseFrameKhaki / Purple / Red。
    /// card 下 bg/direct/di 三层卡背取 Altas/playitem 的 {品质}CardFrameBack{1,2,3}（稀有为 Blue 系列）；
    /// 怪物头像取 Altas/enemy（{Icon}_attack/_damage/_dead）；enemycard 卡面底图取 BaseMap。
    /// heart 文案为当前/上限，healthbar 按百分比改 width。
    /// 卡面/标题底等其余节点颜色以预制体为准，代码不染色。
    /// </summary>
    public sealed class PlayerItem : MonoBehaviour
    {
        public const string LockedStatText = "???";

        private static readonly Color UnlockedPortraitColor = Color.white;
        private static readonly Color LockedPortraitColor = new Color(0f, 0f, 0f, 1f);

        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardIcon;
        [SerializeField] private Image cardBg;
        [SerializeField] private Image cardQualityBg;
        [SerializeField] private Image cardQualityDirect;
        [SerializeField] private Image cardQualityDi;
        [SerializeField] private GameObject cardMask;
        [SerializeField] private TMP_Text cardAttackValue;
        [SerializeField] private Animator attackValueAnimator;
        [SerializeField] private TMP_Text cardAttackHeart;
        [SerializeField] private GameObject attackRoot;
        [SerializeField] private Image attackBg;
        [SerializeField] private Image heartBg;
        [SerializeField] private Image healthBar;
        [SerializeField] private TMP_Text enemyCardName;
        [SerializeField] private Image enemyCardIcon;
        [SerializeField] private Image enemyCardBg;
        [SerializeField] private TMP_Text enemyCardAttackValue;
        [SerializeField] private Animator enemyAttackValueAnimator;
        [SerializeField] private TMP_Text enemyCardAttackHeart;
        [SerializeField] private GameObject enemyAttackRoot;
        [SerializeField] private Image enemyAttackBg;
        [SerializeField] private Image enemyHeartBg;
        [SerializeField] private Image enemyHealthBar;
        [SerializeField] private RectTransform playerRoot;
        [SerializeField] private Animator playerAnimator;
        [SerializeField] private GameObject topDialog;
        [SerializeField] private TMP_Text topDialogText;
        [SerializeField] private GameObject bottomDialog;
        [SerializeField] private TMP_Text bottomDialogText;

        public const float DialogSpeed = 2f;
        public const float DialogShowDuration = 0.2f / DialogSpeed;
        public const float DialogHideDuration = 0.15f / DialogSpeed;
        public const float DialogCharInterval = 0.08f / DialogSpeed;
        /// <summary>伤害结算时 attack / enemycardattack 果冻弹出时长。</summary>
        public const float AttackPopDuration = 0.4f;
        /// <summary>攻块布局默认缩放（与 PlayerItem 预制体 attack / enemycardattack 一致）。</summary>
        public const float AttackStatLayoutScale = 0.55f;

        private bool _stateHidden;
        private bool _enemyVisual;
        private bool _visualReady;
        // SetSelectLift 已应用的选中态（-1=未应用过）：行复用重绑时选中态基本不变，幂等跳过
        private int _selectLiftApplied = -1;
        // 引用只解析一次（树结构固定），见 EnsureRefs
        private bool _refsResolved;
        private bool _dialogRefsReady;
        private Tween _dialogTween;
        private Tween _attackPopTween;
        private Vector3 _attackRestScale = Vector3.one * AttackStatLayoutScale;
        private Vector3 _enemyAttackRestScale = Vector3.one * AttackStatLayoutScale;
        private GameObject _activeDialog;
        // card / enemycard 节点引用：显隐切换按节点做，不依赖节点上是否挂 Image
        // （预制体 card 节点只有 RectTransform+CanvasRenderer，按 Image 查找会漏）
        private GameObject _cardRoot;
        private GameObject _enemyCardRoot;

        private TMP_Text ActiveName => _enemyVisual && enemyCardName != null ? enemyCardName : cardName;
        private Image ActiveIcon => _enemyVisual && enemyCardIcon != null ? enemyCardIcon : cardIcon;
        private TMP_Text ActiveAttackValue =>
            _enemyVisual && enemyCardAttackValue != null ? enemyCardAttackValue : cardAttackValue;
        private Animator ActiveAttackAnimator =>
            _enemyVisual && enemyAttackValueAnimator != null ? enemyAttackValueAnimator : attackValueAnimator;
        private GameObject ActiveAttackRoot =>
            _enemyVisual && enemyAttackRoot != null ? enemyAttackRoot : attackRoot;
        private Image ActiveHeartBg => _enemyVisual && enemyHeartBg != null ? enemyHeartBg : heartBg;
        // 敌人形态必须用 enemycardheart 下的条；找不到时不要回落到人物 heart（会改到隐藏节点上）。
        private Image ActiveHealthBar => _enemyVisual ? enemyHealthBar : healthBar;
        private float ActiveHealthBarFullWidth =>
            _enemyVisual ? _enemyHealthBarFullWidth : _healthBarFullWidth;
        private TMP_Text ActiveHeart =>
            _enemyVisual && enemyCardAttackHeart != null ? enemyCardAttackHeart : cardAttackHeart;

        // healthbar 满宽（Sliced + 改 width）；EnsureRefs 时从预制体 sizeDelta.x 记下
        private float _healthBarFullWidth;
        private float _enemyHealthBarFullWidth;

        public RectTransform CardIconRect
        {
            get
            {
                EnsureRefs();
                var icon = ActiveIcon;
                return icon != null ? icon.rectTransform : null;
            }
        }

        public RectTransform RootRect
        {
            get
            {
                EnsureRefs();
                return playerRoot;
            }
        }

        public Animator RootAnimator
        {
            get
            {
                EnsureRefs();
                return playerAnimator;
            }
        }

        /// <summary>
        /// 播放 PlayerRoot 上 Animator 的指定状态。预制体挂 PlayerRoot.controller（无参数，靠状态名播放）。
        /// </summary>
        public void PlayAnimation(string stateName, int layer = 0, float normalizedTime = 0f)
        {
            EnsureRefs();
            if (playerAnimator == null || string.IsNullOrEmpty(stateName))
            {
                return;
            }

            playerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            playerAnimator.enabled = true;
            playerAnimator.speed = 1f;
            playerAnimator.Play(stateName, layer, normalizedTime);
            playerAnimator.Update(0f);
        }

        /// <summary>
        /// 选角抬卡。取消选中必须关掉 Animator 再把 card Y 打回 0：默认 clip 不写该曲线，
        /// 且 Play 同一状态不会重头播，否则会一直停在抬起高度。
        /// </summary>
        public void SetSelectLift(bool selected, string selectState, string idleState, bool force = false)
        {
            EnsureRefs();
            // 幂等：列表行复用重绑时绝大多数是未选中态，直接跳过，避免每次重绑都写
            // Animator 属性与 anchoredPosition（Transform setter 不做相同值检查，
            // 每次写都标脏 → SyncTransform/Rebuild，是滚动重绑尖峰的主要构成之一）。
            // force：池租用复位用——上任视图可能绕过本方法直接动过卡，缓存不可信
            var applied = selected ? 1 : 0;
            if (!force && _selectLiftApplied == applied)
            {
                return;
            }

            _selectLiftApplied = applied;
            var card = ResolveAnimatedCard();
            if (playerAnimator == null)
            {
                SetAnchoredY(card, 0f);
                return;
            }

            playerAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            playerAnimator.speed = 1f;
            if (!selected)
            {
                playerAnimator.enabled = false;
                SetAnchoredY(card, 0f);
                return;
            }

            playerAnimator.enabled = true;
            if (!string.IsNullOrEmpty(idleState))
            {
                playerAnimator.Play(idleState, 0, 0f);
                playerAnimator.Update(0f);
            }

            SetAnchoredY(card, 0f);
            playerAnimator.Play(selectState, 0, 0f);
            playerAnimator.Update(0f);
        }

        private RectTransform ResolveAnimatedCard()
        {
            EnsureRefs();
            if (cardIcon != null)
            {
                return cardIcon.rectTransform.parent as RectTransform;
            }

            if (playerRoot == null)
            {
                return null;
            }

            var frame = playerRoot.Find("cardFrame");
            return frame != null ? frame.Find("card") as RectTransform : null;
        }

        private static void SetAnchoredY(RectTransform rect, float y)
        {
            if (rect == null)
            {
                return;
            }

            var pos = rect.anchoredPosition;
            if (pos.y == y)
            {
                return;
            }

            pos.y = y;
            rect.anchoredPosition = pos;
        }

        public RectTransform AttackValueRect
        {
            get
            {
                EnsureRefs();
                var value = ActiveAttackValue;
                return value != null ? value.rectTransform : null;
            }
        }

        public Animator AttackValueAnimator
        {
            get
            {
                EnsureRefs();
                return ActiveAttackAnimator;
            }
        }

        public bool HasDialog(bool top)
        {
            EnsureRefs();
            return ResolveDialog(top) != null;
        }

        public static float DialogPlayDuration(string text)
        {
            var n = string.IsNullOrEmpty(text) ? 0 : text.Length;
            return DialogShowDuration + n * DialogCharInterval;
        }

        /// <summary>
        /// 弹出 TopDialog（卡上方）或 BottomDialog（卡下方）。开场对峙：敌人 top，人物 bottom。
        /// 气泡入场后按字逐个显示。返回入场 + 打字 tween。
        /// </summary>
        public Tween PlayDialog(bool top, string text)
        {
            EnsureRefs();
            KillDialogTween();
            HideDialogRoots();
            var root = ResolveDialog(top);
            var label = ResolveDialogText(top);
            if (root == null)
            {
                return null;
            }

            var full = text ?? string.Empty;
            root.SetActive(true);
            if (label != null)
            {
                label.text = full;
                label.maxVisibleCharacters = 0;
                label.ForceMeshUpdate();
            }

            var rt = root.transform as RectTransform;
            // 气泡带 ContentSizeFitter。点其他按钮会触发布局重算，高度一变中心轴就会把气泡顶走。先按全文排好再关掉。
            FreezeDialogLayout(rt);
            _activeDialog = root;
            var group = EnsureCanvasGroup(root);
            group.alpha = 0f;
            if (rt != null)
            {
                rt.localScale = Vector3.one * 0.7f;
            }

            var seq = DOTween.Sequence().SetLink(root, LinkBehaviour.KillOnDestroy);
            seq.Join(group.DOFade(1f, DialogShowDuration));
            if (rt != null)
            {
                seq.Join(rt.DOScale(1f, DialogShowDuration).SetEase(Ease.OutBack));
            }

            if (label != null && full.Length > 0)
            {
                var reveal = full.Length;
                seq.Append(DOTween.To(
                        () => label.maxVisibleCharacters,
                        value => label.maxVisibleCharacters = value,
                        reveal,
                        reveal * DialogCharInterval)
                    .SetEase(Ease.Linear));
            }

            _dialogTween = seq;
            return seq;
        }

        public Tween HideDialog()
        {
            EnsureRefs();
            KillDialogTween();
            var root = _activeDialog;
            if (root == null || !root.activeSelf)
            {
                HideDialogRoots();
                return null;
            }

            var group = EnsureCanvasGroup(root);
            var tween = group.DOFade(0f, DialogHideDuration)
                .SetLink(root, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    if (root != null)
                    {
                        root.SetActive(false);
                    }

                    if (_activeDialog == root)
                    {
                        _activeDialog = null;
                    }
                });
            _dialogTween = tween;
            return tween;
        }

        public void HideDialogImmediate()
        {
            KillDialogTween();
            HideDialogRoots();
        }

        public Tween PlayDissolve(float duration = -1f, Action onComplete = null)
        {
            var dissolve = GetComponent<UiDissolve>();
            if (dissolve == null)
            {
                dissolve = gameObject.AddComponent<UiDissolve>();
            }

            return dissolve.Play(duration, onComplete);
        }

        public void ResetDissolve()
        {
            var dissolve = GetComponent<UiDissolve>();
            dissolve?.ResetState();
        }

        public void Bind(SeatState seat, Sprite portrait, int attack = 0)
        {
            EnsureRefs();
            // 怪物走 enemycard；PVP 玩家对手（DisplayAsHero）走人物 card，攻/血底用预制体 healthbar。
            var asMonster = seat != null && !seat.IsPlayer && !seat.DisplayAsHero;
            SetEnemyVisual(asMonster);
            if (asMonster)
            {
                ApplyMonsterFrames(seat.MonsterId);
            }
            else
            {
                ApplyTheme();
            }

            SetName(seat != null ? seat.Name : string.Empty);
            SetAttack(attack);
            SetHp(
                seat != null ? seat.Hp : 0,
                seat != null ? seat.MaxHp : 0,
                hideWhenZero: false);
            SetPortrait(portrait);
            SetCardMaskVisible(false);
        }

        public void ApplyTheme(QualityType quality = QualityType.Ordinary)
        {
            EnsureRefs();
            SetEnemyVisual(false);
            ApplyQualityCardBack(quality);
            SetCardMaskVisible(false);
        }

        /// <summary>敌人形态：启用 enemycard 并按 MonsterId 取 BaseMap。图鉴等非对局场景使用。</summary>
        public void ApplyEnemyTheme(int monsterId)
        {
            EnsureRefs();
            SetEnemyVisual(true);
            ApplyMonsterFrames(monsterId);
        }

        private void SetEnemyVisual(bool enemy)
        {
            if (enemy && _enemyCardRoot == null)
            {
                enemy = false;
            }

            _enemyVisual = enemy;
            _visualReady = true;
            if (_cardRoot != null && _cardRoot.activeSelf == enemy)
            {
                _cardRoot.SetActive(!enemy);
            }

            if (_enemyCardRoot != null && _enemyCardRoot.activeSelf != enemy)
            {
                _enemyCardRoot.SetActive(enemy);
            }
        }

        /// <summary>
        /// card/bg、card/bg/direct、card/bg/direct/di 三层卡背按品质取 Altas/playitem 的
        /// {品质}CardFrameBack{1,2,3}（稀有为 BlueCardFrame{1,2,3}）。目标品质缺图时回退
        /// 普通品质，图集整体不可用时保留当前图。
        /// </summary>
        private void ApplyQualityCardBack(QualityType quality)
        {
            ApplyQualityLayer(cardQualityBg, quality, 1);
            ApplyQualityLayer(cardQualityDirect, quality, 2);
            ApplyQualityLayer(cardQualityDi, quality, 3);
        }

        private static void ApplyQualityLayer(Image target, QualityType quality, int layer)
        {
            if (target == null)
            {
                return;
            }

            var sprite = PlayItemSpriteLibrary.GetCardFrameBack(quality, layer);
            if (sprite == null && quality != QualityType.Ordinary)
            {
                sprite = PlayItemSpriteLibrary.GetCardFrameBack(QualityType.Ordinary, layer);
            }

            if (sprite != null)
            {
                target.sprite = sprite;
            }
        }

        /// <summary>
        /// 怪物 enemycard 卡面底图用 BaseMap；enemycardattack / enemycardheart 底图按
        /// MonsterType 取 HealthBarBigBaseFrameKhaki（普通）/ Purple（精英）/ Red（Boss）。
        /// 贴图在 Altas/playitem。缺配置或缺图保留当前 sprite。
        /// </summary>
        private void ApplyMonsterFrames(int monsterId)
        {
            var row = FindMonster(monsterId);
            if (row == null)
            {
                return;
            }

            var baseMap = PlayItemSpriteLibrary.Get(row.BaseMap);
            if (baseMap != null && enemyCardBg != null)
            {
                enemyCardBg.sprite = baseMap;
            }

            var statFrame = PlayItemSpriteLibrary.Get(EnemyStatFrameSpriteName(row.Type));
            if (statFrame == null)
            {
                return;
            }

            if (enemyAttackBg != null)
            {
                enemyAttackBg.sprite = statFrame;
            }

            if (enemyHeartBg != null)
            {
                enemyHeartBg.sprite = statFrame;
            }
        }

        /// <summary>
        /// 敌人攻/血底图：普通 Khaki、精英 Purple、Boss Red（与 BaseMap 色系一致）。
        /// </summary>
        private static string EnemyStatFrameSpriteName(MonsterType type)
        {
            switch (type)
            {
                case MonsterType.Elite:
                    return "HealthBarBigBaseFramePurple";
                case MonsterType.Boss:
                    return "HealthBarBigBaseFrameRed";
                default:
                    return "HealthBarBigBaseFrameKhaki";
            }
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

        public void SetName(string name)
        {
            EnsureRefs();
            var label = ActiveName;
            if (label != null)
            {
                var value = name ?? string.Empty;
                if (label.text != value)
                {
                    label.text = value;
                }
            }
        }

        public void SetAttack(int attack)
        {
            EnsureRefs();
            var value = Mathf.Max(0, attack);
            SetAttackDisplay(value.ToString(), visible: value > 0);
        }

        /// <summary>只改攻击力文案，不动 attack / enemycardattack 显隐（局内默认隐藏，结算再弹出）。</summary>
        public void SetAttackValue(int attack)
        {
            EnsureRefs();
            var label = ActiveAttackValue;
            if (label == null)
            {
                return;
            }

            var value = Mathf.Max(0, attack).ToString();
            if (label.text != value)
            {
                label.text = value;
            }
        }

        /// <summary>隐藏人物 <c>attack</c> 与敌人 <c>enemycardattack</c>，并复位到布局缩放。</summary>
        public void HideAttackStat()
        {
            EnsureRefs();
            KillAttackPopTween();
            HideAttackRoot(attackRoot, _attackRestScale);
            HideAttackRoot(enemyAttackRoot, _enemyAttackRestScale);
        }

        /// <summary>
        /// 伤害结算：当前形态的攻击整块从 0 果冻弹到布局缩放（预制体 0.55）。
        /// 调用前用 <see cref="SetAttackValue"/> 写好数字。
        /// </summary>
        public Tween PlayAttackStatPop()
        {
            EnsureRefs();
            KillAttackPopTween();
            var root = ActiveAttackRoot;
            if (root == null)
            {
                var label = ActiveAttackValue;
                if (label != null && !label.gameObject.activeSelf)
                {
                    label.gameObject.SetActive(true);
                }

                return null;
            }

            var rest = _enemyVisual ? _enemyAttackRestScale : _attackRestScale;
            var t = root.transform;
            t.DOKill();
            root.SetActive(true);
            t.localScale = Vector3.zero;
            _attackPopTween = t.DOScale(rest, AttackPopDuration)
                .SetEase(Ease.OutElastic)
                .SetLink(root, LinkBehaviour.KillOnDestroy);
            return _attackPopTween;
        }

        public void PlayAttackNumberShake(bool low, bool high)
        {
            EnsureRefs();
            var animator = ActiveAttackAnimator;
            if (animator == null)
            {
                return;
            }

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.SetBool("low", false);
            animator.SetBool("high", false);
            animator.SetBool("normal", false);

            var state = low ? "NumberShackLow" : high ? "NumberShackHigh" : "NumberNormal";
            animator.SetBool("low", low);
            animator.SetBool("high", high);
            animator.SetBool("normal", !low && !high);
            animator.Play(state, 0, 0f);
            animator.Update(0f);
            if (low)
            {
                animator.SetBool("low", false);
            }

            if (high)
            {
                animator.SetBool("high", false);
            }
        }

        /// <summary>
        /// 血量文案为 <c>当前/上限</c>，healthbar（Sliced）按百分比改 width。
        /// <paramref name="maxHp"/> &lt; 0 时按满血处理（局外面板只有上限时传当前值即可）。
        /// </summary>
        public void SetHp(int hp, int maxHp = -1, bool hideWhenZero = true)
        {
            EnsureRefs();
            var value = Mathf.Max(0, hp);
            var max = maxHp < 0 ? value : Mathf.Max(0, maxHp);
            if (max < value)
            {
                max = value;
            }

            var visible = value > 0 || max > 0 || !hideWhenZero;
            if (hideWhenZero && value <= 0 && max <= 0)
            {
                visible = false;
            }

            var ratio = max > 0 ? Mathf.Clamp01((float)value / max) : 0f;
            SetHpDisplay($"{value}/{max}", visible, ratio);
        }

        /// <summary>
        /// 解锁态：隐藏 <c>card/cardMask</c>；未解锁：打开遮罩，attack / heart 显示 ??? 且保持可见。
        /// 已解锁的真实数值仍由 <see cref="SetAttack"/> / <see cref="SetHp"/> 写入。
        /// </summary>
        public void SetUnlocked(bool unlocked)
        {
            EnsureRefs();
            SetCardMaskVisible(!unlocked);
            if (unlocked)
            {
                return;
            }

            SetAttackDisplay(LockedStatText, visible: true);
            SetHpDisplay(LockedStatText, visible: true, fillRatio: 1f);
        }

        public void SetPortrait(Sprite portrait, bool locked = false)
        {
            EnsureRefs();
            var icon = ActiveIcon;
            if (icon == null)
            {
                return;
            }

            if (portrait == null)
            {
                icon.enabled = false;
                icon.color = UnlockedPortraitColor;
                return;
            }

            icon.sprite = portrait;
            icon.color = locked ? LockedPortraitColor : UnlockedPortraitColor;
            icon.enabled = true;
        }

        private void SetAttackDisplay(string text, bool visible)
        {
            var label = ActiveAttackValue;
            if (label != null)
            {
                var value = text ?? string.Empty;
                if (label.text != value)
                {
                    label.text = value;
                }
            }

            var root = ActiveAttackRoot;
            if (root != null)
            {
                // 显示时一律拉回布局缩放（预制体 0.55）。Hide/弹出曾误写成 1 时，
                // 只 SetActive 会把 LevelUI/主页攻块留在错误大小。
                if (visible)
                {
                    var rest = _enemyVisual ? _enemyAttackRestScale : _attackRestScale;
                    if (root.transform.localScale != rest)
                    {
                        root.transform.localScale = rest;
                    }
                }

                if (root.activeSelf != visible)
                {
                    root.SetActive(visible);
                }
            }
            else if (label != null && label.gameObject.activeSelf != visible)
            {
                label.gameObject.SetActive(visible);
            }
        }

        private void SetHpDisplay(string text, bool visible, float fillRatio = 1f)
        {
            var label = ActiveHeart;
            if (label != null)
            {
                var value = text ?? string.Empty;
                if (label.text != value)
                {
                    label.text = value;
                }
            }

            var bar = ActiveHealthBar;
            if (bar != null)
            {
                // 满宽记在字段里；若曾被收成 0，禁止再用当前 sizeDelta（已是空条）当满宽。
                if (_enemyVisual)
                {
                    EnsureHealthBarSliced(bar, ref _enemyHealthBarFullWidth);
                }
                else
                {
                    EnsureHealthBarSliced(bar, ref _healthBarFullWidth);
                }

                var full = ActiveHealthBarFullWidth;
                var width = full * Mathf.Clamp01(fillRatio);
                var size = bar.rectTransform.sizeDelta;
                if (!Mathf.Approximately(size.x, width))
                {
                    size.x = width;
                    bar.rectTransform.sizeDelta = size;
                }
            }

            var bg = ActiveHeartBg;
            if (bg != null)
            {
                if (bg.gameObject.activeSelf != visible)
                {
                    bg.gameObject.SetActive(visible);
                }
            }
            else if (label != null && label.gameObject.activeSelf != visible)
            {
                label.gameObject.SetActive(visible);
            }
        }

        private void SetCardMaskVisible(bool visible)
        {
            EnsureRefs();
            if (cardMask != null && cardMask.activeSelf != visible)
            {
                cardMask.SetActive(visible);
            }
        }

        private void Awake()
        {
            EnsureRefs();
        }

        private void OnDestroy()
        {
            KillAttackPopTween();
            HideDialogImmediate();
        }

        private void EnsureRefs()
        {
            // 一次性解析：树结构固定（池化复用不改层级），找不到的引用也只找这一次。
            // 否则行复用重绑时对缺失引用每次都 FindDeep 全树递归，是重绑 GC 小分配的大头
            if (_refsResolved)
            {
                return;
            }

            _refsResolved = true;
            if (cardName == null)
            {
                cardName = FindText("card_Name");
            }

            if (cardIcon == null)
            {
                cardIcon = FindImage("card_icon");
            }

            if (cardBg == null)
            {
                cardBg = FindImage("card");
            }

            if (_cardRoot == null)
            {
                var node = FindDeep(transform, "card");
                if (node != null)
                {
                    _cardRoot = node.gameObject;
                }
            }

            if (_enemyCardRoot == null)
            {
                var node = FindDeep(transform, "enemycard");
                if (node != null)
                {
                    _enemyCardRoot = node.gameObject;
                }
            }

            if (cardQualityBg == null)
            {
                cardQualityBg = FindImage("bg");
            }

            if (cardQualityDirect == null)
            {
                cardQualityDirect = FindImage("direct");
            }

            if (cardQualityDi == null)
            {
                cardQualityDi = FindImage("di");
            }

            if (cardMask == null && _cardRoot != null)
            {
                var node = FindDeep(_cardRoot.transform, "cardMask");
                if (node != null)
                {
                    cardMask = node.gameObject;
                }
            }

            if (cardAttackValue == null)
            {
                cardAttackValue = FindText("card_attackValue");
            }

            if (attackValueAnimator == null && cardAttackValue != null)
            {
                attackValueAnimator = cardAttackValue.GetComponent<Animator>();
            }

            if (attackRoot == null || attackBg == null)
            {
                var node = FindDeep(transform, "attack");
                if (node != null)
                {
                    if (attackRoot == null)
                    {
                        attackRoot = node.gameObject;
                    }

                    if (attackBg == null)
                    {
                        attackBg = node.GetComponent<Image>();
                    }
                }
            }

            // 预制体常已序列化 attackRoot+attackBg，不能只在 Find 分支里记缩放，否则会漏抓 0.55。
            if (attackRoot != null)
            {
                CaptureAttackRestScale(attackRoot.transform, ref _attackRestScale);
            }

            if (heartBg == null)
            {
                heartBg = FindImage("heart");
            }

            if (healthBar == null)
            {
                healthBar = FindImageUnder("heart", "healthbar");
            }

            EnsureHealthBarSliced(healthBar, ref _healthBarFullWidth);

            if (cardAttackHeart == null)
            {
                cardAttackHeart = FindText("card_attackHeart");
            }

            if (enemyCardName == null)
            {
                enemyCardName = FindText("enemycard_Name");
            }

            if (enemyCardIcon == null)
            {
                enemyCardIcon = FindImage("enemycard_icon");
            }

            if (enemyCardBg == null)
            {
                enemyCardBg = FindImage("enemycard");
            }

            if (enemyCardAttackValue == null)
            {
                enemyCardAttackValue = FindText("enemycard_attackValue");
            }

            if (enemyAttackValueAnimator == null && enemyCardAttackValue != null)
            {
                enemyAttackValueAnimator = enemyCardAttackValue.GetComponent<Animator>();
            }

            if (enemyAttackRoot == null || enemyAttackBg == null)
            {
                var node = FindDeep(transform, "enemycardattack");
                if (node != null)
                {
                    if (enemyAttackRoot == null)
                    {
                        enemyAttackRoot = node.gameObject;
                    }

                    if (enemyAttackBg == null)
                    {
                        enemyAttackBg = node.GetComponent<Image>();
                    }
                }
            }

            if (enemyAttackRoot != null)
            {
                CaptureAttackRestScale(enemyAttackRoot.transform, ref _enemyAttackRestScale);
            }

            if (enemyHeartBg == null)
            {
                enemyHeartBg = FindImage("enemycardheart");
            }

            if (enemyHealthBar == null)
            {
                enemyHealthBar = FindImageUnder("enemycardheart", "healthbar");
            }

            // 旧预制体曾把敌人填充条误命名为 Image (1)，挂在 enemycardheart/frame 下。
            if (enemyHealthBar == null)
            {
                var enemyHeart = FindDeep(transform, "enemycardheart");
                var enemyFrame = enemyHeart != null ? FindDeep(enemyHeart, "frame") : null;
                if (enemyFrame != null)
                {
                    for (var i = 0; i < enemyFrame.childCount; i++)
                    {
                        var child = enemyFrame.GetChild(i);
                        var image = child != null ? child.GetComponent<Image>() : null;
                        if (image != null)
                        {
                            enemyHealthBar = image;
                            break;
                        }
                    }
                }
            }

            EnsureHealthBarSliced(enemyHealthBar, ref _enemyHealthBarFullWidth);

            if (enemyCardAttackHeart == null)
            {
                enemyCardAttackHeart = FindText("enemycard_attackHeart");
            }

            HideStateNode();
            // 局内默认隐藏攻；主页/选角等随后 SetAttack(>0) 会再打开。
            HideAttackRoot(attackRoot, _attackRestScale);
            HideAttackRoot(enemyAttackRoot, _enemyAttackRestScale);
            EnsureDialogRefs();

            if (playerRoot == null)
            {
                var node = FindDeep(transform, "PlayerRoot");
                if (node != null)
                {
                    playerRoot = node as RectTransform ?? node.GetComponent<RectTransform>();
                    playerAnimator = node.GetComponent<Animator>();
                }
            }

            if (playerAnimator == null && playerRoot != null)
            {
                playerAnimator = playerRoot.GetComponent<Animator>();
            }

            if (!_visualReady)
            {
                SetEnemyVisual(false);
            }
        }

        private void EnsureDialogRefs()
        {
            if (_dialogRefsReady)
            {
                return;
            }

            _dialogRefsReady = true;
            if (topDialog == null)
            {
                var node = FindDeep(transform, "TopDialog");
                if (node != null)
                {
                    topDialog = node.gameObject;
                }
            }

            if (topDialogText == null && topDialog != null)
            {
                topDialogText = FindDialogText(topDialog.transform);
            }

            if (bottomDialog == null)
            {
                var node = FindDeep(transform, "BottomDialog");
                if (node != null)
                {
                    bottomDialog = node.gameObject;
                }
            }

            if (bottomDialogText == null && bottomDialog != null)
            {
                bottomDialogText = FindDialogText(bottomDialog.transform);
            }

            HideDialogRoots();
        }

        private GameObject ResolveDialog(bool top)
        {
            return top ? topDialog : bottomDialog;
        }

        private TMP_Text ResolveDialogText(bool top)
        {
            return top ? topDialogText : bottomDialogText;
        }

        private void HideDialogRoots()
        {
            ResetDialogText(topDialogText);
            ResetDialogText(bottomDialogText);
            if (topDialog != null)
            {
                topDialog.SetActive(false);
            }

            if (bottomDialog != null)
            {
                bottomDialog.SetActive(false);
            }

            _activeDialog = null;
        }

        private static void FreezeDialogLayout(RectTransform rt)
        {
            if (rt == null)
            {
                return;
            }

            var fitter = rt.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.enabled = true;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            if (fitter != null)
            {
                fitter.enabled = false;
            }
        }

        private static void ResetDialogText(TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            label.maxVisibleCharacters = int.MaxValue;
        }

        private void KillDialogTween()
        {
            if (_dialogTween != null && _dialogTween.IsActive())
            {
                _dialogTween.Kill();
            }

            _dialogTween = null;
        }

        private void KillAttackPopTween()
        {
            if (_attackPopTween != null && _attackPopTween.IsActive())
            {
                _attackPopTween.Kill();
            }

            _attackPopTween = null;
        }

        private static void CaptureAttackRestScale(Transform node, ref Vector3 restScale)
        {
            if (node == null)
            {
                return;
            }

            var scale = node.localScale;
            // 预制体布局是 0.55；若曾被误写成 1，不要把 1 当成 rest，保留默认 AttackStatLayoutScale。
            if (scale.x > 0.01f && !Mathf.Approximately(scale.x, 1f))
            {
                restScale = scale;
            }
        }

        private static void HideAttackRoot(GameObject root, Vector3 restScale)
        {
            if (root == null)
            {
                return;
            }

            root.transform.localScale = restScale;
            if (root.activeSelf)
            {
                root.SetActive(false);
            }
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
        }

        private static TMP_Text FindDialogText(Transform dialog)
        {
            var node = FindDeep(dialog, "DialogText");
            return node != null ? node.GetComponent<TMP_Text>() : null;
        }

        private Image FindImage(string nodeName)
        {
            var node = FindDeep(transform, nodeName);
            return node != null ? node.GetComponent<Image>() : null;
        }

        private Image FindImageUnder(string parentName, string nodeName)
        {
            var parent = FindDeep(transform, parentName);
            if (parent == null)
            {
                return null;
            }

            var node = FindDeep(parent, nodeName);
            return node != null ? node.GetComponent<Image>() : null;
        }

        private static void EnsureHealthBarSliced(Image bar, ref float fullWidth)
        {
            if (bar == null)
            {
                return;
            }

            if (bar.type != Image.Type.Sliced)
            {
                bar.type = Image.Type.Sliced;
            }

            var current = bar.rectTransform.sizeDelta.x;
            if (current > fullWidth)
            {
                fullWidth = current;
            }

            if (fullWidth > 0f)
            {
                return;
            }

            // 条已经是空宽时，用父级 frame 宽度作满宽，避免之后永远画成 0。
            if (bar.rectTransform.parent is RectTransform parent)
            {
                fullWidth = Mathf.Max(0f, parent.sizeDelta.x);
            }
        }

        private void HideStateNode()
        {
            if (_stateHidden)
            {
                return;
            }

            _stateHidden = true;
            var node = FindDeep(transform, "state");
            if (node != null)
            {
                node.gameObject.SetActive(false);
            }
        }

        private TMP_Text FindText(string nodeName)
        {
            var node = FindDeep(transform, nodeName);
            return node != null ? node.GetComponent<TMP_Text>() : null;
        }

        private static Transform FindDeep(Transform root, string nodeName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == nodeName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), nodeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
