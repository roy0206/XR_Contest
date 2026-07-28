using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PTT 상태 표시 (F-013 3.3) plus the selection list shown after repeated recognition failures.
/// This is a screen-overlay UIDocument so the whole flow is verifiable without an HMD; the
/// world-space VR HUD of 5단계 can replace the view without touching the conversation layer.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class AiConversationHud : MonoBehaviour
{
    [Tooltip("Typed-question row. Keep it on for desktop testing, off for builds.")]
    [SerializeField] bool showDebugPanel = true;

    UIDocument _document;
    Label _stateIcon;
    Label _stateLabel;
    Label _transcript;
    Label _subtitle;
    Label _serviceLabel;
    VisualElement _levelTrack;
    VisualElement _levelFill;
    VisualElement _suggestions;
    VisualElement _debugPanel;
    TextField _questionField;
    Button _askButton;

    AiConversationManager _conversation;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (_document?.visualTreeAsset == null) return;

        var root = _document.rootVisualElement;
        _stateIcon = root.Q<Label>("ai-state-icon");
        _stateLabel = root.Q<Label>("ai-state-label");
        _transcript = root.Q<Label>("ai-transcript");
        _subtitle = root.Q<Label>("ai-subtitle");
        _serviceLabel = root.Q<Label>("ai-service-label");
        _levelTrack = root.Q<VisualElement>("ai-level-track");
        _levelFill = root.Q<VisualElement>("ai-level-fill");
        _suggestions = root.Q<VisualElement>("ai-suggestions");
        _debugPanel = root.Q<VisualElement>("ai-debug");
        _questionField = root.Q<TextField>("ai-question-field");
        _askButton = root.Q<Button>("ai-ask-button");

        if (_stateLabel == null || _subtitle == null || _suggestions == null)
        {
            Debug.LogError("[AI HUD] Required elements are missing from IeumiHud.uxml.");
            return;
        }

        if (_debugPanel != null)
            _debugPanel.style.display = showDebugPanel ? DisplayStyle.Flex : DisplayStyle.None;

        if (_askButton != null) _askButton.clicked += AskTypedQuestion;
        _questionField?.RegisterCallback<KeyDownEvent>(OnQuestionFieldKeyDown);
        _questionField?.RegisterCallback<FocusInEvent>(OnQuestionFieldFocusIn);
        _questionField?.RegisterCallback<FocusOutEvent>(OnQuestionFieldFocusOut);

        _conversation = AiConversationManager.Instance;
        if (_conversation == null) return;

        _conversation.StateChanged += OnStateChanged;
        _conversation.TranscriptChanged += OnTranscriptChanged;
        _conversation.SubtitleChanged += OnSubtitleChanged;
        _conversation.SuggestionsChanged += OnSuggestionsChanged;
        _conversation.Ready += OnReady;

        OnStateChanged(_conversation.State);
        OnTranscriptChanged(_conversation.Transcript);
        OnSubtitleChanged(_conversation.Subtitle);
        OnSuggestionsChanged();
        OnReady();
    }

    void OnDisable()
    {
        if (_askButton != null) _askButton.clicked -= AskTypedQuestion;
        _questionField?.UnregisterCallback<KeyDownEvent>(OnQuestionFieldKeyDown);
        _questionField?.UnregisterCallback<FocusInEvent>(OnQuestionFieldFocusIn);
        _questionField?.UnregisterCallback<FocusOutEvent>(OnQuestionFieldFocusOut);

        if (_conversation != null) _conversation.SuppressPushToTalk = false;
        if (_conversation == null) return;
        _conversation.StateChanged -= OnStateChanged;
        _conversation.TranscriptChanged -= OnTranscriptChanged;
        _conversation.SubtitleChanged -= OnSubtitleChanged;
        _conversation.SuggestionsChanged -= OnSuggestionsChanged;
        _conversation.Ready -= OnReady;
        _conversation = null;
    }

    void Update()
    {
        if (_conversation == null || _levelFill == null) return;
        if (_conversation.State != AiConversationState.Listening) return;

        // Width is a percentage so the meter follows the track without a layout query.
        _levelFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(_conversation.MicrophoneLevel) * 100f));
    }

    void OnReady()
    {
        if (_serviceLabel == null || _conversation == null) return;
        var offline = _conversation.IsOffline ? " · 오프라인" : string.Empty;
        _serviceLabel.text = _conversation.ServiceSummary + offline;
    }

    void OnStateChanged(AiConversationState state)
    {
        if (_stateLabel == null) return;

        _stateLabel.text = state switch
        {
            AiConversationState.Idle => "질문 가능",
            AiConversationState.Listening => "듣는 중",
            AiConversationState.Transcribing => "변환 중",
            AiConversationState.Thinking => "생각 중",
            AiConversationState.Speaking => "답변 중",
            _ => "사용 불가"
        };

        SetIconModifier(state);
        SetVisible(_levelTrack, "level-track--visible", state == AiConversationState.Listening);
        if (state == AiConversationState.Listening && _levelFill != null)
            _levelFill.style.width = new StyleLength(Length.Percent(0f));

        OnReady();
    }

    void SetIconModifier(AiConversationState state)
    {
        if (_stateIcon == null) return;

        _stateIcon.RemoveFromClassList("state-icon--idle");
        _stateIcon.RemoveFromClassList("state-icon--listening");
        _stateIcon.RemoveFromClassList("state-icon--busy");
        _stateIcon.RemoveFromClassList("state-icon--speaking");

        var modifier = state switch
        {
            AiConversationState.Idle => "state-icon--idle",
            AiConversationState.Listening => "state-icon--listening",
            AiConversationState.Transcribing => "state-icon--busy",
            AiConversationState.Thinking => "state-icon--busy",
            AiConversationState.Speaking => "state-icon--speaking",
            _ => null
        };

        if (modifier != null) _stateIcon.AddToClassList(modifier);
    }

    void OnTranscriptChanged(string value)
    {
        if (_transcript == null) return;
        _transcript.text = string.IsNullOrWhiteSpace(value) ? string.Empty : $"\"{value}\"";
        SetVisible(_transcript, "transcript--visible", !string.IsNullOrWhiteSpace(value));
    }

    void OnSubtitleChanged(string value)
    {
        if (_subtitle == null) return;
        _subtitle.text = value ?? string.Empty;
        SetVisible(_subtitle, "subtitle--visible", !string.IsNullOrWhiteSpace(value));
    }

    void OnSuggestionsChanged()
    {
        if (_suggestions == null || _conversation == null) return;

        _suggestions.Clear();
        var questions = _conversation.Suggestions;

        for (var i = 0; i < questions.Count; i++)
        {
            var suggestion = questions[i];
            if (string.IsNullOrWhiteSpace(suggestion?.Question)) continue;

            var button = new Button(() => _conversation.AskSuggestion(suggestion))
            {
                text = suggestion.Question
            };
            button.AddToClassList("suggestion-button");
            _suggestions.Add(button);
        }

        SetVisible(_suggestions, "suggestions--visible", _suggestions.childCount > 0);
    }

    // The PTT key is read straight from the keyboard, so typing must disable it.
    void OnQuestionFieldFocusIn(FocusInEvent evt)
    {
        if (_conversation != null) _conversation.SuppressPushToTalk = true;
    }

    void OnQuestionFieldFocusOut(FocusOutEvent evt)
    {
        if (_conversation != null) _conversation.SuppressPushToTalk = false;
    }

    void OnQuestionFieldKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
        AskTypedQuestion();
        evt.StopPropagation();
    }

    void AskTypedQuestion()
    {
        if (_conversation == null || _questionField == null) return;

        var question = _questionField.value;
        if (string.IsNullOrWhiteSpace(question)) return;

        _questionField.SetValueWithoutNotify(string.Empty);
        _conversation.Ask(question);
    }

    static void SetVisible(VisualElement element, string modifier, bool visible)
    {
        if (element == null) return;
        if (visible) element.AddToClassList(modifier);
        else element.RemoveFromClassList(modifier);
    }
}
