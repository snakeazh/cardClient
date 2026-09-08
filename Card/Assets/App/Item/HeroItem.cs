using System;
using App.Config;
using App.Game;
using UnityEngine;
using UnityEngine.UI;

namespace App.Item
{
    /// <summary>
    /// 选角列表单卡。内容走内部 PlayerItem；选中播 aini_card_select。
    /// </summary>
    public sealed class HeroItem : MonoBehaviour
    {
        public const string SelectAnim = "aini_card_select";
        public const string DefaultAnim = "ani_default";

        [SerializeField] private PlayerItem playerItem;
        [SerializeField] private Button button;

        private bool _hasSelectAnim;
        private bool _selectAnimOn;

        public HeroConfig Data { get; private set; }

        public event Action<HeroItem> Clicked;

        public void Bind(HeroConfig hero, Sprite portrait, bool selected, bool unlocked, bool forceSelect = false)
        {
            Data = hero;
            EnsureRefs();
            SetSelected(selected, forceSelect);
            if (playerItem == null)
            {
                return;
            }

            playerItem.ApplyTheme();
            playerItem.SetName(unlocked && hero != null ? hero.Name : PlayerItem.LockedStatText);
            playerItem.SetPortrait(portrait, locked: !unlocked);
            if (unlocked && hero != null)
            {
                playerItem.SetHp(hero.Hp);
                playerItem.SetAttack(hero.HeroDamage);
            }

            playerItem.SetUnlocked(unlocked && hero != null);
        }

        public void SetSelected(bool selected, bool force = false)
        {
            EnsureRefs();
            if (!force && _hasSelectAnim && _selectAnimOn == selected)
            {
                return;
            }

            _hasSelectAnim = true;
            _selectAnimOn = selected;
            ApplySelectAnim();
        }

        private void ApplySelectAnim()
        {
            EnsureRefs();
            if (playerItem == null)
            {
                return;
            }

            playerItem.SetSelectLift(_selectAnimOn, SelectAnim, DefaultAnim);
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
