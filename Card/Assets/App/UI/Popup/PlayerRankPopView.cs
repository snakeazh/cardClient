using System.Threading.Tasks;
using App.Resources;
using CardShare.Contracts;
using Framework.UI.List;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    [AutoScreen(AppScreenIds.PlayerRankPop, UILayer.Popup, ResResourcePaths.PlayerRankPop)]
    public sealed class PlayerRankPopView : ViewBase<PlayerRankPopViewModel>
    {
        private ScrollRect _scroll;
        private RecycleListView _list;
        private PlayerRankAvatarView _selfAvatar;
        private TMP_Text _selfPlace;
        private TMP_Text _selfName;
        private TMP_Text _selfScore;

        protected override void OnBind()
        {
            _scroll = UI.Get<ScrollRect>("RankScroll");
            _list = _scroll.GetComponent<RecycleListView>();
            _selfAvatar = UI.Get<PlayerRankAvatarView>("MyAvatar");
            _selfPlace = UI.Get<TMP_Text>("MyPlace");
            _selfName = UI.Get<TMP_Text>("MyNickName");
            _selfScore = UI.Get<TMP_Text>("MyScore");

            var rowTemplate = UI.Get<PlayerRankRowView>("RowTemplate");
            UI.GetGameObject("EditorPreview").SetActive(false);
            _list.Initialize(_scroll, rowTemplate.gameObject, rowTemplate.RectTransform.rect.height, 10f);
            Binding.BindCommand(UI.Get<Button>("CloseBtn"), ViewModel.CloseCommand);
            Binding.BindCommand(UI.Get<Button>("MyRank"), ViewModel.OpenRankRewardsCommand);
            var status = UI.Get<TMP_Text>("RankStatus");
            Binding.Add(ViewModel.Status.Subscribe(message =>
            {
                status.text = message;
                status.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }));
            Binding.Add(ViewModel.Data.Subscribe(ShowData));
        }

        private void ShowData(PvpLeaderboardResponse data)
        {
            _scroll.StopMovement();
            _scroll.content.anchoredPosition = Vector2.zero;
            _list.SetItems(data?.Entries);

            var self = data?.Self;
            _selfAvatar.SetProfile(self?.AvatarUrl, null);
            _selfName.text = self?.NickName ?? string.Empty;
            _selfPlace.text = self == null ? "—" : self.Place?.ToString() ?? "未上榜";
            _selfScore.text = self?.Score.ToString() ?? "—";
        }

        protected override Task OnViewClose()
        {
            if (_list != null)
            {
                _list.SetItems<PvpLeaderboardEntry>(null);
            }
            if (_selfAvatar != null)
            {
                _selfAvatar.Clear();
            }
            return Task.CompletedTask;
        }
    }
}
