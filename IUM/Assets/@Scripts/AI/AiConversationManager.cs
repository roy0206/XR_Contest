using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>PTT 상태 표시 (F-013 3.3). The HUD renders one row of that table per value.</summary>
public enum AiConversationState
{
    /// <summary>사용 불가 — services still loading, or 노장 대사 재생 중 (F-011 1.4).</summary>
    Unavailable,

    /// <summary>질문 가능.</summary>
    Idle,

    /// <summary>듣는 중.</summary>
    Listening,

    /// <summary>변환 중.</summary>
    Transcribing,

    /// <summary>생각 중.</summary>
    Thinking,

    /// <summary>답변 중.</summary>
    Speaking
}

/// <summary>
/// 이음이 음성 대화 파이프라인: PTT → STT → LLM → 안전성 검사 → TTS, with fixed lines for every
/// failure path and a prepared-answer mode when the device is offline.
///
/// Gameplay never talks to a provider directly. Input arrives as a PTT press from
/// <see cref="VoiceInputModule"/>, and the current process arrives through
/// <see cref="AiProcessContextRegistry"/>, so nothing here depends on XR or on 3~6단계 systems.
/// </summary>
public sealed class AiConversationManager : Singleton<AiConversationManager>, ISceneEventListener
{
    const int MaxSuggestions = 4;
    const int MaxHistoryMessages = 12;

    [Header("Debug")]
    [Tooltip("Logs every state change. Request logging is controlled by ai_config.json.")]
    [SerializeField] bool logStateChanges;

    readonly List<AiChatMessage> _history = new();
    readonly List<AiSuggestedQuestion> _suggestions = new();

    AiRuntimeSettings _settings;
    AiServiceSet _services;
    AiPromptBuilder _promptBuilder;
    AiSafetyFilter _safety;
    AiMicrophoneRecorder _recorder;
    AiVoicePlayer _voice;

    Task _initialization;
    CancellationTokenSource _pipelineTokenSource;
    TaskCompletionSource<bool> _speechCompletion;

    int _recognitionFailures;
    int _offTopicCount;
    int _networkFailures;
    float _offlineUntil;
    float _listenStartTime;
    bool _listeningWithoutMicrophone;
    bool _warnedAboutMicrophone;
    bool _inputLocked;

    public AiConversationState State { get; private set; } = AiConversationState.Unavailable;
    public bool IsReady { get; private set; }

    /// <summary>Last recognized question, shown while 변환 중 completes.</summary>
    public string Transcript { get; private set; }

    /// <summary>Text currently being spoken, or null when nothing is on screen.</summary>
    public string Subtitle { get; private set; }

    /// <summary>0..1 microphone level for the listening indicator.</summary>
    public float MicrophoneLevel { get; private set; }

    public bool IsInputLocked => _inputLocked;

    /// <summary>
    /// Set while a desktop text field has focus. Typing a question would otherwise also press
    /// the PTT key, because the input layer reads the keyboard directly.
    /// </summary>
    public bool SuppressPushToTalk { get; set; }
    public bool UsesMockServices => _services != null && _services.UsesAnyMock;
    public string ServiceSummary => _services?.Describe() ?? "초기화 중";

    /// <summary>True while the pipeline is answering from prepared lines instead of the network.</summary>
    public bool IsOffline => !AiHttp.IsOnline || Time.unscaledTime < _offlineUntil;

    public IReadOnlyList<AiSuggestedQuestion> Suggestions => _suggestions;

    public event Action<AiConversationState> StateChanged;
    public event Action<string> TranscriptChanged;
    public event Action<string> SubtitleChanged;
    public event Action SuggestionsChanged;
    public event Action Ready;

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;

        SceneController.Instance?.RegisterListener(this);
        _ = InitializeAsync();
    }

    protected override void OnDestroy()
    {
        if (SceneController.TryGetInstance(out var controller))
            controller.UnregisterListener(this);

        CancelPipeline();
        _recorder?.Cancel();
        _voice?.Dispose();
        base.OnDestroy();
    }

    void Update()
    {
        if (_recorder != null && _recorder.IsRecording)
        {
            _recorder.Tick();
            MicrophoneLevel = _recorder.CurrentLevel;

            // A stuck button must not record forever (F-013 3.6 length limits).
            if (_recorder.RecordedSeconds >= _settings.Config.Conversation.MaxRecordSeconds)
                EndListening();
        }
        else
        {
            MicrophoneLevel = 0f;
        }

        _voice?.Tick();
    }

    public Task InitializeAsync() => _initialization ??= InitializeInternalAsync();

    async Task InitializeInternalAsync()
    {
        try
        {
            _settings = await AiConfigLoader.LoadAsync(new AiLocalTextFileSource());
            _services = AiServiceFactory.Create(_settings);
            _promptBuilder = new AiPromptBuilder(_settings.Config, _settings.Knowledge);
            _safety = new AiSafetyFilter(_settings.Config);
            _recorder = new AiMicrophoneRecorder(_settings.Config.Conversation);
            _voice = new AiVoicePlayer(transform, _settings.Config.Conversation);
            _voice.Finished += OnSpeechFinished;

            IsReady = true;
            SetState(_inputLocked ? AiConversationState.Unavailable : AiConversationState.Idle);
            Ready?.Invoke();
        }
        catch (Exception exception)
        {
            // The pipeline is optional content: a failed init must not block the process.
            Debug.LogError($"[AI] Initialization failed, 이음이 stays unavailable: {exception.Message}");
            IsReady = false;
            SetState(AiConversationState.Unavailable);
        }
    }

    /// <summary>노장 대사 재생 중에는 이음이 PTT 입력을 잠근다 (F-011 1.4).</summary>
    public void SetInputLocked(bool locked)
    {
        if (_inputLocked == locked) return;
        _inputLocked = locked;

        if (locked)
        {
            CancelPipeline();
            _recorder?.Cancel();
            _voice?.Stop(false);
            SetSubtitle(null);
            HideSuggestions();
            SetState(AiConversationState.Unavailable);
            return;
        }

        SetState(IsReady ? AiConversationState.Idle : AiConversationState.Unavailable);
    }

    /// <summary>PTT pressed.</summary>
    public void BeginListening()
    {
        if (SuppressPushToTalk) return;

        if (!IsReady)
        {
            Debug.LogWarning("[AI] PTT was pressed before 이음이 finished initializing.");
            return;
        }

        if (_inputLocked)
        {
            ShowNotice(_settings.Dialogue.InputLocked, false);
            return;
        }

        if (State == AiConversationState.Listening) return;

        // Barge-in: a new question cancels the previous answer instead of queueing behind it.
        CancelPipeline();
        _voice?.Stop(false);
        SetSubtitle(null);

        _listeningWithoutMicrophone = false;
        if (!_recorder.TryStart(out var error))
        {
            // Mock STT does not need audio, so development stays possible without a microphone.
            if (_services.Stt.IsMock)
            {
                _listeningWithoutMicrophone = true;
                if (!_warnedAboutMicrophone)
                {
                    _warnedAboutMicrophone = true;
                    Debug.LogWarning($"[AI] {error} Mock 입력으로 진행합니다. " +
                                     "녹음 대신 PTT를 누른 시간으로 발화 길이를 판정합니다.");
                }
            }
            else
            {
                Debug.LogWarning($"[AI] Microphone unavailable: {error}");
                ShowNotice(_settings.Dialogue.MicrophoneUnavailable, true);
                return;
            }
        }

        SetTranscript(null);
        _listenStartTime = Time.unscaledTime;
        SetState(AiConversationState.Listening);
    }

    /// <summary>PTT released.</summary>
    public void EndListening()
    {
        if (State != AiConversationState.Listening) return;

        var heldSeconds = Time.unscaledTime - _listenStartTime;
        var audio = _listeningWithoutMicrophone ? null : _recorder.Stop();
        _listeningWithoutMicrophone = false;

        // Without a capture device there is no waveform to measure, so the press duration
        // stands in for it. That keeps the mis-press rule testable on a machine with no mic.
        var conversation = _settings.Config.Conversation;
        var tooShort = audio != null
            ? audio.Duration < conversation.MinRecordSeconds || audio.PeakAmplitude < conversation.SilenceThreshold
            : heldSeconds < conversation.MinRecordSeconds;

        if (tooShort)
        {
            StartPipeline(token => HandleShortRecordingAsync(token));
            return;
        }

        StartPipeline(token => TranscribeAndAnswerAsync(audio, token));
    }

    /// <summary>PTT released outside the listening state, or a cancelled question.</summary>
    public void CancelListening()
    {
        if (State != AiConversationState.Listening) return;
        _recorder.Cancel();
        _listeningWithoutMicrophone = false;
        SetState(AiConversationState.Idle);
    }

    /// <summary>Asks a typed or selected question, skipping capture and recognition.</summary>
    public void Ask(string question)
    {
        if (!IsReady || _inputLocked || string.IsNullOrWhiteSpace(question)) return;

        CancelPipeline();
        _recorder.Cancel();
        _listeningWithoutMicrophone = false;
        _voice?.Stop(false);
        StartPipeline(token => AnswerAsync(question, null, token));
    }

    /// <summary>Asks a question from the selection list; its prepared answer covers an outage.</summary>
    public void AskSuggestion(AiSuggestedQuestion suggestion)
    {
        if (!IsReady || _inputLocked || suggestion == null || string.IsNullOrWhiteSpace(suggestion.Question)) return;

        CancelPipeline();
        _recorder.Cancel();
        _listeningWithoutMicrophone = false;
        _voice?.Stop(false);
        StartPipeline(token => AnswerAsync(suggestion.Question, suggestion, token));
    }

    /// <summary>Clears history and failure counters, e.g. when a new process starts.</summary>
    public void ResetConversation()
    {
        CancelPipeline();
        _recorder?.Cancel();
        _voice?.Stop(false);
        _history.Clear();
        _recognitionFailures = 0;
        _offTopicCount = 0;
        SetTranscript(null);
        SetSubtitle(null);
        HideSuggestions();
        SetState(!IsReady || _inputLocked ? AiConversationState.Unavailable : AiConversationState.Idle);
    }

    async Task TranscribeAndAnswerAsync(AiAudioSample audio, CancellationToken token)
    {
        SetState(AiConversationState.Transcribing);

        AiTranscript transcript;
        try
        {
            transcript = await _services.Stt.TranscribeAsync(
                audio, _settings.Knowledge.PhraseHints, token);
            RegisterNetworkSuccess();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiServiceException exception)
        {
            RegisterServiceFailure(exception, "STT");
            await HandleRecognitionFailureAsync(token);
            return;
        }

        if (!transcript.HasText)
        {
            await HandleRecognitionFailureAsync(token);
            return;
        }

        _recognitionFailures = 0;
        SetTranscript(transcript.Text);
        await AnswerAsync(transcript.Text, null, token);
    }

    async Task AnswerAsync(string question, AiSuggestedQuestion suggestion, CancellationToken token)
    {
        SetTranscript(question);

        var inspection = _safety.InspectInput(question);
        if (!inspection.IsAllowed)
        {
            Debug.Log($"[AI] Question was blocked ({inspection.Reason}).");
            await SpeakAsync(_settings.Dialogue.Blocked, token);
            return;
        }

        var context = AiProcessContextRegistry.Current;

        // Offline or repeatedly failing network: answer from prepared lines instead of hanging.
        if (!_services.Chat.IsMock && IsOffline)
        {
            var prepared = !string.IsNullOrWhiteSpace(suggestion?.FixedAnswer)
                ? suggestion.FixedAnswer
                : _settings.Dialogue.GetOfflineAnswer(question, context);

            ShowSuggestions();
            await SpeakAsync(prepared, token);
            return;
        }

        SetState(AiConversationState.Thinking);
        SetSubtitle(_settings.Dialogue.Thinking);

        var answer = await GenerateAnswerAsync(inspection.Text, context, token);
        if (string.IsNullOrWhiteSpace(answer))
        {
            var fallback = !string.IsNullOrWhiteSpace(suggestion?.FixedAnswer)
                ? suggestion.FixedAnswer
                : _settings.Dialogue.GetFallbackHint(context);

            await SpeakAsync(fallback, token);
            return;
        }

        HideSuggestions();
        RecordHistory(inspection.Text, answer);
        await SpeakAsync(AppendOffTopicSteer(answer), token);
    }

    /// <summary>
    /// Runs the model, inspects the answer and retries once with a stricter instruction when the
    /// safety pass rejects it. Returns null when no usable answer could be produced.
    /// </summary>
    async Task<string> GenerateAnswerAsync(string question, AiProcessContext context, CancellationToken token)
    {
        string retryInstruction = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = _promptBuilder.Build(question, context, _history, _offTopicCount, retryInstruction);

            AiChatResult result;
            try
            {
                result = await _services.Chat.CompleteAsync(request, token);
                RegisterNetworkSuccess();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AiServiceException exception)
            {
                RegisterServiceFailure(exception, "LLM");
                return null;
            }

            if (!result.HasContent) return null;

            var parsed = AiPromptBuilder.ParseResponse(result.Content);
            if (!parsed.HasAnswer) return null;

            var inspection = _safety.InspectOutput(parsed.Answer, context);
            if (inspection.IsAllowed)
            {
                UpdateOffTopicCount(parsed.Topic);
                return inspection.Text;
            }

            Debug.LogWarning($"[AI] Answer rejected by the safety filter ({inspection.Reason}).");
            if (!_settings.Config.Llm.RetryOnSafetyRejection) return null;

            retryInstruction = AiSafetyFilter.BuildRetryInstruction(inspection, context);
        }

        return null;
    }

    /// <summary>음성 인식 실패 (F-013 3.4): one retry line, then the selection list.</summary>
    async Task HandleRecognitionFailureAsync(CancellationToken token)
    {
        _recognitionFailures++;

        var dialogue = _settings.Dialogue;
        var threshold = _settings.Config.Conversation.SuggestionsAfterFailures;

        if (_recognitionFailures >= threshold)
        {
            ShowSuggestions();
            await SpeakAsync(dialogue.RecognitionRepeatedFailure, token);
            return;
        }

        await SpeakAsync(dialogue.RecognitionFirstFailure, token);
    }

    Task HandleShortRecordingAsync(CancellationToken token) =>
        SpeakAsync(_settings.Dialogue.RecordingTooShort, token);

    /// <summary>Synthesizes and plays a line, holding the 답변 중 state until playback ends.</summary>
    async Task SpeakAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        SetSubtitle(text);
        SetState(AiConversationState.Speaking);

        AudioClip clip = null;
        try
        {
            clip = await _services.Tts.SynthesizeAsync(text, token);
            RegisterNetworkSuccess();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiServiceException exception)
        {
            // Losing the voice is recoverable: the subtitle still carries the answer.
            RegisterServiceFailure(exception, "TTS");
        }

        token.ThrowIfCancellationRequested();

        _speechCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _voice.Play(text, clip);

        using (token.Register(() =>
               {
                   _voice.Stop(false);
                   _speechCompletion?.TrySetResult(false);
               }))
        {
            await _speechCompletion.Task;
        }

        _speechCompletion = null;
        token.ThrowIfCancellationRequested();
        SetSubtitle(null);
    }

    void OnSpeechFinished() => _speechCompletion?.TrySetResult(true);

    void RecordHistory(string question, string answer)
    {
        _history.Add(new AiChatMessage(AiChatRole.User, question));
        _history.Add(new AiChatMessage(AiChatRole.Assistant, answer));

        while (_history.Count > MaxHistoryMessages)
            _history.RemoveAt(0);
    }

    /// <summary>
    /// 이후 현재 공정으로 유도한다 (F-013 3.5). The prompt already asks for this; the fixed line
    /// guarantees it once the tolerance is used up, as long as it still fits the speech budget.
    /// </summary>
    string AppendOffTopicSteer(string answer)
    {
        var conversation = _settings.Config.Conversation;
        if (_offTopicCount <= conversation.OffTopicToleranceCount) return answer;

        var steer = _settings.Dialogue.OffTopicSteer;
        if (string.IsNullOrWhiteSpace(steer) || answer.Contains(steer)) return answer;

        return answer.Length + steer.Length + 1 > conversation.DirectAnswerMaxChars
            ? answer
            : $"{answer} {steer}";
    }

    void UpdateOffTopicCount(AiAnswerTopic topic)
    {
        // 반복적으로 무관한 질문을 해도 불이익은 없다 (F-013 3.5); only the steering changes.
        if (topic == AiAnswerTopic.OffTopic) _offTopicCount++;
        else _offTopicCount = 0;
    }

    void RegisterServiceFailure(AiServiceException exception, string stage)
    {
        Debug.LogWarning($"[AI] {stage} failed: {exception.Message}");
        if (!exception.IsNetworkError) return;

        _networkFailures++;
        if (_networkFailures < _settings.Config.Conversation.OfflineFailureThreshold) return;

        _offlineUntil = Time.unscaledTime + _settings.Config.Conversation.OfflineCooldownSeconds;
        Debug.LogWarning($"[AI] Offline mode for {_settings.Config.Conversation.OfflineCooldownSeconds:0}s " +
                         $"after {_networkFailures} network failures.");
    }

    void RegisterNetworkSuccess()
    {
        _networkFailures = 0;
        _offlineUntil = 0f;
    }

    void ShowNotice(string line, bool withSuggestions)
    {
        if (withSuggestions) ShowSuggestions();
        StartPipeline(token => SpeakAsync(line, token));
    }

    /// <summary>
    /// Shows the current process's question list. Called on repeated recognition failure, and
    /// available to gameplay UI that wants to offer the list without waiting for a failure.
    /// </summary>
    public void ShowSuggestions()
    {
        var context = AiProcessContextRegistry.Current;
        var next = _settings.Dialogue.GetSuggestions(context.Process, MaxSuggestions);

        _suggestions.Clear();
        _suggestions.AddRange(next);
        SuggestionsChanged?.Invoke();
    }

    void HideSuggestions()
    {
        if (_suggestions.Count == 0) return;
        _suggestions.Clear();
        SuggestionsChanged?.Invoke();
    }

    void StartPipeline(Func<CancellationToken, Task> body)
    {
        CancelPipeline();
        _pipelineTokenSource = new CancellationTokenSource();
        // Failures are handled inside the pipeline, so the task is intentionally not awaited.
        _ = RunPipelineAsync(body, _pipelineTokenSource.Token);
    }

    async Task RunPipelineAsync(Func<CancellationToken, Task> body, CancellationToken token)
    {
        try
        {
            await body(token);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a new question, a lock or a scene change; the new flow owns the state.
            return;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            await SpeakFallbackAsync(_settings.Dialogue.ServiceError, token);
        }

        if (token.IsCancellationRequested) return;
        SetSubtitle(null);
        SetState(_inputLocked ? AiConversationState.Unavailable : AiConversationState.Idle);
    }

    /// <summary>Last-resort line. Never throws, so it is safe inside a catch block.</summary>
    async Task SpeakFallbackAsync(string line, CancellationToken token)
    {
        try
        {
            await SpeakAsync(line, token);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AI] Fallback line could not be played: {exception.Message}");
        }
    }

    void CancelPipeline()
    {
        if (_pipelineTokenSource == null) return;

        _pipelineTokenSource.Cancel();
        _pipelineTokenSource.Dispose();
        _pipelineTokenSource = null;
        _speechCompletion?.TrySetResult(false);
        _speechCompletion = null;
    }

    void SetState(AiConversationState state)
    {
        if (State == state) return;
        State = state;
        if (logStateChanges) Debug.Log($"[AI] State -> {state}");
        StateChanged?.Invoke(state);
    }

    void SetTranscript(string value)
    {
        Transcript = value;
        TranscriptChanged?.Invoke(value);
    }

    void SetSubtitle(string value)
    {
        if (Subtitle == value) return;
        Subtitle = value;
        SubtitleChanged?.Invoke(value);
    }

    public void OnSceneLoadStart(string sceneName) => ResetConversation();
    public void OnSceneLoadComplete(string sceneName) { }
}
