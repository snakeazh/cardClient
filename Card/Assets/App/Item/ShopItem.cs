using System;
using App.Config;
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
        [SerializeField] private TMP_Text goldNum;
        [SerializeField] private Image gold;
        [SerializeField] private Button button;
        [SerializeField] private Animator animator;

        public ShopItemDef Data { get; private set; }

        
        
        
        public event Action<ShopItem> Clicked;
        public event Action<ShopItem, PointerEventData> DragBegan;
        public event Action<ShopItem, PointerEventData> DragMoved;
        public event Action<ShopItem, PointerEventData> DragEnded;

        private Sprite _defaultIcon;
        private bool _suppressClick;

        public void Bind(ShopItemDef def, Sprite icon = null, Sprite goldSprite = null)
        {
            Data = def;
            SetName(def != null ? def.Name : string.Empty);
            SetIcon(icon);
            SetGoldNum(def != null ? def.Price : 0);
            if (goldSprite != null)
            {
                SetGoldIcon(goldSprite);
            }
        }

        public void Bind(RelicConfig relic, Sprite icon = null, Sprite goldSprite = null, bool forSale = true)
        {
            Bind(relic == null
                ? null
                : new ShopItemDef
                {
                    Id = relic.Id.ToString(),
                    Name = relic.Name,
                    Effect = relic.Desc,
                    Price = forSale ? relic.Price : relic.SellingPrice,
                    RelicConfigId = relic.Id
                }, icon, goldSprite);
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

        public void SetGoldNum(int price)
        {
            if (goldNum != null)
            {
                goldNum.text = Mathf.Max(0, price).ToString();
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
        }

        public void OnDrag(PointerEventData eventData)
        {
            DragMoved?.Invoke(this, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _suppressClick = false;
            DragEnded?.Invoke(this, eventData);
        }

        private void Awake()
        {
            if (cardIcon != null)
            {
                _defaultIcon = cardIcon.sprite;
            }

            HookClick();
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
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

            if (goldNum == null)
            {
                goldNum = FindNamed<TMP_Text>("goldNum");
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
#endif
    }
}
