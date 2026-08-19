using UnityEngine;
using CoreAudioBus = Core.Audio.AudioBus;
using CoreAudioManager = Core.Audio.AudioManager;

/// <summary>
/// 버스 볼륨을 받아 오는 한 지점 (ISSUE-015). 자기 <see cref="AudioSource"/>로 재생하면서 버스
/// 감쇠만 빌려 쓰는 쪽들이 공유한다 — 대사, 이음이 음성, 영상 컷씬이 그렇다.
///
/// 셋 다 <c>Core.Audio.AudioManager</c>의 재생 경로를 타지 않는다. 매니저는 문자열 ID로 사전
/// 등록된 클립만 재생하는데, 이들의 클립은 TTS 합성물이거나 <c>VideoPlayer</c>가 들고 있는 것이라
/// 등록할 대상이 없다. 그래서 볼륨만 계산해 간다.
///
/// 폴백을 여기 모아 둔 이유는 세 곳이 같은 실수를 하지 않게 하기 위해서다. 버스가 없다고 소리를
/// 잃으면 안 되고, 그렇다고 설정을 무시하고 최대 음량으로 내보내서도 안 된다.
/// </summary>
public static class AudioBusVolume
{
    /// <summary>
    /// 버스를 지난 최종 볼륨. 버스가 없으면 저장된 설정값으로 대신한다 — 이때는 마스터 볼륨과
    /// 뮤트가 걸리지 않는다. <c>CoreAudioBootstrap</c>이 모든 씬에 버스를 세우므로 정상 경로는
    /// 아니다.
    /// </summary>
    public static float Resolve(CoreAudioBus bus, float baseVolume = 1f)
    {
        if (CoreAudioManager.TryGetInstance(out var audio) && audio != null)
            return audio.Mixer.Calculate(bus, baseVolume);

        return Mathf.Clamp01(baseVolume * Fallback(bus));
    }

    static float Fallback(CoreAudioBus bus)
    {
        // TryGetInstance는 필드를 그대로 보므로 종료 중에도 일관되고, 없을 때 새로 만들지 않는다.
        if (!DataManager.TryGetInstance(out var data) || !data.IsReady) return 1f;

        var settings = data.Settings;
        if (settings == null) return 1f;

        return bus switch
        {
            CoreAudioBus.Dialogue => settings.DialogueVolume,
            CoreAudioBus.Video => settings.VideoVolume,
            CoreAudioBus.Bgm => settings.MusicVolume,
            CoreAudioBus.Sfx => settings.EnvironmentVolume,
            _ => 1f
        };
    }
}
