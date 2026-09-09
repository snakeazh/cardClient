using System;
using Framework.Save;
using UnityEngine;

namespace App.Audio
{
    /// <summary>
    /// 在宿主物体上挂 BGM / SFX 两个 AudioSource，脏标记落盘开关。
    /// </summary>
    public sealed class AudioService : IAudioService
    {
        public const string BgmSaveKey = "audio.bgm.enabled.v1";
        public const string SfxSaveKey = "audio.sfx.enabled.v1";

        private readonly ISaveService _save;
        private readonly AudioSource _bgm;
        private readonly AudioSource _sfx;
        private bool _bgmEnabled = true;
        private bool _sfxEnabled = true;
        private bool _bgmWasPaused;
        private bool _dirty;

        public AudioService(ISaveService save, GameObject host)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            _bgm = CreateSource(host, "Bgm");
            _sfx = CreateSource(host, "Sfx");
        }

        public bool BgmEnabled => _bgmEnabled;

        public bool SfxEnabled => _sfxEnabled;

        public event Action Changed;

        public void SetBgmEnabled(bool enabled)
        {
            if (_bgmEnabled == enabled)
            {
                return;
            }

            _bgmEnabled = enabled;
            _dirty = true;
            ApplyBgmState();
            Changed?.Invoke();
        }

        public void SetSfxEnabled(bool enabled)
        {
            if (_sfxEnabled == enabled)
            {
                return;
            }

            _sfxEnabled = enabled;
            _dirty = true;
            Changed?.Invoke();
        }

        public void PlayBgm(AudioClip clip, bool loop = true)
        {
            if (clip == null)
            {
                StopBgm();
                return;
            }

            if (_bgm.clip == clip)
            {
                _bgm.loop = loop;
                ApplyBgmState();
                return;
            }

            _bgmWasPaused = false;
            _bgm.clip = clip;
            _bgm.loop = loop;
            if (_bgmEnabled)
            {
                _bgm.Play();
            }
            else
            {
                _bgm.Stop();
            }
        }

        public void StopBgm()
        {
            _bgmWasPaused = false;
            _bgm.Stop();
            _bgm.clip = null;
        }

        public void PlaySfx(AudioClip clip, float volumeScale = 1f)
        {
            if (!_sfxEnabled || clip == null)
            {
                return;
            }

            _sfx.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        public void Load()
        {
            _bgmEnabled = _save.GetInt(BgmSaveKey, 1) != 0;
            _sfxEnabled = _save.GetInt(SfxSaveKey, 1) != 0;
            _dirty = false;
            ApplyBgmState();
            Changed?.Invoke();
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            _save.SetInt(BgmSaveKey, _bgmEnabled ? 1 : 0);
            _save.SetInt(SfxSaveKey, _sfxEnabled ? 1 : 0);
            _save.Save();
            _dirty = false;
        }

        private void ApplyBgmState()
        {
            if (_bgm.clip == null)
            {
                return;
            }

            if (_bgmEnabled)
            {
                if (_bgmWasPaused)
                {
                    _bgm.UnPause();
                    _bgmWasPaused = false;
                }
                else if (!_bgm.isPlaying)
                {
                    _bgm.Play();
                }
            }
            else if (_bgm.isPlaying)
            {
                _bgm.Pause();
                _bgmWasPaused = true;
            }
        }

        private static AudioSource CreateSource(GameObject host, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(host.transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = false;
            return source;
        }
    }
}
