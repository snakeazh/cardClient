using System;
using App.Atlas;
using App.Config;
using App.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Item
{
    /// <summary>
    /// 图鉴/收集类单卡显示控制，挂在预制体 <c>Res/UI/Icon/Item</c> 根节点上。
    /// 节点与 <see cref="PlayerItem"/> 一致走序列化字段优先、按名懒查找兜底，预制体无需手动拖引用。
    /// 界面结构：Item(Button) / ItemRoot(Animator) / cardFrame(IconBG + Mask + card(card_Name、card_icon) + CardBG)。
    /// card 节点 Image 为品质卡面图、CardBG 为品质卡背背景，ApplyQuality 时按品质从 Altas/ItemBg 图集取图；
    /// Mask 为未解锁遮罩，SetUnlocked 控制。
    /// </summary>
    public sealed class ItemCard : MonoBehaviour
    {
        private const string UnknownNameText = "？？？";

        private static readonly Color SelectedBg = ParseHex("F8AB67");
        private static readonly Color SelectedCircle = ParseHex("FFD98A");

        [SerializeField] private Image iconBg;
        [SerializeField] private Image cardImage;
        [SerializeField] private Image cardBack;
        [SerializeField] private GameObject lockMask;
        [SerializeField] private TMP_Text cardName;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private Image cardCircle;
        [SerializeField] private Image cardIcon;
        [SerializeField] private Image iconShadow;
        [SerializeField] private Button button;
        [SerializeField] private RectTransform itemRoot;
        [SerializeField] private Animator itemAnimator;

        public const string SelectAnim = "ani_shop_onchoose";
        public const string DefaultAnim = "ani_shop_default";

        /// <summary>抽卡翻卡演出（Res/Animations/Chouka/ItemRoot.controller 的主动画，5 秒一次性）。</summary>
        public const string RewardRevealAnim = "ItemRoot";

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
        private bool _bgCached;
        private bool _circleCached;
        private bool _selected;
        private bool _hasSelectAnim;
        private bool _selectAnimOn;
        private QualityType _quality = QualityType.Ordinary;

        public QualityType Quality => _quality;

        private void Awake()
        {
            EnsureRefs();
            CacheThemeColors();
            EnsureDefaultFrame();
            // 等级角标仅天赋列表/详情会调 SetLevel 显示，其余共用 Item 预制体的界面（图鉴/商店等）默认隐藏，
            // 防止预制体默认文案（Lv.1）外泄
            SetLevel(null);

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

        /// <summary>
        /// 等级角标（cardFrame/Level 节点）：传 "Lv.2" 这类非空文本显示，null/空串隐藏。
        /// </summary>
        public void SetLevel(string text)
        {
            EnsureRefs();
            if (levelText == null)
            {
                return;
            }

            var visible = !string.IsNullOrEmpty(text);
            levelText.gameObject.SetActive(visible);
            if (visible)
            {
                levelText.text = text;
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

        /// <summary>染 card_icon 颜色；白 = 原色不染色，黑 = 剪影（天赋列表未解锁态用）。</summary>
        public void SetIconColor(Color color)
        {
            EnsureRefs();
            if (cardIcon != null)
            {
                cardIcon.color = color;
            }
        }

        /// <summary>
        /// 解锁态：隐藏 Mask 遮罩、图标原色；未解锁：激活 Mask 遮罩（不染黑卡面组件），名字占位为 ？？？。
        /// </summary>
        public void SetUnlocked(bool unlocked)
        {
            EnsureRefs();
            if (lockMask != null)
            {
                lockMask.SetActive(!unlocked);
                if (!unlocked)
                {
                    // 兜底：遮罩节点上的 Graphic 若被外部禁用（override/误操作），激活遮罩时强制启用，
                    // 否则节点激活了遮罩也不可见
                    var graphic = lockMask.GetComponent<Graphic>();
                    if (graphic != null && !graphic.enabled)
                    {
                        graphic.enabled = true;
                    }
                }
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
        /// 按品质切换 card 节点卡面图与 CardBG 卡背背景（Altas/ItemBg）。目标品质缺图时回退普通品质，
        /// 图集整体不可用时保留当前图（预制体默认即 Ordinary）。
        /// 品质完全由卡面图表达，不改任何节点颜色；选中态高亮仍走 SetSelected。
        /// </summary>
        public void ApplyQuality(QualityType type)
        {
            EnsureRefs();
            _quality = type;
            ApplyFrame(type);
        }

        /// <summary>品质边框与卡背图；目标品质缺图时回退普通品质，图集整体不可用时不动当前图。</summary>
        private void ApplyFrame(QualityType type)
        {
            ApplySprite(cardImage, ItemBgSpriteLibrary.GetCardFrame, type);
            ApplySprite(cardBack, ItemBgSpriteLibrary.GetCardFrameBack, type);
        }

        /// <summary>
        /// 防护：初始化时把 card 边框与 CardBG 卡背统一切到图集版普通品质图。源图已入 ItemBg 图集，
        /// prefab 对源图的直引在图集绑定完成前的窗口会渲染空白（丢背景），运行时以图集 sprite 为准。
        /// </summary>
        private void EnsureDefaultFrame()
        {
            ApplySprite(cardImage, ItemBgSpriteLibrary.GetCardFrame, QualityType.Ordinary);
            ApplySprite(cardBack, ItemBgSpriteLibrary.GetCardFrameBack, QualityType.Ordinary);
        }

        private static void ApplySprite(Image target, Func<QualityType, Sprite> resolve, QualityType type)
        {
            if (target == null)
            {
                return;
            }

            var sprite = resolve(type);
            if (sprite == null && type != QualityType.Ordinary)
            {
                sprite = resolve(QualityType.Ordinary);
            }

            if (sprite != null)
            {
                target.sprite = sprite;
            }
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

        /// <summary>
        /// 抽卡结果翻卡演出：播 ItemRoot 翻面动画（Res/Animations/Chouka），并按品质点亮
        /// ItemRoot 下内嵌的天赋特效实例（红=传说、紫=史诗、蓝=稀有；普通只播动画）。
        /// 嵌套特效默认隐藏、在 Default 层且粒子不吃 Canvas 层级——激活前整组换 UI 层并挂排序继承。
        /// </summary>
        public void PlayRewardReveal(QualityType quality)
        {
            EnsureRefs();
            PrepareRewardFx("ChoukaEffect01");
            var fxName = RewardFxName(quality);
            if (fxName != null)
            {
                PrepareRewardFx(fxName);
            }

            PlayAnimation(RewardRevealAnim);
        }

        private void PrepareRewardFx(string fxName)
        {
            var root = itemRoot != null ? itemRoot.transform : transform;
            var fx = root.Find(fxName);
            if (fx == null)
            {
                return;
            }

            fx.gameObject.SetActive(true);
            UiFx.ApplyUiLayer(fx.gameObject);
            if (fx.GetComponent<EffectSortingInherit>() == null)
            {
                var inherit = fx.gameObject.AddComponent<EffectSortingInherit>();
                inherit.OrderOffset = 10;
            }

            UiFx.RestartParticles(fx.gameObject);
        }

        private static string RewardFxName(QualityType quality)
        {
            switch (quality)
            {
                case QualityType.Legend:
                    return "TianfuRed01";
                case QualityType.Epic:
                    return "TianfuPurple01";
                case QualityType.Rare:
                    return "TianfuBlue01";
                default:
                    return null;
            }
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

        /// <summary>
        /// 各自独立缓存：card_Circle 节点美术侧可删（当前 prefab 已无），缺失不能连坐 IconBG 的回退色缓存，
        /// 否则 _defaultBg 保持 (0,0,0,0)，SetSelected(false) 会把 IconBG 打成全透明（透出黑 Mask，卡面发黑）。
        /// </summary>
        private void CacheThemeColors()
        {
            if (iconBg != null && !_bgCached)
            {
                _defaultBg = iconBg.color;
                _bgCached = true;
            }

            if (cardCircle != null && !_circleCached)
            {
                _defaultCircle = cardCircle.color;
                _circleCached = true;
            }
        }

        private void EnsureRefs()
        {
            if (iconBg == null)
            {
                iconBg = FindImage("IconBG");
            }

            if (cardImage == null)
            {
                cardImage = FindImage("card");
            }

            if (cardBack == null)
            {
                cardBack = FindImage("CardBG");
            }

            if (lockMask == null)
            {
                var maskNode = FindDeep(transform, "Mask");
                lockMask = maskNode != null ? maskNode.gameObject : null;
            }

            if (cardName == null)
            {
                cardName = FindText("card_Name");
            }

            if (levelText == null)
            {
                levelText = FindText("Level");
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
