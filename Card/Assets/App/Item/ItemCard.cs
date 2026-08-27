using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Item
{
    /// <summary>
    /// 图鉴/收集类单卡显示控制，挂在预制体 <c>Res/UI/Icon/Item</c> 根节点上。
    /// 节点与 <see cref="PlayerItem"/> 一致走序列化字段优先、按名懒查找兜底，预制体无需手动拖引用。
    /// 界面结构：Item(Button) / ItemRoot(Animator) / IconShdow + cardFrame(IconBG + card(card_Name、card_Circle、card_icon))。
    /// </summary>
    public sealed class ItemCard : MonoBehaviour
    {
        private const string UnknownNameText = "？？？";

        private static readonly Color SelectedBg = ParseHex("F8AB67");
        private static readonly Color SelectedCircle = ParseHex("FFD98A");

        [SerializeField] private Image iconBg;
        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardCircle;
        [SerializeField] private Image cardIcon;
        [SerializeField] private Image iconShadow;
        [SerializeField] private Button button;
        [SerializeField] private RectTransform itemRoot;
        [SerializeField] private Animator itemAnimator;

        /// <summary>根节点 Button 点击转发；预制体 OnClick 列表为空，监听在这里挂。</summary>
        public event Action<ItemCard> Clicked;

        public bool Interactable
        {
            get => Button != null && Button.interactable;
            set
            {
                if (Button != null)
                {
                    Button.interactable = value;
                }
            }
        }

        /// <summary>图标 RectTransform，供外部飞入/攻击动画做挂点（同 <see cref="PlayerItem.CardIconRect"/>）。</summary>
        public RectTransform IconRect => cardIcon != null ? cardIcon.rectTransform : null;

        public Animator RootAnimator
        {
            get
            {
                EnsureRefs();
                return itemAnimator;
            }
        }

        private Button Button
        {
            get
            {
                EnsureRefs();
                return button;
            }
        }

        private Color _defaultBg;
        private Color _defaultCircle;
        private bool _colorsCached;
        private bool _selected;

        private void Awake()
        {
            EnsureRefs();
            CacheThemeColors();

            if (button != null)
            {
                button.onClick.AddListener(OnClicked);
            }
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(OnClicked);
            }
        }

        /// <summary>一次性写入显示内容；图标由调用方加载好后传入（同 ShopItem.Bind 模式）。</summary>
        public void Bind(string displayName, Sprite icon, bool unlocked)
        {
            SetName(displayName);
            SetIcon(icon);
            SetUnlocked(unlocked);
        }

        public void SetName(string text)
        {
            EnsureRefs();
            if (cardName != null)
            {
                cardName.text = string.IsNullOrEmpty(text) ? UnknownNameText : text;
            }
        }

        /// <summary>icon 为 null 时隐藏图标节点（背景框仍显示）。</summary>
        public void SetIcon(Sprite icon)
        {
            EnsureRefs();
            if (cardIcon == null)
            {
                return;
            }

            if (icon == null)
            {
                cardIcon.enabled = false;
                return;
            }

            cardIcon.sprite = icon;
            cardIcon.enabled = true;
        }

        /// <summary>
        /// 解锁态：card_icon 染回本色（白）；未解锁：染黑表示未收集剪影，名字占位为 ？？？。
        /// </summary>
        public void SetUnlocked(bool unlocked)
        {
            EnsureRefs();
            if (cardIcon != null)
            {
                cardIcon.color = unlocked ? Color.white : Color.black;
            }

            if (!unlocked)
            {
                SetName(null);
            }
        }

        /// <summary>选中态通过 IconBG/card_Circle 换色 + 轻微放大 ItemRoot 表达（原始色在 Awake 缓存）。</summary>
        public void SetSelected(bool selected)
        {
            EnsureRefs();
            _selected = selected;
            ApplyThemeColors();

            if (itemRoot != null)
            {
                var scale = selected ? 1.08f : 1f;
                itemRoot.localScale = new Vector3(scale, scale, 1f);
            }
        }

        public bool IsSelected => _selected;

        /// <summary>阴影节点为纯色半透明 Image，图鉴列表滚动条等场景可整体关掉。</summary>
        public void SetShadowVisible(bool visible)
        {
            EnsureRefs();
            if (iconShadow != null)
            {
                iconShadow.enabled = visible;
            }
        }

        /// <summary>
        /// 播放 ItemRoot 上 Animator 的指定状态。预制体挂的是商店共用的
        /// ShopRoot.controller（无参数，靠状态名播放），现有状态：ani_shop_default、ani_shop_choose_start。
        /// </summary>
        public void PlayAnimation(string stateName, int layer = 0, float normalizedTime = 0f)
        {
            EnsureRefs();
            if (itemAnimator != null && !string.IsNullOrEmpty(stateName))
            {
                itemAnimator.Play(stateName, layer, normalizedTime);
            }
        }

        public void SetAnimationEnabled(bool enabled)
        {
            EnsureRefs();
            if (itemAnimator != null)
            {
                itemAnimator.enabled = enabled;
            }
        }

        private void OnClicked()
        {
            Clicked?.Invoke(this);
        }

        private void ApplyThemeColors()
        {
            EnsureRefs();
            CacheThemeColors();
            if (iconBg != null)
            {
                iconBg.color = _selected ? SelectedBg : _defaultBg;
            }

            if (cardCircle != null)
            {
                cardCircle.color = _selected ? SelectedCircle : _defaultCircle;
            }
        }

        private void CacheThemeColors()
        {
            if (_colorsCached || iconBg == null || cardCircle == null)
            {
                return;
            }

            _defaultBg = iconBg.color;
            _defaultCircle = cardCircle.color;
            _colorsCached = true;
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

            if (iconShadow == null)
            {
                iconShadow = FindImage("IconShdow");
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (itemRoot == null)
            {
                var node = FindDeep(transform, "ItemRoot");
                if (node != null)
                {
                    itemRoot = node as RectTransform ?? node.GetComponent<RectTransform>();
                    itemAnimator = node.GetComponent<Animator>();
                }
            }

            if (itemAnimator == null && itemRoot != null)
            {
                itemAnimator = itemRoot.GetComponent<Animator>();
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
