using System;
using UnityEngine;

/// <summary>
/// Plays 이음이's answer and owns the subtitle timing. When TTS produced no clip (mock or a
/// failed synthesis) the subtitle is still held for the estimated speaking time, so the
/// 답변 중 state lasts the same as it would with real audio.
/// </summary>
public sealed class AiVoicePlayer
{
    readonly AudioSource _source;
    readonly AiConversationConfig _config;

    AudioClip _clip;
    float _endTime;

    public AiVoicePlayer(Transform parent, AiConversationConfig config)
    {
        _config = config ?? new AiConversationConfig();

        var host = new GameObject("IeumiVoice");
        host.transform.SetParent(parent, false);
        _source = host.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 0f; // 이음이 speaks to the player, not from a world position
        ApplyVolume();
    }

    public bool IsSpeaking { get; private set; }

    /// <summary>Raised on the main thread when the answer finished playing or was stopped.</summary>
    public event Action Finished;

    /// <summary>
    /// 이음이 음성도 대사이므로 DIALOGUE 버스를 지난다 (ISSUE-015). 노장 대사와 같은 슬라이더가
    /// 걸리고, 마스터 볼륨과 뮤트도 함께 적용된다.
    /// </summary>
    public void ApplyVolume() => _source.volume = AudioBusVolume.Resolve(Core.Audio.AudioBus.Dialogue);

    public void Play(string text, AudioClip clip)
    {
        Stop(false);
        ApplyVolume();

        var duration = clip != null
            ? clip.length
            : EstimateSeconds(text);

        _clip = clip;
        if (clip != null)
        {
            _source.clip = clip;
            _source.Play();
        }

        IsSpeaking = true;

        // PauseService.Now는 일시정지 구간을 뺀 시각이다 (ISSUE-008). Time.unscaledTime을 쓰면
        // 메뉴를 여는 동안에도 시계가 흘러, AudioListener.pause로 멈춰 선 음성보다 자막이 먼저
        // 끝난다.
        _endTime = PauseService.Now + duration + _config.SubtitleTailSeconds;
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

        // Clips are created per answer by the decoder, so they must be released explicitly.
        if (_clip != null)
        {
            UnityEngine.Object.Destroy(_clip);
            _clip = null;
        }

        if (wasSpeaking && raiseFinished) Finished?.Invoke();
    }

    public void Tick()
    {
        if (!IsSpeaking) return;
        if (PauseService.Now < _endTime) return;
        Stop();
    }

    public void Dispose()
    {
        Stop(false);
        if (_source != null) UnityEngine.Object.Destroy(_source.gameObject);
    }

    float EstimateSeconds(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0f : text.Length / Mathf.Max(1f, _config.SpeechCharsPerSecond);
}
