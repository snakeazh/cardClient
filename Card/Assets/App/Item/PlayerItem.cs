using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>
    /// 局内角色/敌人信息卡。人物与敌人共用同一套节点，颜色与头像在 <see cref="Bind"/> 时赋值。
    /// </summary>
    public sealed class PlayerItem : MonoBehaviour
    {
        private static readonly Color UnlockedPortraitColor = Color.white;
        private static readonly Color LockedPortraitColor = new Color(0f, 0f, 0f, 1f);

        [SerializeField] private Image iconBg;
        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardCircle;
        [SerializeField] private Image cardIcon;
        [SerializeField] private TMP_Text cardAttackValue;
        [SerializeField] private TMP_Text cardAttackHeart;
        [SerializeField] private GameObject attackRoot;
        [SerializeField] private Image attackBg;
        [SerializeField] private Image heartBg;
        [SerializeField] private Sprite playerStatFrame;
        [SerializeField] private Sprite enemyStatFrame;
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

        public void Bind(SeatState seat, Sprite portrait, int attack = 0, int actingAiId = -1)
        {
            EnsureRefs();
            var enemy = seat != null && !seat.IsPlayer;
            ApplyTheme(enemy);
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

        public void ApplyTheme(bool enemy)
        {
            EnsureRefs();
            if (iconBg != null)
            {
                iconBg.color = enemy ? ThemeColors.Enemy : ThemeColors.Player;
            }

            if (cardCircle != null)
            {
                cardCircle.color = enemy ? ThemeColors.EnemyCircle : ThemeColors.PlayerCircle;
            }

            var frame = enemy ? enemyStatFrame : playerStatFrame;
            if (frame != null)
            {
                if (attackBg != null)
                {
                    attackBg.sprite = frame;
                }

                if (heartBg != null)
                {
                    heartBg.sprite = frame;
                }
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

        public void SetHp(int hp)
        {
            EnsureRefs();
            if (cardAttackHeart != null)
            {
                cardAttackHeart.text = Mathf.Max(0, hp).ToString();
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
            if (iconBg == null)
            {
                iconBg = FindImage("IconBG");
            }

            if (cardName == null)
            {
                cardName = FindText("card_Name");
            }

            if (cardCircle == null)
            {
                cardCircle = FindImage("card_Circle");
            }

            if (cardIcon == null)
            {
                cardIcon = FindImage("card_icon");
            }

            if (cardAttackValue == null)
            {
                cardAttackValue = FindText("card_attackValue");
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
