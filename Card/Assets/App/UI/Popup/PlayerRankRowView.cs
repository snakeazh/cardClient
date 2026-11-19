using CardShare.Contracts;
using Framework.UI.List;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    public sealed class PlayerRankRowView : ListItemViewBase<PvpLeaderboardEntry>
    {
        [SerializeField] private Image _background;
        [SerializeField] private Sprite[] _placeBackgrounds;
        [SerializeField] private Image _placeBadge;
        [SerializeField] private Sprite[] _placeBadges;
        [SerializeField] private TMP_Text _place;
        [SerializeField] private PlayerRankAvatarView _avatar;
        [SerializeField] private TMP_Text _nickName;
        [SerializeField] private TMP_Text _rankName;
        [SerializeField] private Image _rankIcon;
        [SerializeField] private TMP_Text _score;

        protected override void OnBindItem(PvpLeaderboardEntry item, int index)
        {
            var place = item.Place.GetValueOrDefault();
            var podium = place >= 1 && place <= 3;
            var podiumIndex = podium ? (int)place - 1 : 3;
            _background.sprite = _placeBackgrounds[podiumIndex];
            _placeBadge.gameObject.SetActive(podium);
            _placeBadge.sprite = podium ? _placeBadges[podiumIndex] : null;
            _place.text = place > 0 ? place.ToString() : "—";
            _place.color = podium ? Color.white : new Color32(35, 54, 69, 255);
            _avatar.SetProfile(item.AvatarUrl, null);
            _nickName.text = string.IsNullOrWhiteSpace(item.NickName) ? "玩家" : item.NickName;
            _rankName.gameObject.SetActive(false);
            _rankIcon.gameObject.SetActive(false);
            _nickName.rectTransform.anchoredPosition = new Vector2(_nickName.rectTransform.anchoredPosition.x, 0f);
            _score.rectTransform.anchoredPosition = new Vector2(_score.rectTransform.anchoredPosition.x, 0f);
            _score.enabled = true;
            _score.text = item.Score.ToString();
        }

        protected override void OnUnbindItem() => _avatar.Clear();
    }
}
