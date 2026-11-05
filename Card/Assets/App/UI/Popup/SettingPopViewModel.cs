using App.Audio;
using App.Net;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 设置弹窗：音乐/音效开关（读写 IAudioService，AudioService 变更即落盘），顶部显示用户 Id。
    /// </summary>
    public sealed class SettingPopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IAudioService _audio;

        public SettingPopViewModel(IUIManager ui, IAudioService audio, GameApiClient api)
        {
            _ui = ui;
            _audio = audio;
            MusicOn = new ObservableProperty<bool>(_audio.BgmEnabled);
            SoundOn = new ObservableProperty<bool>(_audio.SfxEnabled);
            UserId = new ObservableProperty<string>(api.UserId);
            ToggleMusicCommand = new RelayCommand(ToggleMusic);
            ToggleSoundCommand = new RelayCommand(ToggleSound);
            CloseCommand = new RelayCommand(ClosePop);
        }

        /// <summary>音乐（BGM）开关，勾=开。</summary>
        public ObservableProperty<bool> MusicOn { get; }

        /// <summary>音效开关，勾=开。</summary>
        public ObservableProperty<bool> SoundOn { get; }

        /// <summary>当前登录的用户 Id；未连服务器时为空串。</summary>
        public ObservableProperty<string> UserId { get; }

        public IRelayCommand ToggleMusicCommand { get; }

        public IRelayCommand ToggleSoundCommand { get; }

        public IRelayCommand CloseCommand { get; }

        private void ToggleMusic()
        {
            var enabled = !_audio.BgmEnabled;
            _audio.SetBgmEnabled(enabled);
            MusicOn.Value = enabled;
        }

        private void ToggleSound()
        {
            var enabled = !_audio.SfxEnabled;
            _audio.SetSfxEnabled(enabled);
            SoundOn.Value = enabled;
        }

        private void ClosePop()
        {
            _ = _ui.Close(this);
        }
    }
}
