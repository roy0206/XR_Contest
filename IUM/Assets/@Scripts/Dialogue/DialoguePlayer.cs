using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Plays one sequence at a time and owns subtitle timing. Shared by in-game 노장 대사 and by
/// cutscenes; the difference between them lives in the callers, not here.
///
/// A line always lasts a real amount of time even with no audio, so a flow authored before any
/// voice work still plays back at the pace it will ship at.
/// </summary>
public sealed class DialoguePlayer
{
    enum PlaybackState
    {
        Idle,

        /// <summary>Clips for the whole sequence are being fetched before the first line starts.</summary>
        Loading,
        Speaking,

        /// <summary>Silence between lines. The subtitle stays up so it does not flicker.</summary>
        Gap
    }

    readonly AudioSource _source;
    readonly IDialogueVoiceSource _voices;

    DialogueSettings _settings;
    DialogueSequence _sequence;
    AudioClip[] _clips;
    CancellationTokenSource _cancellation;
    PlaybackState _state = PlaybackState.Idle;
    int _lineIndex = -1;
    float _stateEndTime;

    public DialoguePlayer(Transform parent, DialogueSettings settings, IDialogueVoiceSource voices)
    {
        _settings = settings ?? new DialogueSettings();
        _voices = voices ?? throw new ArgumentNullException(nameof(voices));

        var host = new GameObject("DialogueVoice");
        host.transform.SetParent(parent, false);
        _source = host.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;

        // 노장은 모델 없이 화면 고정 UI로 표현하므로 화자가 공간상의 위치를 갖지 않는다.
        // 월드 공간 패널로 바꾸게 되면 화자별 앵커와 spatialBlend를 여기에 되돌린다.
        _source.spatialBlend = 0f;
        ApplyVolume();
    }

    public bool IsPlaying => _state != PlaybackState.Idle;
    public DialogueSequence CurrentSequence => _sequence;
    public DialogueLine CurrentLine => GetLine(_lineIndex);
    public string CurrentText { get; private set; }
    public DialogueSpeaker CurrentSpeaker { get; private set; }

    /// <summary>Raised when a line's audio and subtitle begin. The hook animation and lipsync use.</summary>
    public event Action<DialogueLine> LineStarted;

    public event Action<DialogueLine> LineFinished;

    /// <summary>Empty text means "hide the subtitle".</summary>
    public event Action<DialogueSpeaker, string> SubtitleChanged;

    /// <summary>Second argument is false when the sequence was cut instead of finishing.</summary>
    public event Action<DialogueSequence, bool> SequenceFinished;

    public void UpdateSettings(DialogueSettings settings)
    {
        if (settings == null) return;
        _settings = settings;
    }

    /// <summary>Starts <paramref name="sequence"/>, cutting whatever was playing.</summary>
    public void Play(DialogueSequence sequence)
    {
        Stop();

        if (sequence == null || !sequence.HasLines)
        {
            SequenceFinished?.Invoke(sequence, true);
            return;
        }

        _sequence = sequence;
        _clips = new AudioClip[sequence.Lines.Count];
        _lineIndex = -1;
        _state = PlaybackState.Loading;

        _cancellation = new CancellationTokenSource();
        _ = PreloadAsync(sequence, _cancellation.Token);
    }

    /// <summary>Cuts the current sequence. Always reports an incomplete finish.</summary>
    public void Stop()
    {
        if (_state == PlaybackState.Idle) return;

        var cut = _sequence;
        Cleanup();
        SubtitleChanged?.Invoke(CurrentSpeaker, string.Empty);
        SequenceFinished?.Invoke(cut, false);
    }

    /// <summary>
    /// Drives line progression on <see cref="PauseService.Now"/> rather than any Unity clock.
    /// timeScale cannot be used because an AudioSource ignores it, and unscaledTime cannot be used
    /// because it keeps running while 일시정지 holds the voice still — the subtitle would race
    /// ahead of the audio across a pause. PauseService.Now simply stops.
    /// </summary>
    public void Tick()
    {
        if (_state is PlaybackState.Idle or PlaybackState.Loading) return;
        if (PauseService.Now < _stateEndTime) return;

        if (_state == PlaybackState.Speaking)
        {
            LineFinished?.Invoke(CurrentLine);

            if (_lineIndex >= _sequence.Lines.Count - 1)
            {
                FinishSequence();
                return;
            }

            _state = PlaybackState.Gap;
            _stateEndTime = PauseService.Now + _settings.LineGapSeconds;
            return;
        }

        StartLine(_lineIndex + 1);
    }

    /// <summary>
    /// Applies 대사 볼륨 through the DIALOGUE bus (ISSUE-015). 마스터 볼륨과 뮤트가 함께 걸린다.
    ///
    /// 버스가 없으면 저장된 값을 직접 읽는다. <c>CoreAudioBootstrap</c>이 모든 씬에 버스를 세우므로
    /// 정상 경로는 아니지만, 볼륨 하나 때문에 대사가 사라지는 것보다는 낫다.
    /// </summary>
    public void ApplyVolume() => _source.volume = AudioBusVolume.Resolve(Core.Audio.AudioBus.Dialogue);

    public void Dispose()
    {
        Cleanup();
        if (_source != null) UnityEngine.Object.Destroy(_source.gameObject);
    }

    async Task PreloadAsync(DialogueSequence sequence, CancellationToken cancellationToken)
    {
        try
        {
            // Loading the whole sequence up front trades a short delay before the first line for
            // no gaps between lines, which matters more for a scripted performance.
            for (var i = 0; i < sequence.Lines.Count; i++)
            {
                var clip = await _voices.LoadAsync(sequence.Lines[i], cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _clips[i] = clip;
            }

            StartLine(0);
        }
        catch (OperationCanceledException)
        {
            // Stop already cleaned up; releasing here would double-free.
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Dialogue] '{sequence.Id}' 준비 실패: {exception.Message}");
            if (!cancellationToken.IsCancellationRequested) StartLine(0);
        }
    }

    void StartLine(int index)
    {
        var line = GetLine(index);
        if (line == null)
        {
            FinishSequence();
            return;
        }

        _lineIndex = index;
        _state = PlaybackState.Speaking;

        var clip = _clips != null && index < _clips.Length ? _clips[index] : null;
        ApplyVolume();

        _source.Stop();
        _source.clip = clip;
        if (clip != null) _source.Play();

        CurrentSpeaker = line.Speaker ?? _sequence.Speaker;
        CurrentText = line.Text ?? string.Empty;

        _stateEndTime = PauseService.Now + MeasureLine(line, clip);

        SubtitleChanged?.Invoke(CurrentSpeaker, CurrentText);
        LineStarted?.Invoke(line);
    }

    float MeasureLine(DialogueLine line, AudioClip clip)
    {
        float duration;
        if (line.DurationOverride > 0f) duration = line.DurationOverride;
        else if (clip != null) duration = clip.length;
        else duration = Mathf.Max(_settings.MinLineSeconds, EstimateSeconds(line.Text));

        return duration + Mathf.Max(0f, line.HoldSeconds) + _settings.SubtitleTailSeconds;
    }

    float EstimateSeconds(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0f : text.Length / _settings.SpeechCharsPerSecond;

    void FinishSequence()
    {
        var finished = _sequence;
        Cleanup();
        SubtitleChanged?.Invoke(CurrentSpeaker, string.Empty);
        SequenceFinished?.Invoke(finished, true);
    }

    void Cleanup()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;

        if (_source != null)
        {
            _source.Stop();
            _source.clip = null;
        }

        if (_clips != null)
        {
            for (var i = 0; i < _clips.Length; i++)
                _voices.Release(_clips[i]);
            _clips = null;
        }

        _sequence = null;
        _lineIndex = -1;
        _state = PlaybackState.Idle;
        _stateEndTime = 0f;
        CurrentText = string.Empty;
    }

    DialogueLine GetLine(int index) =>
        _sequence != null && index >= 0 && index < _sequence.Lines.Count ? _sequence.Lines[index] : null;
}
