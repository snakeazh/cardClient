using System;
using App.Config;
using App.Item;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.Game
{
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
        [SerializeField] private Image gold;
        [SerializeField] private Button button;
        [SerializeField] private Animator animator;

        public ShopItemDef Data { get; private set; }

        public int RelicConfigId => Data != null ? Data.RelicConfigId : 0;

        public event Action<ShopItem> Clicked;
        public event Action<ShopItem, PointerEventData> DragBegan;
        public event Action<ShopItem, PointerEventData> DragMoved;
        public event Action<ShopItem, PointerEventData> DragEnded;

        private Sprite _defaultIcon;
        private bool _suppressClick;
        private ItemCard _innerCard;
        private Color _priceColor = Color.black;
        private bool _priceColorCached;

        public void Bind(ShopItemDef def, Sprite icon = null, Sprite goldSprite = null, bool affordable = true)
        {
            Data = def;
            SetName(def != null ? def.Name : string.Empty);
            SetIcon(icon);
            SetGoldNum(def != null ? def.Price : 0, affordable);
            if (goldSprite != null)
            {
                SetGoldIcon(goldSprite);
            }
        }

        public void Bind(RelicConfig relic, Sprite icon = null, Sprite goldSprite = null, bool forSale = true, int sellPrice = -1, int buyPrice = -1, bool affordable = true)
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
                }, icon, goldSprite, affordable);
            ApplyQuality(relic != null ? relic.Type : QualityType.Ordinary);
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
        /// 品质通过内嵌 ItemCard 换品质卡面图（Altas/ItemBg），与图鉴/天赋一致；
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

        private void EnsurePriceText()
        {
            if (goldNum != null)
            {
                return;
            }

            goldNum = FindNamed<TMP_Text>("priceValue") ?? FindNamed<TMP_Text>("goldNum");
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
        }
#endif
    }
}
