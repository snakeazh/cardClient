using System;
using App.Config;
using App.Game;
using UnityEngine;
using UnityEngine.UI;

namespace App.Item
{
    /// <summary>
    /// 选角列表单卡。选中时显示 heroonselect，内容走内部 PlayerItem。
    /// </summary>
    public sealed class HeroItem : MonoBehaviour
    {
        [SerializeField] private GameObject selectFrame;
        [SerializeField] private PlayerItem playerItem;
        [SerializeField] private Button button;

        public HeroConfig Data { get; private set; }

        public event Action<HeroItem> Clicked;

        public void Bind(HeroConfig hero, Sprite portrait, bool selected, bool unlocked)
        {
            Data = hero;
            EnsureRefs();
            SetSelected(selected);
            if (playerItem == null)
            {
                return;
            }

            playerItem.ApplyTheme(false);
            playerItem.SetName(unlocked && hero != null ? hero.Name : "???");
            playerItem.SetHp(unlocked && hero != null ? hero.Hp : 0);
            playerItem.SetAttack(unlocked && hero != null ? hero.HeroDamage : 0);
            playerItem.SetState(string.Empty);
            playerItem.SetPortrait(portrait, locked: !unlocked);
        }

        public void SetSelected(bool selected)
        {
            EnsureRefs();
            if (selectFrame != null)
            {
                selectFrame.SetActive(selected);
            }
        }

        public void BindClick(Action<HeroItem> onClick)
        {
            Clicked = null;
            if (onClick != null)
            {
                Clicked += onClick;
            }
        }

        private void Awake()
        {
            EnsureRefs();
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

        private void EnsureRefs()
        {
            if (selectFrame == null)
            {
                var node = transform.Find("heroonselect");
                if (node != null)
                {
                    selectFrame = node.gameObject;
                }
            }

            if (playerItem == null)
            {
                playerItem = GetComponentInChildren<PlayerItem>(true);
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }
        }
    }
}
