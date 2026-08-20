using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 현재 퀘스트 목표를 작게 유지하는 임시 인게임 HUD. 표시 문구는 quest.json에서 받고,
/// 진행 판정에는 관여하지 않는다. 향후 손목 UI로 교체해도 QuestManager는 그대로 사용할 수 있다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class QuestHud : MonoBehaviour
{
    const float AttentionAfterSeconds = 5f;

    [SerializeField] QuestManager questManager;
    [SerializeField] bool showDeveloperSkipHint = true;

    UIDocument _document;
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
    bool _completed;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (_document?.visualTreeAsset == null) return;

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

        if (_card == null || _progressFill == null || _title == null || _goal == null || _hint == null)
        {
            Debug.LogError("[QuestHud] QuestHud.uxml의 필수 요소를 찾지 못했습니다.", this);
            return;
        }

        if (questManager == null) questManager = FindAnyObjectByType<QuestManager>();
        if (questManager == null)
        {
            Debug.LogWarning("[QuestHud] 씬에 QuestManager가 없어 HUD를 연결하지 못했습니다.", this);
            Hide();
            return;
        }

        questManager.QuestChanged += HandleQuestChanged;
        questManager.ObjectiveChanged += HandleObjectiveChanged;
        questManager.Completed += HandleCompleted;

        _completed = false;
        _lastProgress = 0f;
        _idleSeconds = 0f;
        RefreshAll();
    }

    void OnDisable()
    {
        if (questManager == null) return;
        questManager.QuestChanged -= HandleQuestChanged;
        questManager.ObjectiveChanged -= HandleObjectiveChanged;
        questManager.Completed -= HandleCompleted;
    }

    void Update()
    {
        if (questManager == null || _card == null || _completed || questManager.CurrentObjective == null) return;

        var progress = Mathf.Clamp01(questManager.ObjectiveProgress);
        SetProgress(progress);

        if (!questManager.IsObjectiveActive)
        {
            SetAttention(false);
            if (_state != null) _state.text = "목표 확인 중";
            return;
        }

        if (progress > _lastProgress + 0.0001f)
            _idleSeconds = 0f;
        else if (questManager.CanEvaluateObjective && progress < 1f)
            _idleSeconds += Time.unscaledDeltaTime;

        _lastProgress = progress;
        SetAttention(_idleSeconds >= AttentionAfterSeconds);

        if (_state != null)
            _state.text = questManager.CanEvaluateObjective ? "진행 중" : "안내를 듣는 중";
    }

    void HandleQuestChanged(QuestDefinition quest)
    {
        _completed = false;
        RefreshAll();
    }

    void HandleObjectiveChanged(QuestNodeData node, int index)
    {
        _completed = false;
        _lastProgress = 0f;
        _idleSeconds = 0f;
        SetAttention(false);
        RefreshAll();
    }

    void HandleCompleted(ProcessId process)
    {
        _completed = true;
        SetProgress(1f);
        SetAttention(false);
        if (_state != null) _state.text = "완료";
        if (_goal != null) _goal.text = "훈련을 완료했습니다.";
        if (_hint != null) _hint.text = "다음 장소로 이동합니다.";
        if (_skip != null) _skip.style.display = DisplayStyle.None;
    }

    void RefreshAll()
    {
        if (questManager == null || string.IsNullOrWhiteSpace(questManager.QuestId))
        {
            Hide();
            return;
        }

        Show();
        if (_category != null) _category.text = "현재 퀘스트";
        _title.text = questManager.QuestTitle ?? questManager.QuestId;

        var node = questManager.CurrentNode;
        var objective = questManager.CurrentObjective;
        var number = questManager.ObjectiveNumber;
        var total = questManager.ObjectiveCount;

        if (_count != null)
            _count.text = number > 0 && total > 0 ? $"{number} / {total}" : $"0 / {total}";

        if (objective == null)
        {
            _goal.text = "훈련 안내를 확인하세요.";
            _hint.text = "안내가 끝나면 첫 목표가 시작됩니다.";
            if (_state != null) _state.text = "준비 중";
            SetProgress(0f);
        }
        else
        {
            _goal.text = string.IsNullOrWhiteSpace(objective.Goal) ? node?.Id ?? "현재 목표" : objective.Goal;
            _hint.text = string.IsNullOrWhiteSpace(node?.ControlHint)
                ? "강조된 대상과 상호작용하세요."
                : node.ControlHint;
            if (_state != null)
                _state.text = questManager.CanEvaluateObjective ? "진행 중" : "안내를 듣는 중";
            SetProgress(questManager.ObjectiveProgress);
        }

        if (_skip != null)
        {
            _skip.style.display = showDeveloperSkipHint && questManager.AllowForceObjective
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _skip.text = $"{KeyName(questManager.ForceObjectiveKey)}  ·  현재 목표 건너뛰기 (개발용)";
        }
    }

    void SetProgress(float progress)
    {
        if (_progressFill == null) return;
        _progressFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(progress) * 100f));
    }

    void SetAttention(bool active)
    {
        if (_card == null) return;
        if (active) _card.AddToClassList("quest-card--attention");
        else _card.RemoveFromClassList("quest-card--attention");
    }

    void Show() => _card?.AddToClassList("quest-card--visible");
    void Hide() => _card?.RemoveFromClassList("quest-card--visible");

    static string KeyName(KeyCode key) => key switch
    {
        KeyCode.Return => "Enter",
        KeyCode.KeypadEnter => "Numpad Enter",
        _ => key.ToString()
    };
}
