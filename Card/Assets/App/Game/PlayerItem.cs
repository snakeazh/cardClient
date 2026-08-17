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

        [SerializeField] private Image iconBg;
        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardCircle;
        [SerializeField] private Image cardIcon;
        [SerializeField] private TMP_Text cardAttackValue;
        [SerializeField] private TMP_Text cardAttackHeart;

        public RectTransform CardIconRect
        {
            get
            {
                EnsureRefs();
                return cardIcon != null ? cardIcon.rectTransform : null;
            }
        }

        public void Bind(SeatState seat, Sprite portrait, int attack = 0)
        {
            EnsureRefs();
            var enemy = seat != null && !seat.IsPlayer;
            ApplyTheme(enemy);
            SetName(seat != null ? seat.Name : string.Empty);
            SetAttack(attack);
            SetHp(seat != null ? seat.Hp : 0);
            SetPortrait(portrait);
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

        public void SetPortrait(Sprite portrait)
        {
            EnsureRefs();
            if (cardIcon == null || portrait == null)
            {
                return;
            }

            cardIcon.sprite = portrait;
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
