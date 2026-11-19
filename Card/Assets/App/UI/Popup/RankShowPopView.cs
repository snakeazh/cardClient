using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    [AutoScreen(AppScreenIds.RankShowPop, UILayer.Popup, ResResourcePaths.RankShowPop)]
    public sealed class RankShowPopView : ViewBase<RankShowPopViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindCommand(UI.Get<Button>("CloseBtn"), ViewModel.CloseCommand);
            Binding.BindCommand(UI.Get<Button>("ClaimBtn"), ViewModel.ClaimCommand);
            Binding.BindText(UI.Get<TMP_Text>("ClaimLabel"), ViewModel.ClaimLabel);

            var previous = UI.Get<Image>("PreviousBadge");
            var current = UI.Get<Image>("CurrentBadge");
            var next = UI.Get<Image>("NextBadge");
            var rankName = UI.Get<TMP_Text>("RankName");
            var goldAmount = UI.Get<TMP_Text>("GoldAmount");
            Binding.Add(ViewModel.Data.Subscribe(data =>
            {
                SetBadge(previous, data?.PreviousBadge);
                SetBadge(current, data?.CurrentBadge);
                SetBadge(next, data?.NextBadge);
                rankName.text = string.IsNullOrWhiteSpace(data?.RankName) ? "—" : data.RankName;
                goldAmount.text = data == null ? "—" : $"+{data.GoldAmount}";
            }));
        }

        private static void SetBadge(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
        }
    }
}
