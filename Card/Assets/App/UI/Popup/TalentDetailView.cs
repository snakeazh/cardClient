using System;
using App.Item;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋详情弹窗。注册在 TopMost 层：叠加在 Popup 层的天赋列表之上，弹出时不隐藏列表。
    /// Item 卡显示天赋名、图标与等级角标，Detail 显示当前等级描述；LeftBtn/RightBtn 切换已解锁天赋，
    /// 不足两个时隐藏；UpgradeBtn 看广告升级，满级隐藏；Tip 为预制体固定文案；点 Mask 关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentDetail, UILayer.TopMost, ResResourcePaths.TalentDetail)]
    public sealed class TalentDetailView : ViewBase<TalentDetailViewModel>
    {
        private ItemCard _card;

        protected override void OnBind()
        {
            _card = UI.GetGameObject("Item").GetComponent<ItemCard>();
            if (_card != null)
            {
                _card.SetShadowVisible(false);
                _card.SetAnimationEnabled(false);
                // 不清 card_icon：无配置 Icon 时保留预制体默认图
                _card.SetUnlocked(true);
                Binding.Add(ViewModel.NameText.Subscribe(_card.SetName));
                Binding.Add(ViewModel.LevelText.Subscribe(_card.SetLevel));
                // 品质边框（Altas/ItemBg），切换/升级换行时随快照刷新
                Binding.Add(ViewModel.Quality.Subscribe(_card.ApplyQuality));
                Binding.Add(ViewModel.IconKey.Subscribe(LoadDetailIcon));
            }

            var left = UI.GetGameObject("LeftBtn");
            var right = UI.GetGameObject("RightBtn");
            Binding.BindCommand(left.GetComponent<Button>(), ViewModel.PrevCommand);
            Binding.BindCommand(right.GetComponent<Button>(), ViewModel.NextCommand);
            Binding.BindActive(left, ViewModel.ShowSwitch);
            Binding.BindActive(right, ViewModel.ShowSwitch);

            // UpgradeBtn 看广告升级，满级整体隐藏（同 LeftBtn/RightBtn 的缺失兜底）
            var upgrade = UI.GetGameObject("UpgradeBtn");
            if (upgrade != null)
            {
                var button = upgrade.GetComponent<Button>();
                if (button == null)
                {
                    button = upgrade.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                }

                Binding.BindCommand(button, ViewModel.UpgradeCommand);
            }

            Binding.BindActive(upgrade, ViewModel.ShowUpgrade);

            Binding.BindText(UI.GetGameObject("Detail").GetComponent<TMP_Text>(), ViewModel.DescText);
            BindMaskClose();
        }

        /// <summary>图标在 Altas/Talent 图集（sprite 名=TalentConfig.Icon）；缺图保留预制体默认图。</summary>
        private void LoadDetailIcon(string key)
        {
            if (_card == null || string.IsNullOrEmpty(key) || ViewModel.Atlas == null)
            {
                return;
            }

            if (ViewModel.Atlas.TryGetSprite(ResResourcePaths.TalentAtlas, key, out var sprite) && sprite != null)
            {
                _card.SetIcon(sprite);
            }
        }

        private void BindMaskClose()
        {
            // 预制体优先经 UIReference 挂 Mask 的 Button；缺引用时退回按名查找并运行时补 Button。
            if (!UI.TryGet<Button>("Mask", out var overlay))
            {
                var mask = transform.Find("Mask");
                if (mask == null)
                {
                    return;
                }

                overlay = mask.GetComponent<Button>();
                if (overlay == null)
                {
                    overlay = mask.gameObject.AddComponent<Button>();
                    overlay.transition = Selectable.Transition.None;
                }
            }

            Binding.BindCommand(overlay, ViewModel.CloseCommand);
        }
    }
}
