using System;
using Framework.Save;
using UnityEngine;

namespace App.Audio
{
    /// <summary>
    /// 全局音频。BGM 循环播放、音效 one-shot；各自独立开关，落盘 audio.*.v1。
    /// </summary>
    public interface IAudioService : ISaveFlushable
    {
        bool BgmEnabled { get; }

        bool SfxEnabled { get; }

        event Action Changed;

        void SetBgmEnabled(bool enabled);

        void SetSfxEnabled(bool enabled);

        void PlayBgm(AudioClip clip, bool loop = true);

        void StopBgm();

        void PlaySfx(AudioClip clip, float volumeScale = 1f);

        void Load();
    }
}
