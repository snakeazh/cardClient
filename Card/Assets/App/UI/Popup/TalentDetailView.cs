using System;
using System.Threading.Tasks;
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
    /// Item 卡显示天赋名与图标，Detail 显示当前等级描述；LeftBtn/RightBtn 切换已解锁天赋，
    /// 不足两个时隐藏；Tip 为预制体固定文案；点 Mask 关闭。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentDetail, UILayer.TopMost, ResResourcePaths.TalentDetail)]
    public sealed class TalentDetailView : ViewBase<TalentDetailViewModel>
    {
        private ItemCard _card;

        /// <summary>图标加载请求序号，快速切换天赋时丢弃过期回填。</summary>
        private int _iconRequestId;

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
                Binding.Add(ViewModel.IconKey.Subscribe(LoadDetailIcon));
            }

            var left = UI.GetGameObject("LeftBtn");
            var right = UI.GetGameObject("RightBtn");
            Binding.BindCommand(left.GetComponent<Button>(), ViewModel.PrevCommand);
            Binding.BindCommand(right.GetComponent<Button>(), ViewModel.NextCommand);
            Binding.BindActive(left, ViewModel.ShowSwitch);
            Binding.BindActive(right, ViewModel.ShowSwitch);
            Binding.BindText(UI.GetGameObject("Detail").GetComponent<TMP_Text>(), ViewModel.DescText);
            BindMaskClose();
        }

        private void LoadDetailIcon(string key)
        {
            if (_card == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            var request = ++_iconRequestId;
            _ = LoadIconAsync(request, key);
        }

        private async Task LoadIconAsync(int request, string key)
        {
            Sprite sprite;
            try
            {
                sprite = await ViewModel.Resources.LoadAsync<Sprite>(key);
            }
            catch (Exception)
            {
                return;
            }

            // 过期请求（期间又切换了天赋）或页面已关闭则丢弃。
            if (request != _iconRequestId || sprite == null || _card == null)
            {
                return;
            }

            _card.SetIcon(sprite);
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
