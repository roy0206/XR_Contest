using System;
using UnityEngine;
using CoreAudioBus = Core.Audio.AudioBus;
using CoreAudioManager = Core.Audio.AudioManager;

/// <summary>
/// 대사 음성 재생을 담당하는 모듈 (ISSUE-002). <c>Core.Audio.AudioManager</c>의 DIALOGUE 버스를
/// 소비하지만 재생은 자기 <see cref="AudioSource"/>로 한다.
///
/// 매니저에 태우지 않고 자기 소스를 드는 이유는 클립의 출처 때문이다. 매니저는 문자열 ID로
/// 사전 로드된 클립만 재생하는데, 대사 음성은 Addressables 녹음이 실패하면 TTS로 합성된
/// 런타임 클립이라 ID로 미리 등록할 대상이 없다. 대사 사정을 범용 매니저에 밀어 넣는 대신
/// 버스만 빌려 쓴다.
///
/// 클립 소유권은 갖지 않는다. Addressables 핸들 해제와 합성 클립 <c>Destroy</c>는
/// <c>DialogueVoiceLibrary</c>가 추적하므로, 여기서 <c>Destroy</c>하면 이중 해제가 된다.
/// </summary>
public sealed class DialogueAudioModule : Module
{
    readonly CoreAudioManager _audio;
    readonly AudioSource _source;

    float _baseVolume = 1f;
    bool _subscribed;

    public DialogueAudioModule(MonoThing thing, CoreAudioManager audio) : base(thing)
    {
        _audio = audio;

        var host = new GameObject("DialogueVoice");
        host.transform.SetParent(thing.transform, false);
        _source = host.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;

        // 대사는 화면 밖 위치가 아니라 플레이어에게 직접 들려주는 소리다.
        _source.spatialBlend = 0f;
    }

    /// <summary>재생 중이며 아직 끝나지 않았다.</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>재생이 끝났거나 <see cref="Stop"/>으로 중단됐을 때. 메인 스레드에서 발생한다.</summary>
    public event Action Finished;

    public override void OnAdded()
    {
        if (_audio == null || _subscribed) return;
        _audio.Mixer.Changed += ApplyVolume;
        _subscribed = true;
    }

    public override void OnRemoved()
    {
        if (_subscribed && _audio != null)
        {
            _audio.Mixer.Changed -= ApplyVolume;
            _subscribed = false;
        }

        Stop(false);
        if (_source != null) UnityEngine.Object.Destroy(_source.gameObject);
    }

    /// <param name="clip">null이면 아무것도 하지 않는다. 음성 없는 대사는 오류가 아니다.</param>
    /// <param name="volume">이 대사 한 줄의 기준 볼륨. 버스와 마스터가 여기에 곱해진다.</param>
    public void Play(AudioClip clip, float volume = 1f)
    {
        Stop(false);
        if (clip == null) return;

        _baseVolume = Mathf.Clamp01(volume);
        _source.clip = clip;
        ApplyVolume();
        _source.Play();
        IsSpeaking = true;
    }

    public void Stop(bool raiseFinished = true)
    {
        var wasSpeaking = IsSpeaking;
        IsSpeaking = false;

        if (_source != null)
        {
            _source.Stop();
            _source.clip = null;
        }

        if (wasSpeaking && raiseFinished) Finished?.Invoke();
    }

    public override void OnUpdate()
    {
        if (!IsSpeaking || _source == null) return;

        // 일시정지 중에는 판정하지 않는다. AudioListener.pause가 소스를 멈춘 상태이므로
        // isPlaying만 보고 끝났다고 단정하면 메뉴를 여는 순간 대사가 종료된 것으로 처리된다.
        if (PauseService.IsPaused) return;

        if (_source.isPlaying) return;
        Stop();
    }

    void ApplyVolume()
    {
        if (_source == null) return;

        _source.volume = _audio != null
            ? _audio.Mixer.Calculate(CoreAudioBus.Dialogue, _baseVolume)
            : _baseVolume;
    }
}
