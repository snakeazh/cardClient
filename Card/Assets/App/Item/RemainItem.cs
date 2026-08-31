using System;
using App.Config;
using UnityEngine;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>
    /// 遗物列表物品格。节点在预制体上直接挂到序列化字段：空槽仅显示 Empty；
    /// 有遗物时按品质给 BgStyle 取色（白/蓝/紫/橙，见 <see cref="ThemeColors.ForQuality"/>），
    /// Frame 是 BgStyle 的子节点不参与换色。点击通过 <see cref="Clicked"/> 抛出。
    /// </summary>
    public sealed class RemainItem : MonoBehaviour
    {
        [SerializeField] private GameObject empty;
        [SerializeField] private Image bgStyle;
        [SerializeField] private Image icon;
        [SerializeField] private Button button;

        public int RelicConfigId { get; private set; }

        public event Action<RemainItem> Clicked;

        public void Bind(RelicConfig relic, Sprite sprite = null)
        {
            RelicConfigId = relic != null ? relic.Id : 0;
            SetIcon(sprite);
            if (empty != null)
            {
                empty.SetActive(relic == null);
            }

            if (bgStyle != null)
            {
                bgStyle.gameObject.SetActive(relic != null);
                if (relic != null)
                {
                    bgStyle.color = ThemeColors.ForQuality(relic.Type);
                }
            }
        }

        public void BindClick(Action<RemainItem> onClick)
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
            if (empty == null)
            {
                var node = FindNamed<Transform>("Empty");
                if (node != null)
                {
                    empty = node.gameObject;
                }
            }

            if (bgStyle == null)
            {
                bgStyle = FindNamed<Image>("BgStyle");
            }

            if (icon == null)
            {
                icon = FindNamed<Image>("Icon");
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
            if (empty == null)
            {
                var node = FindNamed<Transform>("Empty");
                if (node != null)
                {
                    empty = node.gameObject;
                }
            }

            if (bgStyle == null)
            {
                bgStyle = FindNamed<Image>("BgStyle");
            }

            if (icon == null)
            {
                icon = FindNamed<Image>("Icon");
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }
        }
#endif
    }
}
