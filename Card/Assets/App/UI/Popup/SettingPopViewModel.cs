using App.Audio;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 设置弹窗：音乐/音效开关（读写 IAudioService，AudioService 变更即落盘）。
    /// </summary>
    public sealed class SettingPopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IAudioService _audio;

        public SettingPopViewModel(IUIManager ui, IAudioService audio)
        {
            _ui = ui;
            _audio = audio;
            MusicOn = new ObservableProperty<bool>(_audio.BgmEnabled);
            SoundOn = new ObservableProperty<bool>(_audio.SfxEnabled);
            ToggleMusicCommand = new RelayCommand(ToggleMusic);
            ToggleSoundCommand = new RelayCommand(ToggleSound);
            CloseCommand = new RelayCommand(ClosePop);
        }

        /// <summary>音乐（BGM）开关，勾=开。</summary>
        public ObservableProperty<bool> MusicOn { get; }

        /// <summary>音效开关，勾=开。</summary>
        public ObservableProperty<bool> SoundOn { get; }

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
