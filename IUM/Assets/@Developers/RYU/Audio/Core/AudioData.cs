using System;

namespace Core.Audio
{
    [Serializable]
    public sealed class AudioManifest
    {
        public AudioRuntimeSettings settings = new();
        public AudioEntryData[] sounds = Array.Empty<AudioEntryData>();
    }

    [Serializable]
    public sealed class AudioEntryData
    {
        public string id;
        public string loadKey;
    }

    [Serializable]
    public sealed class AudioRuntimeSettings
    {
        public int initialPoolSize = 12;
        public int maxSfxChannels = 32;
        public float spatialBlend = 1f;
        public float minDistance = 1f;
        public float maxDistance = 15f;

        /// <summary>
        /// Quest 스페이셜라이저 사용 여부. 구 IUM <c>AudioManager</c>에서 이식했다.
        /// 2D로 재생하는 소리에는 켜도 효과가 없으므로 실제 적용은 spatialBlend와 함께 판단한다.
        /// </summary>
        public bool enableSpatialization = true;

        public float masterVolume = 1f;
        public float sfxVolume = 1f;
        public float bgmVolume = 1f;
        public float dialogueVolume = 1f;
        public float videoVolume = 1f;
        public bool masterMuted;
        public bool sfxMuted;
        public bool bgmMuted;
        public bool dialogueMuted;
        public bool videoMuted;
    }
}
