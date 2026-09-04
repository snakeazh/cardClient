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
    /// 局内角色/敌人信息卡。人物与敌人共用同一套节点，头像与数值在 <see cref="Bind"/> 时赋值。
    /// 品质完全由图集图表达：attack/heart 底图取 Altas/ItemBg 的 {品质}RectangleFrame，
    /// 卡面/标题底等其余节点颜色以预制体为准，代码不染色。
    /// </summary>
    public sealed class PlayerItem : MonoBehaviour
    {
        private static readonly Color UnlockedPortraitColor = Color.white;
        private static readonly Color LockedPortraitColor = new Color(0f, 0f, 0f, 1f);

        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardIcon;
        [SerializeField] private TMP_Text cardAttackValue;
        [SerializeField] private Animator attackValueAnimator;
        [SerializeField] private TMP_Text cardAttackHeart;
        [SerializeField] private GameObject attackRoot;
        [SerializeField] private Image attackBg;
        [SerializeField] private Image heartBg;
        [SerializeField] private TMP_Text cardState;
        [SerializeField] private RectTransform playerRoot;
        [SerializeField] private Animator playerAnimator;

        public RectTransform CardIconRect
        {
            get
            {
                EnsureRefs();
                return cardIcon != null ? cardIcon.rectTransform : null;
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
                return cardAttackValue != null ? cardAttackValue.rectTransform : null;
            }
        }

        public Animator AttackValueAnimator
        {
            get
            {
                EnsureRefs();
                return attackValueAnimator;
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

        public void Bind(SeatState seat, Sprite portrait, int attack = 0, int actingAiId = -1)
        {
            EnsureRefs();
            var enemy = seat != null && !seat.IsPlayer;
            ApplyTheme();
            SetName(seat != null ? seat.Name : string.Empty);
            SetAttack(attack);
            SetHp(seat != null ? seat.Hp : 0);
            SetPortrait(portrait);
            if (enemy)
            {
                SetState(FormatAiState(seat, actingAiId));
            }
            else if (seat != null && !string.IsNullOrEmpty(seat.PeekedType))
            {
                SetState($"透视 {seat.PeekedType}");
            }
            else
            {
                SetState(string.Empty);
            }
        }

        public static string FormatAiState(SeatState seat, int actingAiId)
        {
            if (seat == null || seat.IsPlayer)
            {
                return string.Empty;
            }

            if (seat.Folded)
            {
                return "弃牌";
            }

            if (seat.Id == actingAiId)
            {
                return "操作中";
            }

            if (!string.IsNullOrEmpty(seat.PeekedType))
            {
                return $"透视 {seat.PeekedType}";
            }

            if (!string.IsNullOrEmpty(seat.Status))
            {
                return seat.Status;
            }

            return seat.StreetPaid > 0 ? "已下注" : string.Empty;
        }

        public void SetState(string text)
        {
            EnsureRefs();
            if (cardState != null)
            {
                cardState.text = text ?? string.Empty;
            }
        }

        public void ApplyTheme(QualityType quality = QualityType.Ordinary)
        {
            EnsureRefs();
            ApplyStatFrame(quality);
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

        public void SetName(string name)
        {
            EnsureRefs();
            if (cardName != null)
            {
                cardName.text = name ?? string.Empty;
            }
        }

        public void SetAttack(int attack)
        {
            EnsureRefs();
            var value = Mathf.Max(0, attack);
            if (cardAttackValue != null)
            {
                cardAttackValue.text = value.ToString();
            }

            if (attackRoot != null)
            {
                attackRoot.SetActive(value > 0);
            }
            else if (cardAttackValue != null)
            {
                cardAttackValue.gameObject.SetActive(value > 0);
            }
        }

        public void PlayAttackNumberShake(bool low, bool high)
        {
            EnsureRefs();
            if (attackValueAnimator == null)
            {
                return;
            }

            attackValueAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            attackValueAnimator.enabled = true;
            attackValueAnimator.SetBool("low", false);
            attackValueAnimator.SetBool("high", false);
            attackValueAnimator.SetBool("normal", false);

            var state = low ? "NumberShackLow" : high ? "NumberShackHigh" : "NumberNormal";
            attackValueAnimator.SetBool("low", low);
            attackValueAnimator.SetBool("high", high);
            attackValueAnimator.SetBool("normal", !low && !high);
            attackValueAnimator.Play(state, 0, 0f);
            attackValueAnimator.Update(0f);
            if (low)
            {
                attackValueAnimator.SetBool("low", false);
            }

            if (high)
            {
                attackValueAnimator.SetBool("high", false);
            }
        }

        public void SetHp(int hp)
        {
            EnsureRefs();
            var value = Mathf.Max(0, hp);
            if (cardAttackHeart != null)
            {
                cardAttackHeart.text = value.ToString();
            }

            if (heartBg != null)
            {
                heartBg.gameObject.SetActive(value > 0);
            }
            else if (cardAttackHeart != null)
            {
                cardAttackHeart.gameObject.SetActive(value > 0);
            }
        }

        public void SetPortrait(Sprite portrait, bool locked = false)
        {
            EnsureRefs();
            if (cardIcon == null)
            {
                return;
            }

            if (portrait == null)
            {
                cardIcon.enabled = false;
                cardIcon.color = UnlockedPortraitColor;
                return;
            }

            cardIcon.sprite = portrait;
            cardIcon.color = locked ? LockedPortraitColor : UnlockedPortraitColor;
            cardIcon.enabled = true;
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

            if (cardState == null)
            {
                cardState = FindText("state");
            }

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
        }

        private Image FindImage(string nodeName)
        {
            var node = FindDeep(transform, nodeName);
            return node != null ? node.GetComponent<Image>() : null;
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
