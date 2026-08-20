using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// GameFlow가 진입시킨 한 퀘스트 그래프를 실행한다. 씬·컷씬 이동과 진행 저장은 직접 결정하지
/// 않고 완료 결과를 GameFlow에 보고한다. 현재 튜토리얼의 단계 판정을 일반화한 런타임이다.
/// 실행 전 그래프 계약은 <see cref="QuestGraphValidator"/>로 검증한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class QuestManager : MonoBehaviour
{
    public const string DataKey = "quest";

    enum Phase
    {
        Loading,
        Intro,
        Objective,
        ObjectiveSuccess,
        Complete,
        Leaving,
        Missing
    }

    [Header("참조")]
    [Tooltip("비우면 씬에서 찾습니다.")]
    [SerializeField] Player player;

    [Header("퀘스트")]
    [Tooltip("씬을 직접 실행할 때 사용할 퀘스트 ID입니다.")]
    [SerializeField] string sceneQuestId = "tutorial";

    [Tooltip("끄면 저장된 진행을 무시하고 sceneQuestId를 실행합니다.")]
    [SerializeField] bool useSavedProgress = true;

    [Header("개발 도구")]
    [SerializeField] bool showOverlay = true;
    [SerializeField] bool allowForceObjective = true;
    [SerializeField] KeyCode forceObjectiveKey = KeyCode.Return;

    QuestTable _table;
    QuestDefinition _definition;
    QuestNodeData _node;
    ProcessStep _objective;
    Phase _phase = Phase.Loading;
    int _objectiveIndex = -1;
    int _objectiveCount;
    float _idleSeconds;
    float _resumeAt;
    bool _introPlayed;

    public string QuestId => _definition?.Id;
    public ProcessId Process { get; private set; }
    public bool IsRunning => _phase is Phase.Intro or Phase.Objective or Phase.ObjectiveSuccess or Phase.Complete;
    public QuestNodeData CurrentNode => _node;
    public ProcessStepData CurrentObjective => _objective?.Data;

    public event Action<QuestDefinition> QuestChanged;
    public event Action<QuestNodeData, int> ObjectiveChanged;
    public event Action<ProcessId> Completed;

    void Awake()
    {
        if (player == null) player = FindAnyObjectByType<Player>();
        if (player == null)
            Debug.LogWarning("[Quest] 씬에 Player가 없어 입력 기반 목표를 판정할 수 없습니다.", this);

        _ = InitializeAsync();
    }

    async Task InitializeAsync()
    {
        _table = await LoadTableAsync();
        _table.Settings ??= new ProcessSettings();
        _table.Settings.Clamp();
        _table.Quests ??= new System.Collections.Generic.List<QuestDefinition>();

        var validation = QuestGraphValidator.Validate(_table);
        foreach (var error in validation)
            Debug.LogError($"[Quest] {error}", this);

        if (InGameDialogue.TryGetInstance(out var dialogue))
        {
            try
            {
                await dialogue.InitializeAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Quest] 대사 시스템 준비에 실패해 대사 없이 진행합니다: {exception.Message}");
            }
        }

        if (this == null) return;

        if (!ResolveQuest(out var quest) || !BeginQuest(quest))
        {
            Debug.LogWarning($"[Quest] 이 씬에서 실행할 퀘스트 '{sceneQuestId}'를 찾지 못했습니다.", this);
            _phase = Phase.Missing;
        }
    }

    bool ResolveQuest(out QuestDefinition quest)
    {
        quest = null;
        if (useSavedProgress && DataManager.TryGetInstance(out var data) && data.IsReady)
        {
            if (_table.TryGet(data.Progress.NextProcess, out quest)) return true;

            if (_table.TryGet(sceneQuestId, out quest))
            {
                Debug.LogWarning(
                    $"[Quest] 저장된 진행 '{data.Progress.NextProcess}'에 해당하는 퀘스트가 없어 " +
                    $"씬 기본값 '{sceneQuestId}'를 실행합니다.", this);
                return true;
            }

            return false;
        }

        if (_table.TryGet(sceneQuestId, out quest)) return true;

        if (useSavedProgress)
            Debug.LogWarning(
                $"[Quest] 저장 데이터가 준비되지 않아 씬 기본값 '{sceneQuestId}'도 찾지 못했습니다.", this);

        return false;
    }

    bool BeginQuest(QuestDefinition definition)
    {
        if (definition == null || !definition.HasGraph ||
            !definition.TryGetNode(definition.EntryNode, out var entry) ||
            entry.Kind != QuestNodeKind.Entry)
            return false;

        _definition = definition;
        _node = entry;
        _objective = null;
        _objectiveIndex = -1;
        _objectiveCount = CountObjectives(definition);
        _idleSeconds = 0f;
        _resumeAt = 0f;
        _introPlayed = false;
        Process = definition.Process;
        _phase = Phase.Intro;

        PlayDialogue(definition.IntroDialogue);
        QuestChanged?.Invoke(definition);
        return true;
    }

    static int CountObjectives(QuestDefinition definition)
    {
        var count = 0;
        if (definition?.Nodes == null) return count;
        foreach (var node in definition.Nodes)
            if (node?.Kind == QuestNodeKind.Objective)
                count++;
        return count;
    }

    async Task<QuestTable> LoadTableAsync()
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            if (DataManager.Instance.Static != null &&
                DataManager.Instance.Static.TryGet<QuestTable>(DataKey, out var table) &&
                table != null)
                return table;

            Debug.LogWarning($"[Quest] 정적 데이터 '{DataKey}'가 없어 빈 퀘스트 표로 시작합니다.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Quest] 퀘스트 데이터를 불러오지 못했습니다: {exception.Message}");
        }

        return QuestTable.CreateEmpty();
    }

    void Update()
    {
        if (PauseService.IsPaused || !IsRunning) return;
        if (CutsceneDirector.TryGetInstance(out var director) && director.IsPlaying) return;
        if (PauseService.Now < _resumeAt) return;

        switch (_phase)
        {
            case Phase.Intro:
                if (!IsSpeaking) AdvanceFromCurrent();
                break;

            case Phase.Objective:
                TickObjective();
                break;

            case Phase.ObjectiveSuccess:
                if (!IsSpeaking) AdvanceFromCurrent();
                break;

            case Phase.Complete:
                if (!IsSpeaking) BeginLeave();
                break;
        }
    }

    void TickObjective()
    {
        if (_objective == null) return;

        if (!_introPlayed)
        {
            _introPlayed = true;
            PlayDialogue(_objective.Data.IntroDialogue);
        }

        if (ForcePressed())
        {
            _objective.ForceSatisfy();
            SucceedObjective();
            return;
        }

        if (!ProcessGate.IsOpen) return;

        var delta = Time.unscaledDeltaTime;
        _objective.Tick(delta);
        if (_objective.IsSatisfied)
        {
            SucceedObjective();
            return;
        }

        TickRetry(delta);
    }

    void TickRetry(float delta)
    {
        if (_objective == null || string.IsNullOrWhiteSpace(_objective.Data.RetryDialogue)) return;

        if (_objective.MadeProgress)
        {
            _idleSeconds = 0f;
            return;
        }

        _idleSeconds += delta;
        if (_idleSeconds < _table.Settings.RetryAfterSeconds) return;

        _idleSeconds = 0f;
        PlayDialogue(_objective.Data.RetryDialogue);
    }

    void SucceedObjective()
    {
        _phase = Phase.ObjectiveSuccess;
        _idleSeconds = 0f;
        PlayDialogue(_objective.Data.SuccessDialogue);
    }

    void AdvanceFromCurrent()
    {
        if (_definition == null || _node == null || !_definition.TryGetNext(_node.Id, out var next))
        {
            FailGraph($"노드 '{_node?.Id ?? "<null>"}'의 다음 노드를 찾지 못했습니다.");
            return;
        }

        EnterNode(next);
    }

    void EnterNode(QuestNodeData node)
    {
        _node = node;
        switch (node.Kind)
        {
            case QuestNodeKind.Objective:
                if (node.Objective == null)
                {
                    FailGraph($"목표 노드 '{node.Id}'에 objective가 없습니다.");
                    return;
                }

                _objectiveIndex++;
                _objective = new ProcessStep(node.Objective, player);
                _phase = Phase.Objective;
                _idleSeconds = 0f;
                _introPlayed = false;
                _resumeAt = PauseService.Now + _table.Settings.StepGapSeconds;

                ApplyLocks(node.Objective);
                _objective.Arm();
                ObjectiveChanged?.Invoke(node, _objectiveIndex);
                break;

            case QuestNodeKind.Complete:
                BeginComplete();
                break;

            default:
                FailGraph($"진행 중 Entry 노드 '{node.Id}'에 다시 진입했습니다.");
                break;
        }
    }

    void BeginComplete()
    {
        _phase = Phase.Complete;
        _objective = null;
        ProcessTarget.SetAllAvailable(false);
        PlayDialogue(_definition.CompleteDialogue);
    }

    void BeginLeave()
    {
        _phase = Phase.Leaving;
        _ = LeaveAsync();
    }

    async Task LeaveAsync()
    {
        var process = Process;
        Completed?.Invoke(process);

        if (!GameFlow.TryGetInstance(out var flow))
        {
            Debug.LogError("[Quest] 완료 결과를 받을 GameFlow가 없습니다.", this);
            return;
        }

        await flow.CompleteQuestAsync(process, _definition.Grade);
    }

    void FailGraph(string message)
    {
        _phase = Phase.Missing;
        _objective = null;
        ProcessTarget.SetAllAvailable(false);
        Debug.LogError($"[Quest] {message}", this);
    }

    static void ApplyLocks(ProcessStepData step)
    {
        var keys = step.Unlock != null && step.Unlock.Count > 0 ? step.Unlock : null;

        foreach (var target in ProcessTarget.All)
        {
            if (target == null) continue;

            var unlocked = keys != null
                ? keys.Contains(target.Key)
                : string.Equals(target.Key, step.Target, StringComparison.Ordinal);

            target.SetAvailable(unlocked);
        }
    }

    static bool IsSpeaking =>
        InGameDialogue.TryGetInstance(out var dialogue) && dialogue.IsSpeaking;

    static void PlayDialogue(string sequenceId)
    {
        if (string.IsNullOrWhiteSpace(sequenceId)) return;

        if (!InGameDialogue.TryGetInstance(out var dialogue) || !dialogue.IsReady)
        {
            Debug.LogWarning($"[Quest] 대사 시스템이 준비되지 않아 '{sequenceId}'를 재생하지 못했습니다.");
            return;
        }

        dialogue.Play(sequenceId);
    }

    bool ForcePressed()
    {
        if (!allowForceObjective) return false;
        var input = UserInput.Instance;
        return input != null && input.GetKeyDown(forceObjectiveKey);
    }

    void OnGUI()
    {
        if (!showOverlay) return;

        var step = _objective?.Data;
        var lines = new[]
        {
            $"퀘스트: {QuestId ?? "-"}   목표: {(_definition != null ? $"{_objectiveIndex + 1}/{_objectiveCount}" : "-")}   상태: {_phase}",
            $"노드: {_node?.Id ?? "-"}",
            $"목표: {(step != null ? step.Goal : _phase == Phase.Missing ? "그래프 오류" : "-")}",
            $"조건: {(step != null ? $"{step.Condition} {_objective.Progress * 100f:F0}%" : "-")}",
            $"공정 판정: {ProcessGate.Describe()}",
            allowForceObjective
                ? $"{forceObjectiveKey} 현재 목표 강제 통과 · Esc 일시정지"
                : "Esc 일시정지"
        };

        GUI.Box(new Rect(10f, 10f, 680f, 22f * lines.Length + 12f), string.Empty);
        for (var i = 0; i < lines.Length; i++)
            GUI.Label(new Rect(20f, 16f + i * 22f, 660f, 22f), lines[i]);
    }
}
