using System;
using App.Config;
using CardShare.Contracts.Config;
using UnityEngine;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>
    /// 商店货架 / 已购装备格。节点在预制体上直接挂到序列化字段，展示由 <see cref="Bind"/> 写入，点击通过 <see cref="Clicked"/> 抛出。
    /// </summary>
    public sealed class EquipShopIcon : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private Image bgcolor;
        [SerializeField] private GameObject nohave;
        [SerializeField] private Button button;

        public int RelicConfigId { get; private set; }

        public event Action<EquipShopIcon> Clicked;

        public void Bind(RelicConfig relic, Sprite sprite = null)
        {
            RelicConfigId = relic != null ? relic.Id : 0;
            SetIcon(sprite);
            if (bgcolor != null)
            {
                bgcolor.color = ThemeColors.EquipSlot(relic != null, relic != null ? relic.Type : QualityType.Ordinary);
            }

            if (nohave != null)
            {
                nohave.SetActive(relic == null);
            }
        }

        public void BindClick(Action<EquipShopIcon> onClick)
        {
            Clicked = null;
            if (onClick != null)
            {
                Clicked += onClick;
            }

            HookClick();
        }

        public void SetIcon(Sprite sprite)
        {
            EnsureRefs();
            if (icon == null)
            {
                return;
            }

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        private void Awake()
        {
            EnsureRefs();
            HookClick();
        }

        private void OnEnable()
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
            EnsureRefs();
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (RelicConfigId <= 0)
            {
                return;
            }

            Clicked?.Invoke(this);
        }

        private void EnsureRefs()
        {
            if (icon == null)
            {
                icon = FindNamed<Image>("icon");
            }

            if (bgcolor == null)
            {
                bgcolor = FindNamed<Image>("bgcolor");
            }

            if (nohave == null)
            {
                var node = FindNamed<Transform>("nohave");
                if (node != null)
                {
                    nohave = node.gameObject;
                }
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
            if (icon == null)
            {
                icon = FindNamed<Image>("icon");
            }

            if (bgcolor == null)
            {
                bgcolor = FindNamed<Image>("bgcolor");
            }

            if (nohave == null)
            {
                var node = FindNamed<Transform>("nohave");
                if (node != null)
                {
                    nohave = node.gameObject;
                }
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }
        }
#endif
    }
}
