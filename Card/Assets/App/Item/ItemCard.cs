using System;
using App.Config;
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

        public const string SelectAnim = "ani_shop_onchoose";
        public const string DefaultAnim = "ani_shop_default";

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
        private bool _hasSelectAnim;
        private bool _selectAnimOn;

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
        /// 按遗物品质给 IconBG / card_Circle 上色（无 IconTitleBG）。
        /// 同时刷新选中态回退色，避免之后 SetSelected(false) 打回预制体原色。
        /// </summary>
        public void ApplyQuality(QualityType type)
        {
            EnsureRefs();
            ThemeColors.ApplyCard(type, iconBg, null, cardCircle);
            if (iconBg != null)
            {
                _defaultBg = iconBg.color;
            }

            if (cardCircle != null)
            {
                _defaultCircle = cardCircle.color;
            }

            _colorsCached = true;
        }

        /// <summary>
        /// 选中抬卡。取消选中关掉 Animator 并把 card Y 打回 0（默认 clip 不写该曲线）。
        /// 与 <see cref="SetSelected"/> 的换色/缩放分开，图鉴列表不受影响。
        /// </summary>
        public void PlaySelected(bool selected, bool force = false)
        {
            if (!force && _hasSelectAnim && _selectAnimOn == selected)
            {
                return;
            }

            _hasSelectAnim = true;
            _selectAnimOn = selected;
            SetSelectLift(selected);
        }

        /// <summary>
        /// 播放 ItemRoot 上 Animator 的指定状态。预制体挂的是商店共用的
        /// ShopRoot.controller（无参数，靠状态名播放）。
        /// </summary>
        public void PlayAnimation(string stateName, int layer = 0, float normalizedTime = 0f)
        {
            EnsureRefs();
            if (itemAnimator == null || string.IsNullOrEmpty(stateName))
            {
                return;
            }

            itemAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            itemAnimator.enabled = true;
            itemAnimator.speed = 1f;
            itemAnimator.Play(stateName, layer, normalizedTime);
            itemAnimator.Update(0f);
        }

        private void SetSelectLift(bool selected)
        {
            EnsureRefs();
            var card = ResolveAnimatedCard();
            if (itemAnimator == null)
            {
                SetAnchoredY(card, 0f);
                return;
            }

            itemAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            itemAnimator.speed = 1f;
            if (!selected)
            {
                itemAnimator.enabled = false;
                SetAnchoredY(card, 0f);
                return;
            }

            itemAnimator.enabled = true;
            itemAnimator.Play(DefaultAnim, 0, 0f);
            itemAnimator.Update(0f);
            SetAnchoredY(card, 0f);
            itemAnimator.Play(SelectAnim, 0, 0f);
            itemAnimator.Update(0f);
        }

        private RectTransform ResolveAnimatedCard()
        {
            EnsureRefs();
            if (cardIcon != null)
            {
                return cardIcon.rectTransform.parent as RectTransform;
            }

            if (itemRoot == null)
            {
                return null;
            }

            var frame = itemRoot.Find("cardFrame");
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
