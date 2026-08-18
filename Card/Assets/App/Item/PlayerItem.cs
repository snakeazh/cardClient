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
        private static readonly Color PlayerIconBg = ParseHex("EB9852");
        private static readonly Color PlayerCircle = ParseHex("F8AB67");
        private static readonly Color EnemyIconBg = ParseHex("F6393C");
        private static readonly Color EnemyCircle = ParseHex("B20003");

        private static readonly Color UnlockedPortraitColor = Color.white;
        private static readonly Color LockedPortraitColor = new Color(0f, 0f, 0f, 1f);

        [SerializeField] private Image iconBg;
        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardCircle;
        [SerializeField] private Image cardIcon;
        [SerializeField] private TMP_Text cardAttackValue;
        [SerializeField] private TMP_Text cardAttackHeart;
        [SerializeField] private TMP_Text cardState;

        public RectTransform CardIconRect
        {
            get
            {
                EnsureRefs();
                return cardIcon != null ? cardIcon.rectTransform : null;
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
                iconBg.color = enemy ? EnemyIconBg : PlayerIconBg;
            }

            if (cardCircle != null)
            {
                cardCircle.color = enemy ? EnemyCircle : PlayerCircle;
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
            if (cardAttackValue != null)
            {
                cardAttackValue.text = Mathf.Max(0, attack).ToString();
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

            if (cardAttackHeart == null)
            {
                cardAttackHeart = FindText("card_attackHeart");
            }

            if (cardState == null)
            {
                cardState = FindText("state");
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

        private static Color ParseHex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            color.a = 1f;
            return color;
        }
    }
}
