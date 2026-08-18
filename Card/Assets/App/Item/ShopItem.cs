using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>
    /// 商店货架单卡。节点在预制体上直接挂到序列化字段，展示由 <see cref="Bind"/> 写入，点击通过 <see cref="Clicked"/> 抛出。
    /// </summary>
    public sealed class ShopItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text cardName;
        [SerializeField] private Image cardIcon;
        [SerializeField] private TMP_Text goldNum;
        [SerializeField] private Image gold;
        [SerializeField] private Button button;

        public ShopItemDef Data { get; private set; }

        public event Action<ShopItem> Clicked;

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
            if (cardIcon == null || icon == null)
            {
                return;
            }

            cardIcon.sprite = icon;
            cardIcon.enabled = true;
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

        private void Awake()
        {
            HookClick();
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }

            Clicked = null;
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
            Clicked?.Invoke(this);
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
