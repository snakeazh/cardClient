using System;
using App.Atlas;
using App.Config;
using App.UI;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>
    /// 局内角色/敌人信息卡。人物用 card，敌人用 enemycard，头像与数值在 <see cref="Bind"/> 时赋值。
    /// 玩家 attack/heart 底图取 Altas/ItemBg 的 {品质}RectangleFrame；
    /// 怪物 enemycard 底图取 MonsterConfig.BaseMap，attack/heart 取 HealthBar。
    /// 卡面/标题底等其余节点颜色以预制体为准，代码不染色。
    /// </summary>
    public sealed class PlayerItem : MonoBehaviour
    {
        private static readonly Color UnlockedPortraitColor = Color.white;
        private static readonly Color LockedPortraitColor = new Color(0f, 0f, 0f, 1f);

        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardIcon;
        [SerializeField] private Image cardBg;
        [SerializeField] private TMP_Text cardAttackValue;
        [SerializeField] private Animator attackValueAnimator;
        [SerializeField] private TMP_Text cardAttackHeart;
        [SerializeField] private GameObject attackRoot;
        [SerializeField] private Image attackBg;
        [SerializeField] private Image heartBg;
        [SerializeField] private TMP_Text enemyCardName;
        [SerializeField] private Image enemyCardIcon;
        [SerializeField] private Image enemyCardBg;
        [SerializeField] private TMP_Text enemyCardAttackValue;
        [SerializeField] private Animator enemyAttackValueAnimator;
        [SerializeField] private TMP_Text enemyCardAttackHeart;
        [SerializeField] private GameObject enemyAttackRoot;
        [SerializeField] private Image enemyAttackBg;
        [SerializeField] private Image enemyHeartBg;
        [SerializeField] private RectTransform playerRoot;
        [SerializeField] private Animator playerAnimator;

        private bool _stateHidden;
        private bool _enemyVisual;
        private bool _visualReady;

        private TMP_Text ActiveName => _enemyVisual && enemyCardName != null ? enemyCardName : cardName;
        private Image ActiveIcon => _enemyVisual && enemyCardIcon != null ? enemyCardIcon : cardIcon;
        private TMP_Text ActiveAttackValue =>
            _enemyVisual && enemyCardAttackValue != null ? enemyCardAttackValue : cardAttackValue;
        private Animator ActiveAttackAnimator =>
            _enemyVisual && enemyAttackValueAnimator != null ? enemyAttackValueAnimator : attackValueAnimator;
        private GameObject ActiveAttackRoot =>
            _enemyVisual && enemyAttackRoot != null ? enemyAttackRoot : attackRoot;
        private Image ActiveHeartBg => _enemyVisual && enemyHeartBg != null ? enemyHeartBg : heartBg;
        private TMP_Text ActiveHeart =>
            _enemyVisual && enemyCardAttackHeart != null ? enemyCardAttackHeart : cardAttackHeart;

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
        public void SetSelectLift(bool selected, string selectState, string idleState)
        {
            EnsureRefs();
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
            var enemy = seat != null && !seat.IsPlayer;
            SetEnemyVisual(enemy);
            if (enemy)
            {
                ApplyMonsterFrames(seat.MonsterId);
            }
            else
            {
                ApplyTheme();
            }

            SetName(seat != null ? seat.Name : string.Empty);
            SetAttack(attack);
            SetHp(seat != null ? seat.Hp : 0, hideWhenZero: false);
            SetPortrait(portrait);
        }

        public void ApplyTheme(QualityType quality = QualityType.Ordinary)
        {
            EnsureRefs();
            SetEnemyVisual(false);
            ApplyStatFrame(quality);
        }

        /// <summary>敌人形态：启用 enemycard 并按 MonsterId 取 BaseMap/HealthBar 底图。图鉴等非对局场景使用。</summary>
        public void ApplyEnemyTheme(int monsterId)
        {
            EnsureRefs();
            SetEnemyVisual(true);
            ApplyMonsterFrames(monsterId);
        }

        private void SetEnemyVisual(bool enemy)
        {
            if (enemy && enemyCardBg == null)
            {
                enemy = false;
            }

            _enemyVisual = enemy;
            _visualReady = true;
            if (cardBg != null)
            {
                cardBg.gameObject.SetActive(!enemy);
            }

            if (enemyCardBg != null)
            {
                enemyCardBg.gameObject.SetActive(enemy);
            }
        }

        /// <summary>
        /// attack/heart 数值底图按品质取 Altas/ItemBg 的 {品质}RectangleFrame（预制体默认即
        /// OrdinaryRectangleFrame 的直引，运行时统一以图集 sprite 为准）。目标品质缺图时回退
        /// 普通品质，图集整体不可用时保留当前图。
        /// </summary>
        private void ApplyStatFrame(QualityType quality)
        {
            var frame = ItemBgSpriteLibrary.GetRectangleFrame(quality);
            if (frame == null && quality != QualityType.Ordinary)
            {
                frame = ItemBgSpriteLibrary.GetRectangleFrame(QualityType.Ordinary);
            }

            if (frame == null)
            {
                return;
            }

            if (attackBg != null)
            {
                attackBg.sprite = frame;
            }

            if (heartBg != null)
            {
                heartBg.sprite = frame;
            }
        }

        /// <summary>
        /// 怪物 enemycard 底图用 BaseMap，enemycardattack/heart 用 HealthBar。缺配置或缺图保留当前 sprite。
        /// </summary>
        private void ApplyMonsterFrames(int monsterId)
        {
            var row = FindMonster(monsterId);
            if (row == null)
            {
                return;
            }

            var baseMap = ItemBgSpriteLibrary.Get(row.BaseMap);
            if (baseMap != null && enemyCardBg != null)
            {
                enemyCardBg.sprite = baseMap;
            }

            var healthBar = ItemBgSpriteLibrary.Get(row.HealthBar);
            if (healthBar == null)
            {
                return;
            }

            if (enemyAttackBg != null)
            {
                enemyAttackBg.sprite = healthBar;
            }

            if (enemyHeartBg != null)
            {
                enemyHeartBg.sprite = healthBar;
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
                label.text = name ?? string.Empty;
            }
        }

        public void SetAttack(int attack)
        {
            EnsureRefs();
            var value = Mathf.Max(0, attack);
            var label = ActiveAttackValue;
            if (label != null)
            {
                label.text = value.ToString();
            }

            var root = ActiveAttackRoot;
            if (root != null)
            {
                root.SetActive(value > 0);
            }
            else if (label != null)
            {
                label.gameObject.SetActive(value > 0);
            }
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

        public void SetHp(int hp, bool hideWhenZero = true)
        {
            EnsureRefs();
            var value = Mathf.Max(0, hp);
            var label = ActiveHeart;
            if (label != null)
            {
                label.text = value.ToString();
            }

            var visible = value > 0 || !hideWhenZero;
            var bg = ActiveHeartBg;
            if (bg != null)
            {
                bg.gameObject.SetActive(visible);
            }
            else if (label != null)
            {
                label.gameObject.SetActive(visible);
            }
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

        private void Awake()
        {
            EnsureRefs();
        }

        private void EnsureRefs()
        {
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

            if (heartBg == null)
            {
                heartBg = FindImage("heart");
            }

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

            if (enemyHeartBg == null)
            {
                enemyHeartBg = FindImage("enemycardheart");
            }

            if (enemyCardAttackHeart == null)
            {
                enemyCardAttackHeart = FindText("enemycard_attackHeart");
            }

            HideStateNode();

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

        private Image FindImage(string nodeName)
        {
            var node = FindDeep(transform, nodeName);
            return node != null ? node.GetComponent<Image>() : null;
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
