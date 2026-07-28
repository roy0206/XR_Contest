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

    /// <summary>Uses the stored 대사 볼륨. The mixer has no dialogue channel yet (DataSystem 4.2).</summary>
    public void ApplyVolume()
    {
        var volume = 1f;
        if (DataManager.HasInstance && DataManager.Instance.IsReady)
            volume = DataManager.Instance.Settings.DialogueVolume;
        _source.volume = Mathf.Clamp01(volume);
    }

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
        _endTime = Time.unscaledTime + duration + _config.SubtitleTailSeconds;
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
        if (Time.unscaledTime < _endTime) return;
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
