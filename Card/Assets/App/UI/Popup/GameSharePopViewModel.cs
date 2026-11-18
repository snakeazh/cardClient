using System;
using App.Ad;
using App.Game;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    public sealed class GameSharePopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly GameSession _session;
        private readonly IAdService _ad = AdServiceFactory.Create();
        private bool _watching;

        public GameSharePopViewModel(IUIManager ui, GameSession session)
        {
            _ui = ui;
            _session = session;
            WatchCommand = new RelayCommand(Watch, () => !_watching);
            CloseCommand = new RelayCommand(Close, () => !_watching);
        }

        public int SkillId { get; private set; }
        public IRelayCommand WatchCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public void Setup(int skillId) => SkillId = skillId;

        private async void Watch()
        {
            if (_watching || !_session.CanWatchPveSkillAd(SkillId)) return;
            _watching = true;
            WatchCommand.RaiseCanExecuteChanged();
            CloseCommand.RaiseCanExecuteChanged();
            try
            {
                var result = await _ad.ShowRewardedVideoAsync();
                if (!IsOpen) return;
                if (!result.Rewarded)
                {
                    Toast.Show(result.Status == AdWatchStatus.ClosedEarly ? "完整观看广告后获得次数" : "广告暂不可用，请稍后重试");
                    return;
                }
                if (_session.GrantPveSkillAd(SkillId))
                {
                    Toast.Show("技能次数已补满");
                    await _ui.Close(this);
                }
            }
            catch (Exception)
            {
                if (IsOpen) Toast.Show("广告播放失败，请稍后重试");
            }
            finally
            {
                _watching = false;
                WatchCommand.RaiseCanExecuteChanged();
                CloseCommand.RaiseCanExecuteChanged();
            }
        }

        private void Close() => _ = _ui.Close(this);
    }
}
