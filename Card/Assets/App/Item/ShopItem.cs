using System;
using System.Collections.Generic;
using App.Config;
using App.Resources;
using App.UI;
using CardShare.Contracts.Config;
using App.Item;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>格子形态：正常卡 / 空槽位 / 购买槽位。</summary>
    public enum ShopItemState
    {
        Normal,
        Empty,
        BuySlot
    }

    /// <summary>
    /// 商店货架单卡。节点在预制体上直接挂到序列化字段，展示由 <see cref="Bind"/> 写入，点击通过 <see cref="Clicked"/> 抛出。
    /// </summary>
    public sealed class ShopItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public const string ChooseStartAnim = "ani_shop_choose_start";
        public const string ChooseEndAnim = "ani_shop_choose_end";

        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardIcon;
        [SerializeField] private Image iconBg;
        [SerializeField] private Image iconTitleBg;
        [SerializeField] private Image cardCircle;
        [SerializeField] private TMP_Text goldNum;
        [SerializeField] private TMP_Text lv;
        [SerializeField] private TMP_Text hasNum;
        [SerializeField] private Image gold;
        [SerializeField] private Button button;
        [SerializeField] private Animator animator;
        [SerializeField] private GameObject normalRoot;
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private GameObject buySlotRoot;
        [SerializeField] private RectTransform battleHor;
        [SerializeField] private Image bondIcon;

        public ShopItemDef Data { get; private set; }

        public ShopItemState State { get; private set; } = ShopItemState.Normal;

        public int RelicConfigId => Data != null ? Data.RelicConfigId : 0;

        /// <summary>引导挖孔：卡面 + 下方金币价（价格常溢出父布局高度）。</summary>
        public void CollectGuideHoleRects(List<RectTransform> into)
        {
            if (into == null)
            {
                return;
            }

            var root = transform as RectTransform;
            if (root != null)
            {
                into.Add(root);
            }

            EnsurePriceText();
            if (goldNum != null)
            {
                into.Add(goldNum.rectTransform);
            }

            if (gold != null)
            {
                into.Add(gold.rectTransform);
            }
        }

        public event Action<ShopItem> Clicked;
        public event Action<ShopItem, PointerEventData> DragBegan;
        public event Action<ShopItem, PointerEventData> DragMoved;
        public event Action<ShopItem, PointerEventData> DragEnded;

        private Sprite _defaultIcon;
        private bool _suppressClick;
        private ItemCard _innerCard;
        private Color _priceColor = Color.black;
        private bool _priceColorCached;
        private Color _buySlotPriceColor = Color.black;
        private bool _buySlotPriceColorCached;
        private readonly List<Image> _bondIcons = new List<Image>(4);
        private int _bondSerial;

        public void Bind(ShopItemDef def, Sprite icon = null, Sprite goldSprite = null, bool affordable = true, int level = 1, int count = 0)
        {
            // 池化格子上一轮可能是 Empty/BuySlot 态，绑定数据即回到正常卡形态。
            SetState(ShopItemState.Normal);
            Data = def;
            SetName(def != null ? def.Name : string.Empty);
            SetIcon(icon);
            SetLevel(level);
            SetHasNum(count);
            SetGoldNum(def != null ? def.Price : 0, affordable);
            if (goldSprite != null)
            {
                SetGoldIcon(goldSprite);
            }

            ApplyBonds(null);
        }

        public void Bind(RelicConfig relic, Sprite icon = null, Sprite goldSprite = null, bool forSale = true, int sellPrice = -1, int buyPrice = -1, bool affordable = true, int level = 1, int count = 0)
        {
            Bind(relic == null
                ? null
                : new ShopItemDef
                {
                    Id = relic.Id.ToString(),
                    Name = relic.Name,
                    Effect = relic.Desc,
                    Price = forSale
                        ? (buyPrice >= 0 ? buyPrice : relic.Price)
                        : (sellPrice >= 0 ? sellPrice : Math.Max(0, relic.SellingPrice)),
                    RelicConfigId = relic.Id
                }, icon, goldSprite, affordable, level, count);
            ApplyQuality(relic != null ? relic.Type : QualityType.Ordinary);
            ApplyBonds(relic != null ? relic.BindingID : null);
        }

        public void Bind(string name, Sprite icon, int price, Sprite goldSprite = null)
        {
            Bind(new ShopItemDef { Name = name, Price = price }, icon, goldSprite);
        }

        public void SetName(string name)
        {
            if (cardName != null)
            {
                cardName.text = name ?? string.Empty;
            }
        }

        public void SetIcon(Sprite icon)
        {
            if (cardIcon == null)
            {
                return;
            }

            cardIcon.sprite = icon != null ? icon : _defaultIcon;
            cardIcon.enabled = cardIcon.sprite != null;
        }

        public void SetLevel(int level)
        {
            EnsureLevelText();
            if (lv == null)
            {
                return;
            }

            var shown = level > 0 ? level : 1;
            lv.text = "LV" + shown;
        }

        /// <summary>持有数量。0 隐藏；大于 0 显示 xN。</summary>
        public void SetHasNum(int count)
        {
            EnsureHasNumText();
            if (hasNum == null)
            {
                return;
            }

            var show = count > 0;
            hasNum.gameObject.SetActive(show);
            if (show)
            {
                hasNum.text = "数量x" + count;
            }
        }

        public void SetGoldNum(int price, bool affordable = true)
        {
            EnsurePriceText();
            if (goldNum != null)
            {
                if (!_priceColorCached)
                {
                    _priceColor = goldNum.color;
                    _priceColorCached = true;
                }

                goldNum.text = Mathf.Max(0, price).ToString();
                goldNum.color = affordable ? _priceColor : ThemeColors.ShopPriceUnaffordable;
            }

            if (gold != null)
            {
                gold.enabled = true;
            }
        }

        public void SetGoldIcon(Sprite goldSprite)
        {
            if (gold == null || goldSprite == null)
            {
                return;
            }

            gold.sprite = goldSprite;
            gold.enabled = true;
        }

        /// <summary>
        /// 三态互斥显隐：Normal = 正常卡（Item 子树），Empty = 空槽位，BuySlot = 购买槽位。
        /// 非 Normal 态清掉 Data，避免池化格子的残留配置被当成可点击圣物。
        /// </summary>
        public void SetState(ShopItemState state)
        {
            EnsureStateRoots();
            State = state;
            if (state != ShopItemState.Normal)
            {
                Data = null;
            }

            if (normalRoot != null)
            {
                normalRoot.SetActive(state == ShopItemState.Normal);
            }

            if (emptyRoot != null)
            {
                emptyRoot.SetActive(state == ShopItemState.Empty);
            }

            if (buySlotRoot != null)
            {
                buySlotRoot.SetActive(state == ShopItemState.BuySlot);
            }

            if (state != ShopItemState.Normal)
            {
                ApplyBonds(null);
            }
        }

        /// <summary>
        /// 买槽格内容。PVP 显示金币价格（Price 子树，买不起变红）；PVE 看广告解锁时切到 AD 图。
        /// BuySlot 子树内自带 priceValue，与正常卡的 priceValue 同名，须限定子树查找。
        /// </summary>
        public void SetBuySlotContent(int price, bool affordable, bool showPrice)
        {
            EnsureStateRoots();
            if (buySlotRoot == null)
            {
                return;
            }

            var ad = FindNamedUnder<Transform>(buySlotRoot.transform, "AD");
            if (ad != null)
            {
                ad.gameObject.SetActive(!showPrice);
            }

            var priceRoot = FindNamedUnder<Transform>(buySlotRoot.transform, "Price");
            if (priceRoot != null)
            {
                priceRoot.gameObject.SetActive(showPrice);
            }

            if (!showPrice)
            {
                return;
            }

            var priceText = FindNamedUnder<TMP_Text>(buySlotRoot.transform, "priceValue");
            if (priceText == null)
            {
                return;
            }

            if (!_buySlotPriceColorCached)
            {
                _buySlotPriceColor = priceText.color;
                _buySlotPriceColorCached = true;
            }

            priceText.text = Mathf.Max(0, price).ToString();
            priceText.color = affordable ? _buySlotPriceColor : ThemeColors.ShopPriceUnaffordable;
        }

        /// <summary>
        /// 品质通过内嵌 ItemCard 换品质卡背（Altas/playitem），与图鉴/天赋一致；
        /// 未内嵌 ItemCard 的货架卡不做品质处理。
        /// </summary>
        public void ApplyQuality(QualityType type)
        {
            if (_innerCard == null)
            {
                HookInnerCard();
            }

            _innerCard?.ApplyQuality(type);
        }

        /// <summary>
        /// battleHor 展示这件圣物所属羁绊的图标。没有 BindingID、空槽、买槽时收起。
        /// 图标未进缓存时先藏起来，加载完成后再刷一次。
        /// </summary>
        private void ApplyBonds(int[] bindingIds)
        {
            var serial = ++_bondSerial;
            PaintBonds(bindingIds, serial, requestLoad: true);
        }

        private void PaintBonds(int[] bindingIds, int serial, bool requestLoad)
        {
            EnsureBattleHor();
            HideBondIcons();
            if (State != ShopItemState.Normal || bindingIds == null || bindingIds.Length == 0)
            {
                SetBattleHorActive(false);
                return;
            }

            var missing = false;
            var shown = 0;
            for (var i = 0; i < bindingIds.Length; i++)
            {
                var id = bindingIds[i];
                if (id <= 0 || !BindingConfig.TryGet(id, out var binding) || binding == null)
                {
                    continue;
                }

                var stem = ResResourcePaths.BondArtName(binding.Type);
                if (string.IsNullOrEmpty(stem))
                {
                    continue;
                }

                if (!BondIconLoader.TryGet(stem, out var sprite))
                {
                    missing = true;
                    continue;
                }

                var image = RentBondIcon();
                if (image == null)
                {
                    continue;
                }

                image.sprite = sprite;
                image.color = Color.white;
                image.enabled = true;
                image.raycastTarget = false;
                image.gameObject.SetActive(true);
                shown++;
            }

            SetBattleHorActive(shown > 0);
            if (shown > 0 && battleHor != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(battleHor);
            }

            if (missing && requestLoad)
            {
                _ = LoadBonds(serial, bindingIds);
            }
        }

        private async System.Threading.Tasks.Task LoadBonds(int serial, int[] bindingIds)
        {
            for (var i = 0; i < bindingIds.Length; i++)
            {
                var id = bindingIds[i];
                if (id <= 0 || !BindingConfig.TryGet(id, out var binding) || binding == null)
                {
                    continue;
                }

                await BondIconLoader.EnsureAsync(ResResourcePaths.BondArtName(binding.Type));
                if (serial != _bondSerial)
                {
                    return;
                }
            }

            if (serial != _bondSerial)
            {
                return;
            }

            PaintBonds(bindingIds, serial, requestLoad: false);
        }

        private void HideBondIcons()
        {
            if (bondIcon != null)
            {
                bondIcon.gameObject.SetActive(false);
            }

            for (var i = 0; i < _bondIcons.Count; i++)
            {
                var image = _bondIcons[i];
                if (image != null)
                {
                    image.gameObject.SetActive(false);
                }
            }
        }

        private Image RentBondIcon()
        {
            EnsureBattleHor();
            if (bondIcon == null || battleHor == null)
            {
                return null;
            }

            for (var i = 0; i < _bondIcons.Count; i++)
            {
                var existing = _bondIcons[i];
                if (existing != null && !existing.gameObject.activeSelf)
                {
                    return existing;
                }
            }

            var clone = Instantiate(bondIcon, battleHor);
            clone.raycastTarget = false;
            _bondIcons.Add(clone);
            return clone;
        }

        private void SetBattleHorActive(bool active)
        {
            if (battleHor != null)
            {
                battleHor.gameObject.SetActive(active);
            }
        }

        private void EnsureBattleHor()
        {
            if (battleHor == null)
            {
                battleHor = FindNamed<RectTransform>("battleHor");
            }

            if (bondIcon == null && battleHor != null)
            {
                bondIcon = FindNamedUnder<Image>(battleHor, "icon");
            }
        }

        public void BindClick(Action<ShopItem> onClick)
        {
            Clicked = null;
            if (onClick != null)
            {
                Clicked += onClick;
            }
        }

        /// <summary>
        /// 播放 ShopRoot 上 Animator 的指定状态。预制体挂 ShopRoot.controller（无参数，靠状态名播放）。
        /// </summary>
        public void PlayAnimation(string stateName, int layer = 0, float normalizedTime = 0f)
        {
            EnsureAnimator();
            if (animator != null && !string.IsNullOrEmpty(stateName))
            {
                animator.Play(stateName, layer, normalizedTime);
            }
        }

        public void PlayChooseStart()
        {
            PlayAnimation(ChooseStartAnim);
        }

        public void PlayChooseEnd()
        {
            PlayAnimation(ChooseEndAnim);
        }

        public void BindDrag(
            Action<ShopItem, PointerEventData> onBegan,
            Action<ShopItem, PointerEventData> onMoved,
            Action<ShopItem, PointerEventData> onEnded)
        {
            DragBegan = null;
            DragMoved = null;
            DragEnded = null;
            if (onBegan != null)
            {
                DragBegan += onBegan;
            }

            if (onMoved != null)
            {
                DragMoved += onMoved;
            }

            if (onEnded != null)
            {
                DragEnded += onEnded;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _suppressClick = true;
            DragBegan?.Invoke(this, eventData);
            ForwardDrag(eventData, ExecuteEvents.beginDragHandler);
        }

        public void OnDrag(PointerEventData eventData)
        {
            DragMoved?.Invoke(this, eventData);
            ForwardDrag(eventData, ExecuteEvents.dragHandler);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _suppressClick = false;
            DragEnded?.Invoke(this, eventData);
            ForwardDrag(eventData, ExecuteEvents.endDragHandler);
        }

        private void ForwardDrag<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> handler)
            where T : IEventSystemHandler
        {
            if (eventData == null || transform.parent == null)
            {
                return;
            }

            ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, handler);
        }

        private void Awake()
        {
            EnsurePriceText();
            if (cardIcon != null)
            {
                _defaultIcon = cardIcon.sprite;
            }

            HookClick();
            HookInnerCard();
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }

            if (_innerCard != null)
            {
                _innerCard.Clicked -= HandleInnerClick;
                _innerCard = null;
            }

            Clicked = null;
            DragBegan = null;
            DragMoved = null;
            DragEnded = null;
            _bondSerial++;
        }

        private void HookClick()
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            Clicked?.Invoke(this);
        }

        private void HookInnerCard()
        {
            _innerCard = GetComponentInChildren<ItemCard>(true);
            if (_innerCard == null)
            {
                return;
            }

            _innerCard.Clicked -= HandleInnerClick;
            _innerCard.Clicked += HandleInnerClick;
        }

        private void HandleInnerClick(ItemCard _)
        {
            HandleClick();
        }

        private void EnsureHasNumText()
        {
            if (hasNum != null)
            {
                return;
            }

            hasNum = FindNamed<TMP_Text>("hasNum");
        }

        private void EnsureLevelText()
        {
            if (lv != null)
            {
                return;
            }

            lv = FindNamed<TMP_Text>("lv");
        }

        private void EnsurePriceText()
        {
            if (goldNum != null)
            {
                return;
            }

            // BuySlot 子树里也有同名 priceValue，须限定在正常卡（Item）子树内查找。
            EnsureStateRoots();
            var normal = normalRoot != null ? normalRoot.transform : null;
            goldNum = FindNamedUnder<TMP_Text>(normal, "priceValue") ?? FindNamedUnder<TMP_Text>(normal, "goldNum");
        }

        private void EnsureAnimator()
        {
            if (animator != null)
            {
                return;
            }

            var nodes = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].name != "ShopRoot")
                {
                    continue;
                }

                animator = nodes[i].GetComponent<Animator>();
                if (animator != null)
                {
                    return;
                }
            }
        }

        private T FindNamed<T>(string nodeName) where T : Component
        {
            var nodes = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].name == nodeName)
                {
                    return nodes[i].GetComponent<T>();
                }
            }

            return null;
        }

        private void EnsureStateRoots()
        {
            if (normalRoot == null)
            {
                var node = FindNamed<Transform>("Item");
                normalRoot = node != null ? node.gameObject : null;
            }

            if (emptyRoot == null)
            {
                var node = FindNamed<Transform>("IsEmpty");
                emptyRoot = node != null ? node.gameObject : null;
            }

            if (buySlotRoot == null)
            {
                var node = FindNamed<Transform>("BuySlot");
                buySlotRoot = node != null ? node.gameObject : null;
            }
        }

        /// <summary>限定 root 子树内按名查找，避免与其它子树的同名节点（如正常卡与 BuySlot 各有 priceValue）混淆。</summary>
        private static T FindNamedUnder<T>(Transform root, string nodeName) where T : Component
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == nodeName)
            {
                return root.GetComponent<T>();
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamedUnder<T>(root.GetChild(i), nodeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            WireSerializedRefs();
        }

        private void OnValidate()
        {
            WireSerializedRefs();
        }

        private void WireSerializedRefs()
        {
            if (cardName == null)
            {
                cardName = FindNamed<TMP_Text>("card_Name");
            }

            if (cardIcon == null)
            {
                cardIcon = FindNamed<Image>("card_icon");
            }

            if (iconBg == null)
            {
                iconBg = FindNamed<Image>("IconBG");
            }

            if (iconTitleBg == null)
            {
                iconTitleBg = FindNamed<Image>("IconTitleBG");
            }

            if (cardCircle == null)
            {
                cardCircle = FindNamed<Image>("card_Circle");
            }

            if (goldNum == null)
            {
                goldNum = FindNamed<TMP_Text>("priceValue") ?? FindNamed<TMP_Text>("goldNum");
            }

            if (lv == null)
            {
                lv = FindNamed<TMP_Text>("lv");
            }

            if (hasNum == null)
            {
                hasNum = FindNamed<TMP_Text>("hasNum");
            }

            if (gold == null)
            {
                gold = FindNamed<Image>("gold");
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (animator == null)
            {
                animator = FindNamed<Animator>("ShopRoot");
            }

            if (normalRoot == null)
            {
                var node = FindNamed<Transform>("Item");
                normalRoot = node != null ? node.gameObject : null;
            }

            if (emptyRoot == null)
            {
                var node = FindNamed<Transform>("IsEmpty");
                emptyRoot = node != null ? node.gameObject : null;
            }

            if (buySlotRoot == null)
            {
                var node = FindNamed<Transform>("BuySlot");
                buySlotRoot = node != null ? node.gameObject : null;
            }

            if (battleHor == null)
            {
                battleHor = FindNamed<RectTransform>("battleHor");
            }

            if (bondIcon == null && battleHor != null)
            {
                bondIcon = FindNamedUnder<Image>(battleHor, "icon");
            }
        }
#endif
    }
}
