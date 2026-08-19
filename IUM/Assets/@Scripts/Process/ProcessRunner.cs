using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 공정 진행 관리 (F-004, F-014). 씬에 하나 놓으면 <see cref="UserProgressData.NextProcess"/>가
/// 가리키는 공정을 process.json에서 찾아 단계를 순서대로 굴린다.
///
/// 진입 대사 → 단계 설명 → 판정 → 성공 대사 → 다음 단계 → 완료 대사 → 진행 저장 → 다음 목적지.
///
/// 위상은 <see cref="Update"/>에서 넘긴다. 코루틴을 쓰지 않는 이유는 <see cref="CutsceneDirector"/>와
/// 같다 — 틱을 멈추면 진행도 멈춰야 하는데 WaitForSeconds는 일시정지를 통과해 버린다.
///
/// 판정 여부는 <see cref="ProcessGate"/> 하나로 결정된다. 대사가 blocksProcess로 게이트를 쥐므로
/// "노장의 설명 종료 전에는 작업 판정을 시작하지 않는다"(F-014 5.3)가 조건문 없이 성립한다.
/// </summary>
public sealed class ProcessRunner : MonoBehaviour
{
    /// <summary>Static data key registered in manifest.json.</summary>
    public const string DataKey = "process";

    enum Phase
    {
        Loading,

        /// <summary>공정 진입 대사가 끝나기를 기다린다.</summary>
        Intro,

        /// <summary>단계 설명과 판정.</summary>
        Step,

        /// <summary>성공 대사가 끝나기를 기다린다.</summary>
        StepSuccess,

        /// <summary>완료 대사가 끝나기를 기다린다.</summary>
        Complete,

        /// <summary>진행을 저장하고 다음 목적지로 넘기는 중. 더 이상 틱하지 않는다.</summary>
        Leaving,

        /// <summary>이 공정의 정의가 없다. 진행을 막지 않고 알리기만 한다.</summary>
        Missing
    }

    [Header("참조")]
    [Tooltip("비우면 씬에서 찾는다. 씬 빌더가 채워 준다.")]
    [SerializeField] Player player;

    [Header("공정")]
    [Tooltip("이 씬이 담당하는 공정. 저장된 진행이 이 씬과 맞지 않을 때 대신 쓴다.")]
    [SerializeField] ProcessId sceneProcess = ProcessId.Tutorial;

    [Tooltip("끄면 저장된 진행을 무시하고 항상 위 공정을 실행한다. 단독 테스트용.")]
    [SerializeField] bool useSavedProgress = true;

    [Tooltip("현재 공정 완료 후 다음 공정 정의를 같은 씬에서 이어서 실행한다. 메인 플레이 씬용.")]
    [SerializeField] bool continueInSameScene;

    [Header("개발 도구")]
    [SerializeField] bool showOverlay = true;

    [Tooltip("현재 단계를 키 입력으로 강제 통과시킨다. 실제 메인 공정에서는 끈다.")]
    [SerializeField] bool allowForceStep = true;

    [Tooltip("현재 단계를 강제로 통과시킨다. 마이크 없이 PTT 단계를 넘기는 경로다.")]
    [SerializeField] KeyCode forceStepKey = KeyCode.Return;

    readonly List<ProcessStep> _steps = new();

    ProcessTable _table;
    ProcessDefinition _definition;
    ProcessStep _current;
    Phase _phase = Phase.Loading;
    int _index = -1;
    float _idleSeconds;
    float _resumeAt;
    bool _introPlayed;

    public ProcessId Process { get; private set; }
    public bool IsRunning => _phase is Phase.Intro or Phase.Step or Phase.StepSuccess or Phase.Complete;

    /// <summary>현재 단계. 안내 UI가 생기면 여기를 읽는다.</summary>
    public ProcessStepData CurrentStep => _current?.Data;

    /// <summary>단계가 바뀔 때. 두 번째 인자는 0부터 시작하는 순번이다.</summary>
    public event Action<ProcessStepData, int> StepChanged;

    /// <summary>공정을 마치고 다음 목적지로 넘어가기 직전.</summary>
    public event Action<ProcessId> Completed;

    void Awake()
    {
        if (player == null) player = FindAnyObjectByType<Player>();
        if (player == null)
            Debug.LogWarning("[Process] 씬에 Player가 없어 입력 기반 단계를 판정할 수 없습니다.", this);

        _ = InitializeAsync();
    }

    async Task InitializeAsync()
    {
        _table = await LoadTableAsync();
        _table.Prepare();

        if (this == null) return;

        var process = ResolveProcess();
        Process = process;
        if (!BeginProcess(process))
        {
            // 진행을 막지 않는다. 아직 정의하지 않은 공정에 들어온 것은 개발 중에 흔한 상태이고,
            // 여기서 멈춰 세우면 그 씬에서 아무것도 할 수 없다.
            Debug.LogWarning($"[Process] '{Process}'의 정의가 process.json에 없습니다.");
            _phase = Phase.Missing;
            return;
        }

    }

    bool BeginProcess(ProcessId process)
    {
        if (!_table.TryGet(process, out var definition) || !definition.HasSteps) return false;

        Process = process;
        _definition = definition;
        _steps.Clear();
        _current = null;
        _index = -1;
        _idleSeconds = 0f;
        _resumeAt = 0f;
        _introPlayed = false;

        for (var i = 0; i < _definition.Steps.Count; i++)
            _steps.Add(new ProcessStep(_definition.Steps[i], player));

        BeginIntro();
        return true;
    }

    /// <summary>
    /// 어느 공정을 실행할지 정한다. 정상 흐름에서는 <see cref="GameFlow"/>가 진행에 맞는 씬으로
    /// 보내므로 저장된 값이 곧 이 씬의 공정이다.
    ///
    /// 씬을 에디터에서 직접 열면 그 전제가 깨진다. 튜토리얼을 한 번 완주하면 저장된 진행은
    /// 다음 공정을 가리키는데, 그 공정의 정의는 이 씬에 없으므로 대사도 잠금도 없는 빈 씬이
    /// 된다. 개발 중에는 씬을 직접 여는 것이 기본 작업 방식이라 그쪽을 막아 둔다.
    /// </summary>
    ProcessId ResolveProcess()
    {
        if (!useSavedProgress) return sceneProcess;

        if (!DataManager.TryGetInstance(out var data) || !data.IsReady) return sceneProcess;

        var saved = data.Progress.NextProcess;
        if (_table.TryGet(saved, out var definition) && definition.HasSteps) return saved;

        // 저장된 공정을 이 데이터로는 실행할 수 없다. 씬이 선언한 공정으로 돌린다.
        if (saved == sceneProcess) return saved;

        Debug.LogWarning(
            $"[Process] 저장된 진행은 '{saved}'인데 정의가 없어 이 씬의 '{sceneProcess}'를 실행합니다. " +
            "정상 흐름으로 확인하려면 메인 화면에서 새 게임을 시작하십시오.");
        return sceneProcess;
    }

    async Task<ProcessTable> LoadTableAsync()
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            if (DataManager.Instance.Static != null &&
                DataManager.Instance.Static.TryGet<ProcessTable>(DataKey, out var table) &&
                table != null)
                return table;

            Debug.LogWarning($"[Process] 정적 데이터 '{DataKey}'가 없어 빈 공정표로 시작합니다.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Process] 공정 데이터를 불러오지 못했습니다: {exception.Message}");
        }

        return ProcessTable.CreateEmpty();
    }

    void BeginIntro()
    {
        _phase = Phase.Intro;

        // 대사가 없으면 기다릴 것도 없다. 다음 틱에서 첫 단계로 넘어간다.
        PlayDialogue(_definition.IntroDialogue);
    }

    void Update()
    {
        if (PauseService.IsPaused || !IsRunning) return;

        // 컷씬이 화면을 쥐고 있는 동안에는 공정도 멈춘다. 컷씬은 ProcessGate도 함께 잡으므로
        // 판정은 어차피 열리지 않지만, 위상 전환까지 멈춰야 대사가 겹치지 않는다.
        if (CutsceneDirector.TryGetInstance(out var director) && director.IsPlaying) return;

        var now = PauseService.Now;
        if (now < _resumeAt) return;

        switch (_phase)
        {
            case Phase.Intro:
                if (!IsSpeaking) AdvanceStep();
                break;

            case Phase.Step:
                TickStep();
                break;

            case Phase.StepSuccess:
                if (!IsSpeaking) AdvanceStep();
                break;

            case Phase.Complete:
                if (!IsSpeaking) BeginLeave();
                break;
        }
    }

    void TickStep()
    {
        if (_current == null) return;

        if (!_introPlayed)
        {
            _introPlayed = true;
            PlayDialogue(_current.Data.IntroDialogue);
        }

        if (ForcePressed())
        {
            _current.ForceSatisfy();
            SucceedStep();
            return;
        }

        // 대사가 도는 동안에는 판정하지 않는다 (F-014 5.3). 재안내 타이머도 함께 멈춘다 —
        // 설명을 듣고 있는 시간을 "진전 없음"으로 셀 수는 없다.
        if (!ProcessGate.IsOpen) return;

        var delta = Time.unscaledDeltaTime;
        _current.Tick(delta);

        if (_current.IsSatisfied)
        {
            SucceedStep();
            return;
        }

        TickRetry(delta);
    }

    /// <summary>F-004 4.5의 10초 재안내. 진전이 있으면 타이머를 되돌린다.</summary>
    void TickRetry(float delta)
    {
        if (string.IsNullOrWhiteSpace(_current.Data.RetryDialogue)) return;

        if (_current.MadeProgress)
        {
            _idleSeconds = 0f;
            return;
        }

        _idleSeconds += delta;
        if (_idleSeconds < _table.Settings.RetryAfterSeconds) return;

        _idleSeconds = 0f;
        PlayDialogue(_current.Data.RetryDialogue);
    }

    void SucceedStep()
    {
        _phase = Phase.StepSuccess;
        _idleSeconds = 0f;
        PlayDialogue(_current.Data.SuccessDialogue);
    }

    /// <summary>다음 단계로 넘어가거나, 남은 단계가 없으면 완료로 간다.</summary>
    void AdvanceStep()
    {
        _index++;

        if (_index >= _steps.Count)
        {
            BeginComplete();
            return;
        }

        _current = _steps[_index];
        _phase = Phase.Step;
        _idleSeconds = 0f;
        _introPlayed = false;

        // 설명 대사는 여기서 재생하지 않는다. 간격이 지난 뒤 TickStep이 재생해야 앞 단계의 성공
        // 대사와 붙어 들리지 않는다.
        _resumeAt = PauseService.Now + _table.Settings.StepGapSeconds;

        ApplyLocks(_current.Data);
        _current.Arm();

        StepChanged?.Invoke(_current.Data, _index);
    }

    /// <summary>
    /// 공정 잠금 (F-014 5.3). unlock이 비어 있으면 판정 대상 하나만 열린다.
    ///
    /// 한 번에 훑으며 각 대상의 최종 상태를 정한다. 전부 잠근 뒤 다시 여는 방식이면 안 된다 —
    /// <see cref="Grabbable.GrabEnabled"/>를 끄면 쥐고 있던 손이 즉시 놓으므로, 계속 열려 있어야
    /// 할 도구도 단계가 바뀔 때마다 한 번씩 손에서 떨어진다. 잡기에서 놓기로 넘어갈 때 톱이
    /// 바닥에 떨어지던 원인이 이것이었다.
    /// </summary>
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

    void BeginComplete()
    {
        _phase = Phase.Complete;
        _current = null;

        // 공정이 끝났으니 도구를 모두 잠근다. 완료 대사가 흐르는 동안 물건을 집어 드는 그림은
        // 원하는 것이 아니다.
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
        var continueHere = false;
        var nextProcess = ProcessId.Completed;

        if (DataManager.TryGetInstance(out var data) && data.IsReady)
        {
            var progress = data.Progress;
            progress.Complete(process, _definition.Grade);

            nextProcess = progress.NextProcess;
            continueHere = continueInSameScene &&
                           _table.TryGet(nextProcess, out var nextDefinition) &&
                           nextDefinition.HasSteps;

            // 같은 작업장에서 다음 공정을 이어 갈 때는 위치도 작업 상태의 일부다. 엔딩처럼 씬을
            // 실제로 떠날 때만 지워 다음 장소에 이전 좌표가 적용되지 않게 한다.
            if (!continueHere) progress.PlayerPose = null;

            try
            {
                await data.SaveUserAsync();
            }
            catch (Exception exception)
            {
                // 치명적이지 않다. 메모리상 진행은 이미 올라갔으므로 다음 저장 때 함께 기록된다.
                Debug.LogError($"[Process] 진행 저장에 실패했습니다: {exception.Message}");
            }
        }

        if (this == null) return;

        Completed?.Invoke(process);

        if (continueHere && BeginProcess(nextProcess))
        {
            Debug.Log($"[Process] 같은 씬에서 다음 공정 '{Process}'을 시작합니다.");
            return;
        }

        GameFlow.Instance.GoToCurrent();
    }

    static bool IsSpeaking =>
        InGameDialogue.TryGetInstance(out var dialogue) && dialogue.IsSpeaking;

    static void PlayDialogue(string sequenceId)
    {
        if (string.IsNullOrWhiteSpace(sequenceId)) return;

        // 조용히 버리지 않는다. 대사 시스템이 아직 준비되지 않았거나 씬에 없으면 그 줄은
        // 사라지는데, 흔적이 없으면 원인을 찾을 수 없다.
        if (!InGameDialogue.TryGetInstance(out var dialogue) || !dialogue.IsReady)
        {
            Debug.LogWarning($"[Process] 대사 시스템이 준비되지 않아 '{sequenceId}'를 재생하지 못했습니다.");
            return;
        }

        dialogue.Play(sequenceId);
    }

    bool ForcePressed()
    {
        if (!allowForceStep) return false;
        var input = UserInput.Instance;
        return input != null && input.GetKeyDown(forceStepKey);
    }

    void OnGUI()
    {
        if (!showOverlay) return;

        var step = _current?.Data;
        var lines = new[]
        {
            $"공정: {Process}   단계: {(_definition != null ? $"{_index + 1}/{_steps.Count}" : "-")}   위상: {_phase}",
            $"목표: {(step != null ? step.Goal : _phase == Phase.Missing ? "정의 없음" : "-")}",
            $"조건: {(step != null ? $"{step.Condition} {_current.Progress * 100f:F0}%" : "-")}",
            $"공정 판정: {ProcessGate.Describe()}",
            allowForceStep
                ? $"{forceStepKey} 현재 단계 강제 통과 · Esc 일시정지"
                : "Esc 일시정지"
        };

        GUI.Box(new Rect(10f, 10f, 620f, 22f * lines.Length + 12f), string.Empty);
        for (var i = 0; i < lines.Length; i++)
            GUI.Label(new Rect(20f, 16f + i * 22f, 600f, 22f), lines[i]);
    }
}
