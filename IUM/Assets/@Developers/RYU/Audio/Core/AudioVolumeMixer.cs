using System;
using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// 버스별 볼륨과 뮤트를 들고 최종 출력 볼륨을 계산한다. Unity AudioMixer를 쓰지 않으므로
    /// 필터나 DSP는 걸 수 없고, 버스 단위 감쇠까지만 가능하다.
    /// </summary>
    public sealed class AudioVolumeMixer
    {
        private float masterVolume = 1f;
        private float sfxVolume = 1f;
        private float bgmVolume = 1f;
        private float dialogueVolume = 1f;
        private bool masterMuted;
        private bool sfxMuted;
        private bool bgmMuted;
        private bool dialogueMuted;

        public event Action Changed;

        public float MasterVolume
        {
            get => masterVolume;
            set => SetVolume(ref masterVolume, value);
        }

        public float SfxVolume
        {
            get => sfxVolume;
            set => SetVolume(ref sfxVolume, value);
        }

        public float BgmVolume
        {
            get => bgmVolume;
            set => SetVolume(ref bgmVolume, value);
        }

        public float DialogueVolume
        {
            get => dialogueVolume;
            set => SetVolume(ref dialogueVolume, value);
        }

        public bool MasterMuted
        {
            get => masterMuted;
            set => SetMuted(ref masterMuted, value);
        }

        public bool SfxMuted
        {
            get => sfxMuted;
            set => SetMuted(ref sfxMuted, value);
        }

        public bool BgmMuted
        {
            get => bgmMuted;
            set => SetMuted(ref bgmMuted, value);
        }

        public bool DialogueMuted
        {
            get => dialogueMuted;
            set => SetMuted(ref dialogueMuted, value);
        }

        public float Calculate(AudioBus bus, float baseVolume)
        {
            if (masterMuted || IsMuted(bus))
                return 0f;

            return Mathf.Clamp01(baseVolume) * masterVolume * GetBusVolume(bus);
        }

        public void Apply(AudioRuntimeSettings settings)
        {
            if (settings == null)
                return;

            masterVolume = Mathf.Clamp01(settings.masterVolume);
            sfxVolume = Mathf.Clamp01(settings.sfxVolume);
            bgmVolume = Mathf.Clamp01(settings.bgmVolume);
            dialogueVolume = Mathf.Clamp01(settings.dialogueVolume);
            masterMuted = settings.masterMuted;
            sfxMuted = settings.sfxMuted;
            bgmMuted = settings.bgmMuted;
            dialogueMuted = settings.dialogueMuted;
            Changed?.Invoke();
        }

        // 버스가 셋으로 늘면서 삼항 연산자로는 표현이 안 된다. 새 버스를 추가할 때 여기 두 곳만
        // 채우면 되도록 분리해 두었다. default는 던지지 않고 통과시킨다. 알 수 없는 버스 때문에
        // 소리가 사라지는 것보다 마스터 볼륨으로라도 나오는 편이 낫다.
        private bool IsMuted(AudioBus bus) => bus switch
        {
            AudioBus.Sfx => sfxMuted,
            AudioBus.Bgm => bgmMuted,
            AudioBus.Dialogue => dialogueMuted,
            _ => false
        };

        private float GetBusVolume(AudioBus bus) => bus switch
        {
            AudioBus.Sfx => sfxVolume,
            AudioBus.Bgm => bgmVolume,
            AudioBus.Dialogue => dialogueVolume,
            _ => 1f
        };

        private void SetVolume(ref float target, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(target, value))
                return;

            target = value;
            Changed?.Invoke();
        }

        private void SetMuted(ref bool target, bool value)
        {
            if (target == value)
                return;

            target = value;
            Changed?.Invoke();
        }
    }
}
