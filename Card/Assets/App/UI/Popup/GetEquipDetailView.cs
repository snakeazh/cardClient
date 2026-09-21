using App.Config;
using CardShare.Contracts.Config;
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
    /// 局内新解锁遗物弹窗。Item 卡显示遗物名/图标/品质并播翻卡演出；
    /// Detail / AcquireMethod / equipNum 绑定文案；LeftBtn=上、RightBtn=下切换多件；
    /// get 确定与 Mask 关闭。Tip / CongratulationsImg 用预制体静态展示。
    /// </summary>
    [AutoScreen(AppScreenIds.GetEquipDetail, UILayer.TopMost, ResResourcePaths.GetEquipDetail)]
    public sealed class GetEquipDetailView : ViewBase<GetEquipDetailViewModel>
    {
        private ItemCard _card;
        private bool _revealPlayed;

        protected override void OnBind()
        {
            _card = UI.GetGameObject("Item").GetComponent<ItemCard>();
            if (_card != null)
            {
                _card.SetShadowVisible(false);
                _card.SetUnlocked(true);
                Binding.Add(ViewModel.NameText.Subscribe(_card.SetName));
                Binding.Add(ViewModel.IconSprite.Subscribe(ApplyIcon, emitCurrent: true));
                Binding.Add(ViewModel.Quality.Subscribe(OnQualityChanged, emitCurrent: true));
            }

            Binding.BindText(UI.GetGameObject("Detail").GetComponent<TMP_Text>(), ViewModel.DescText);
            Binding.BindText(UI.GetGameObject("equipNum").GetComponent<TMP_Text>(), ViewModel.EquipNumText);

            var acquire = UI.GetGameObject("AcquireMethod");
            Binding.BindActive(acquire, ViewModel.ShowAcquireMethod);
            Binding.BindText(acquire.GetComponent<TMP_Text>(), ViewModel.AcquireMethodText);

            // 预制体节点名仍是 LeftBtn/RightBtn，语义为上/下切换
            var up = UI.GetGameObject("LeftBtn");
            var down = UI.GetGameObject("RightBtn");
            Binding.BindCommand(up.GetComponent<Button>(), ViewModel.PrevCommand);
            Binding.BindCommand(down.GetComponent<Button>(), ViewModel.NextCommand);
            Binding.BindActive(up, ViewModel.ShowSwitch);
            Binding.BindActive(down, ViewModel.ShowSwitch);

            Binding.BindCommand(UI.GetGameObject("get").GetComponent<Button>(), ViewModel.CloseCommand);
            BindMaskClose();
        }

        private void OnQualityChanged(QualityType quality)
        {
            if (_card == null)
            {
                return;
            }

            _card.ApplyQuality(quality);
            // 仅首次打开播翻卡；上下切换只换内容，避免反复演出
            if (!_revealPlayed)
            {
                _revealPlayed = true;
                _card.PlayRewardReveal(quality, showChoukaEffect: false);
            }
        }

        private void ApplyIcon(Sprite sprite)
        {
            if (_card == null || sprite == null)
            {
                return;
            }

            _card.SetIcon(sprite);
        }

        private void BindMaskClose()
        {
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
