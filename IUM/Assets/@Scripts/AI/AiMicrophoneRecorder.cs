using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Push-to-talk capture. Recording is bounded by <see cref="AiConversationConfig.MaxRecordSeconds"/>
/// so a stuck button cannot record forever, and the captured rate is reported as-is instead of
/// being resampled: the STT request carries whatever the device produced.
/// </summary>
public sealed class AiMicrophoneRecorder
{
    const int LevelWindowSamples = 512;

    readonly AiConversationConfig _config;
    readonly float[] _levelBuffer = new float[LevelWindowSamples];

    AudioClip _clip;
    string _device;
    float _startTime;

    public AiMicrophoneRecorder(AiConversationConfig config) => _config = config ?? new AiConversationConfig();

    public bool IsRecording { get; private set; }

    /// <summary>0..1 peak of the most recent window, for the listening indicator.</summary>
    public float CurrentLevel { get; private set; }

    public float RecordedSeconds => IsRecording ? Time.unscaledTime - _startTime : 0f;

    public static bool HasDevice => Microphone.devices != null && Microphone.devices.Length > 0;

    public static bool HasPermission
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }
    }

    /// <summary>Fire-and-forget permission prompt. The next PTT press succeeds once granted.</summary>
    public static void RequestPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            Permission.RequestUserPermission(Permission.Microphone);
#endif
    }

    public bool TryStart(out string error)
    {
        error = null;
        if (IsRecording) return true;

        if (!HasPermission)
        {
            RequestPermission();
            error = "마이크 권한이 없습니다.";
            return false;
        }

        if (!HasDevice)
        {
            error = "사용 가능한 마이크 장치가 없습니다.";
            return false;
        }

        _device = Microphone.devices[0];
        var sampleRate = ResolveSampleRate(_device);
        var maxSeconds = Mathf.CeilToInt(_config.MaxRecordSeconds);

        _clip = Microphone.Start(_device, false, maxSeconds, sampleRate);
        if (_clip == null)
        {
            error = $"마이크 '{_device}' 를 시작하지 못했습니다.";
            _device = null;
            return false;
        }

        IsRecording = true;
        CurrentLevel = 0f;
        _startTime = Time.unscaledTime;
        return true;
    }

    /// <summary>Call every frame while recording; refreshes the level meter.</summary>
    public void Tick()
    {
        if (!IsRecording || _clip == null) return;

        var position = Microphone.GetPosition(_device);
        if (position < LevelWindowSamples)
        {
            CurrentLevel = 0f;
            return;
        }

        if (!_clip.GetData(_levelBuffer, position - LevelWindowSamples))
            return;

        var peak = 0f;
        for (var i = 0; i < _levelBuffer.Length; i++)
        {
            var value = Mathf.Abs(_levelBuffer[i]);
            if (value > peak) peak = value;
        }

        // Smoothed so the indicator does not flicker on consonants.
        CurrentLevel = Mathf.Lerp(CurrentLevel, Mathf.Clamp01(peak * 4f), 0.35f);
    }

    /// <summary>Stops capture and returns only the part that was actually recorded.</summary>
    public AiAudioSample Stop()
    {
        if (!IsRecording || _clip == null)
        {
            Cancel();
            return null;
        }

        var position = Microphone.GetPosition(_device);
        var channels = _clip.channels;
        var frequency = _clip.frequency;
        var totalSamples = _clip.samples;

        Microphone.End(_device);

        // Position is in frames; a full-length recording wraps back to 0.
        var frames = position <= 0 ? totalSamples : Mathf.Min(position, totalSamples);
        var samples = new float[frames * channels];
        if (frames > 0) _clip.GetData(samples, 0);

        ReleaseClip();
        return new AiAudioSample(samples, frequency, channels);
    }

    public void Cancel()
    {
        if (IsRecording && !string.IsNullOrEmpty(_device))
            Microphone.End(_device);
        ReleaseClip();
    }

    void ReleaseClip()
    {
        if (_clip != null) Object.Destroy(_clip);
        _clip = null;
        _device = null;
        IsRecording = false;
        CurrentLevel = 0f;
    }

    static int ResolveSampleRate(string device)
    {
        Microphone.GetDeviceCaps(device, out var minFrequency, out var maxFrequency);
        // (0, 0) means the device accepts any frequency.
        if (minFrequency == 0 && maxFrequency == 0) return 16000;
        return Mathf.Clamp(16000, minFrequency, maxFrequency);
    }
}
