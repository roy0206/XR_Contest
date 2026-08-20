using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// QuestManager의 읽기 전용 상태를 인게임 HUD로 표시한다. 진행 판정에는 관여하지 않는다.
/// UI와 매니저의 생성 순서가 달라도 연결을 재시도하며 모든 런타임 상태를 명시적으로 보여준다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class QuestHud : MonoBehaviour
{
    const float AttentionAfterSeconds = 5f;
    const float BindRetrySeconds = 0.5f;
    const string AttentionClass = "quest-card--attention";
    const string BlockedClass = "quest-card--blocked";
    const string CompleteClass = "quest-card--complete";
    const string ErrorClass = "quest-card--error";

    [SerializeField] QuestManager questManager;
    [SerializeField] bool showDeveloperSkipHint = true;

    UIDocument _document;
    QuestManager _boundManager;
    VisualElement _card;
    VisualElement _progressFill;
    Label _category;
    Label _title;
    Label _count;
    Label _goal;
    Label _hint;
    Label _state;
    Label _skip;

    float _lastProgress;
    float _idleSeconds;
    float _nextBindAttempt;
    bool _viewReady;
    bool _templateErrorReported;
    bool _elementErrorReported;
    bool _managerWarningReported;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        _nextBindAttempt = 0f;
        TryPrepareView();
        TryBindManager();
    }

    void OnDisable() => UnbindManager();

    void Update()
    {
        if (!_viewReady && !TryPrepareView()) return;

        if (_boundManager == null)
        {
            _boundManager = null;

            if (Time.unscaledTime >= _nextBindAttempt)
            {
                _nextBindAttempt = Time.unscaledTime + BindRetrySeconds;
                TryBindManager();
            }

            if (_boundManager == null) RenderDisconnected();
            return;
        }

        Render();
    }

    bool TryPrepareView()
    {
        if (_viewReady) return true;
        if (_document == null) _document = GetComponent<UIDocument>();

        if (_document == null || _document.visualTreeAsset == null)
        {
            if (!_templateErrorReported)
            {
                _templateErrorReported = true;
                Debug.LogError("[QuestHud] UIDocument에 QuestHud.uxml이 연결되지 않았습니다.", this);
            }
            return false;
        }

        var root = _document.rootVisualElement;
        _card = root.Q<VisualElement>("quest-card");
        _progressFill = root.Q<VisualElement>("quest-progress-fill");
        _category = root.Q<Label>("quest-category");
        _title = root.Q<Label>("quest-title");
        _count = root.Q<Label>("quest-count");
        _goal = root.Q<Label>("quest-goal");
        _hint = root.Q<Label>("quest-hint");
        _state = root.Q<Label>("quest-state");
        _skip = root.Q<Label>("quest-skip");

        _viewReady = _card != null && _progressFill != null && _category != null && _title != null &&
            _count != null && _goal != null && _hint != null && _state != null && _skip != null;

        if (!_viewReady && !_elementErrorReported)
        {
            _elementErrorReported = true;
            Debug.LogError("[QuestHud] QuestHud.uxml의 필수 요소를 찾지 못했습니다.", this);
        }

        return _viewReady;
    }

    bool TryBindManager()
    {
        if (!_viewReady || _boundManager != null) return _boundManager != null;
        if (questManager == null) questManager = FindAnyObjectByType<QuestManager>();

        if (questManager == null)
        {
            if (!_managerWarningReported)
            {
                _managerWarningReported = true;
                Debug.LogWarning("[QuestHud] QuestManager 연결을 기다리고 있습니다.", this);
            }
            return false;
        }

        _boundManager = questManager;
        _boundManager.QuestChanged += HandleQuestChanged;
        _boundManager.ObjectiveChanged += HandleObjectiveChanged;
        _boundManager.StateChanged += HandleStateChanged;
        _boundManager.Completed += HandleCompleted;
        _managerWarningReported = false;
        ResetProgressTracking();
        Render();
        return true;
    }

    void UnbindManager()
    {
        if (_boundManager != null)
        {
            _boundManager.QuestChanged -= HandleQuestChanged;
            _boundManager.ObjectiveChanged -= HandleObjectiveChanged;
            _boundManager.StateChanged -= HandleStateChanged;
            _boundManager.Completed -= HandleCompleted;
        }

        _boundManager = null;
    }

    void HandleQuestChanged(QuestDefinition quest)
    {
        ResetProgressTracking();
        Render();
    }

    void HandleObjectiveChanged(QuestNodeData node, int index)
    {
        ResetProgressTracking();
        Render();
    }

    void HandleStateChanged(QuestRuntimeState state)
    {
        if (state != QuestRuntimeState.Objective) SetAttention(false);
        Render();
    }

    void HandleCompleted(ProcessId process) => Render();

    void Render()
    {
        if (!_viewReady || _boundManager == null) return;

        Show();
        _category.text = _boundManager.State == QuestRuntimeState.Missing ? "퀘스트 오류" : "현재 퀘스트";
        _title.text = string.IsNullOrWhiteSpace(_boundManager.QuestTitle)
            ? _boundManager.State == QuestRuntimeState.Loading ? "퀘스트 준비 중" : "퀘스트"
            : _boundManager.QuestTitle;
        _count.text = ObjectiveCountText();

        switch (_boundManager.State)
        {
            case QuestRuntimeState.Loading:
                RenderFixed("불러오는 중", "퀘스트를 준비하고 있습니다.", "잠시만 기다려 주세요.", 0f, null);
                break;

            case QuestRuntimeState.Intro:
                RenderFixed("안내 중", "퀘스트 안내를 확인하세요.", "안내가 끝나면 첫 목표가 시작됩니다.", 0f,
                    BlockedClass);
                break;

            case QuestRuntimeState.Objective:
                RenderObjective();
                break;

            case QuestRuntimeState.ObjectiveSuccess:
                RenderFixed("목표 완료", CurrentGoal(), "다음 안내를 확인하세요.", 1f, CompleteClass);
                break;

            case QuestRuntimeState.Complete:
                RenderFixed("완료 확인 중", "퀘스트 목표를 모두 완료했습니다.",
                    "완료 안내가 끝나면 다음 장소로 이동합니다.", 1f, CompleteClass);
                break;

            case QuestRuntimeState.Leaving:
                RenderFixed("이동 중", "퀘스트를 완료했습니다.", "다음 장소로 이동합니다.", 1f, CompleteClass);
                break;

            case QuestRuntimeState.Missing:
                var detail = Application.isEditor || Debug.isDebugBuild
                    ? _boundManager.FailureReason
                    : null;
                RenderFixed("진행 불가", "퀘스트를 불러올 수 없습니다.",
                    string.IsNullOrWhiteSpace(detail) ? "퀘스트 데이터를 확인한 후 다시 시작해 주세요." : detail,
                    0f, ErrorClass);
                break;
        }

        UpdateSkipHint();
    }

    void RenderObjective()
    {
        var progress = Mathf.Clamp01(_boundManager.ObjectiveProgress);
        var hint = CurrentHint();
        var state = "진행 중";
        string mode = null;

        switch (_boundManager.ObjectiveBlockReason)
        {
            case QuestObjectiveBlockReason.Paused:
                state = "일시정지";
                hint = "계속하려면 일시정지를 해제하세요.";
                mode = BlockedClass;
                break;
            case QuestObjectiveBlockReason.Cutscene:
                state = "이야기 진행 중";
                hint = "이야기가 끝나면 목표를 계속할 수 있습니다.";
                mode = BlockedClass;
                break;
            case QuestObjectiveBlockReason.Dialogue:
                state = "안내 중";
                hint = "안내를 들은 뒤 목표를 진행하세요.";
                mode = BlockedClass;
                break;
            case QuestObjectiveBlockReason.ProcessGate:
                state = "진행 대기";
                hint = "현재 진행 중인 상호작용을 마쳐 주세요.";
                mode = BlockedClass;
                break;
            case QuestObjectiveBlockReason.StepGap:
                state = "목표 준비 중";
                mode = BlockedClass;
                break;
            case QuestObjectiveBlockReason.Inactive:
                state = "목표 확인 중";
                mode = BlockedClass;
                break;
        }

        if (progress > _lastProgress + 0.0001f)
            _idleSeconds = 0f;
        else if (_boundManager.CanEvaluateObjective && progress < 1f)
            _idleSeconds += Time.unscaledDeltaTime;
        else
            _idleSeconds = 0f;

        _lastProgress = progress;
        ApplyMode(mode);
        SetAttention(_idleSeconds >= AttentionAfterSeconds);
        _state.text = state;
        _goal.text = CurrentGoal();
        _hint.text = hint;
        SetProgress(progress);
    }

    void RenderFixed(string state, string goal, string hint, float progress, string mode)
    {
        _idleSeconds = 0f;
        ApplyMode(mode);
        SetAttention(false);
        _state.text = state;
        _goal.text = goal;
        _hint.text = hint;
        SetProgress(progress);
    }

    void RenderDisconnected()
    {
        if (!_viewReady) return;
        Show();
        ApplyMode(ErrorClass);
        SetAttention(false);
        _category.text = "퀘스트 상태";
        _title.text = "연결 대기 중";
        _count.text = "-";
        _state.text = "연결 대기";
        _goal.text = "퀘스트 진행 정보를 찾고 있습니다.";
        _hint.text = "QuestManager가 준비되면 자동으로 연결됩니다.";
        _skip.style.display = DisplayStyle.None;
        SetProgress(0f);
    }

    string ObjectiveCountText()
    {
        var total = _boundManager.ObjectiveCount;
        if (total <= 0) return "0 / 0";
        if (_boundManager.State is QuestRuntimeState.Complete or QuestRuntimeState.Leaving)
            return $"{total} / {total}";
        return $"{Mathf.Clamp(_boundManager.ObjectiveNumber, 0, total)} / {total}";
    }

    string CurrentGoal()
    {
        var objective = _boundManager.CurrentObjective;
        if (!string.IsNullOrWhiteSpace(objective?.Goal)) return objective.Goal;
        return string.IsNullOrWhiteSpace(_boundManager.CurrentNode?.Id) ? "현재 목표를 진행하세요." : _boundManager.CurrentNode.Id;
    }

    string CurrentHint()
    {
        var hint = _boundManager.CurrentNode?.ControlHint;
        return string.IsNullOrWhiteSpace(hint) ? "강조된 대상과 상호작용하세요." : hint;
    }

    void UpdateSkipHint()
    {
        var visible = showDeveloperSkipHint && _boundManager.State == QuestRuntimeState.Objective &&
            _boundManager.AllowForceObjective;
        _skip.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (visible)
            _skip.text = $"{KeyName(_boundManager.ForceObjectiveKey)}  ·  현재 목표 건너뛰기 (개발용)";
    }

    void ResetProgressTracking()
    {
        _lastProgress = 0f;
        _idleSeconds = 0f;
        SetAttention(false);
    }

    void SetProgress(float progress) =>
        _progressFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(progress) * 100f));

    void ApplyMode(string mode)
    {
        _card.RemoveFromClassList(BlockedClass);
        _card.RemoveFromClassList(CompleteClass);
        _card.RemoveFromClassList(ErrorClass);
        if (!string.IsNullOrWhiteSpace(mode)) _card.AddToClassList(mode);
    }

    void SetAttention(bool active)
    {
        if (_card == null) return;
        if (active) _card.AddToClassList(AttentionClass);
        else _card.RemoveFromClassList(AttentionClass);
    }

    void Show() => _card?.AddToClassList("quest-card--visible");

    static string KeyName(KeyCode key) => key switch
    {
        KeyCode.Return => "Enter",
        KeyCode.KeypadEnter => "Numpad Enter",
        _ => key.ToString()
    };
}
