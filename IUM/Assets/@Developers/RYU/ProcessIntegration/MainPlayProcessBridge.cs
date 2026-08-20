using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 메인 플레이 씬의 제작 오브젝트를 데이터 기반 공정 러너에 연결한다.
/// 팀원 소유 <see cref="InkLineZone"/>은 수정하지 않고 공개된 완료 이벤트만 구독한다.
/// </summary>
public sealed class MainPlayProcessBridge : MonoBehaviour
{
    public const string MakmeokSignal = "main.makmeok.lines";
    public const string SawingSignal = "main.sawing.complete";
    public const string PurlinInstallSignal = "main.purlin.install";
    public const string GongpoPuzzleSignal = "main.gongpo.assembled";
    public const string PurlinPartId = "1floor";
    public const int GongpoRequiredPartCount = 37;

    [SerializeField] ProcessRunner runner;
    [SerializeField] List<InkLineZone> inkLineZones = new();
    [SerializeField] PlaneZone sawingZone;
    [SerializeField] List<AssemblyTarget> assemblyTargets = new();

    [Header("미구현 공정 검증")]
    [Tooltip("실제 도구가 아직 없는 공정에서만 Enter로 해당 공정의 검증 신호를 보냅니다.")]
    [SerializeField] bool enableDesktopPlaceholders = true;
    [SerializeField] KeyCode completePlaceholderKey = KeyCode.Return;
    [SerializeField] bool showOverlay = true;

    readonly Dictionary<InkLineZone, UnityAction> _inkListeners = new();
    readonly Dictionary<AssemblyTarget, Action<Grabbable>> _assemblyListeners = new();
    readonly HashSet<AssemblyTarget> _completedAssemblyTargets = new();
    readonly Dictionary<Grabbable, bool> _originalGrabAccess = new();
    readonly Dictionary<Grabbable, string> _assemblyPartIds = new();
    readonly List<Grabbable> _inkTools = new();
    readonly List<Grabbable> _planeTools = new();
    UnityAction _sawingListener;
    PauseController _pauseController;
    ProcessId? _activeProcess;
    int _completedInkLineCount;
    int _completedPurlinCount;
    int _completedGongpoCount;
    bool _sawingCompleted;
    bool _started;
    bool _restartRequested;

    void Awake()
    {
        if (runner == null) runner = FindAnyObjectByType<ProcessRunner>();
        if (sawingZone == null) sawingZone = FindAnyObjectByType<PlaneZone>();
        assemblyTargets ??= new List<AssemblyTarget>();
        if (assemblyTargets.Count == 0)
            assemblyTargets.AddRange(FindObjectsByType<AssemblyTarget>(FindObjectsSortMode.None));

        CacheGatedObjects();
    }

    void OnEnable()
    {
        ResetAllSignals();

        if (runner != null)
        {
            runner.ProcessChanged += OnProcessChanged;
            runner.Completed += OnProcessCompleted;
        }

        for (var i = 0; i < inkLineZones.Count; i++)
        {
            var zone = inkLineZones[i];
            if (zone == null || _inkListeners.ContainsKey(zone)) continue;

            UnityAction listener = () => OnInkLineCompleted(zone);
            _inkListeners.Add(zone, listener);
            zone.OnWorkCompleted.AddListener(listener);
        }

        if (sawingZone != null)
        {
            _sawingListener = OnSawingCompleted;
            sawingZone.OnWorkCompleted.AddListener(_sawingListener);
        }

        BindPauseController();
        ApplyAccess(null);

        if (_started)
        {
            BindAssemblyTargets();
            SynchronizeRunningProcess();
        }
    }

    void Start()
    {
        _started = true;
        BindAssemblyTargets();
        BindPauseController();
        SynchronizeRunningProcess();
    }

    void OnDisable()
    {
        foreach (var pair in _inkListeners)
            if (pair.Key != null) pair.Key.OnWorkCompleted.RemoveListener(pair.Value);

        if (sawingZone != null && _sawingListener != null)
            sawingZone.OnWorkCompleted.RemoveListener(_sawingListener);

        if (runner != null)
        {
            runner.ProcessChanged -= OnProcessChanged;
            runner.Completed -= OnProcessCompleted;
        }

        if (_pauseController != null)
            _pauseController.ProcessRestartRequested -= OnProcessRestartRequested;

        foreach (var pair in _assemblyListeners)
            if (pair.Key != null && pair.Key.Snap != null)
                pair.Key.Snap.Assembled -= pair.Value;

        _inkListeners.Clear();
        _assemblyListeners.Clear();
        _completedAssemblyTargets.Clear();
        _sawingListener = null;
        _pauseController = null;
        _activeProcess = null;
        _completedInkLineCount = 0;
        _completedPurlinCount = 0;
        _completedGongpoCount = 0;
        _sawingCompleted = false;
        _restartRequested = false;
    }

    void CacheGatedObjects()
    {
        foreach (var tool in FindObjectsByType<InkLineTool>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            RememberTool(tool != null ? tool.GetComponentInParent<Grabbable>(true) : null, _inkTools);

        foreach (var tool in FindObjectsByType<HandPlaneTool>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            RememberTool(tool != null ? tool.GetComponentInParent<Grabbable>(true) : null, _planeTools);

        foreach (var male in FindObjectsByType<MaleSnapPoint>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (male == null) continue;

            var part = male.GetComponentInParent<Grabbable>(true);
            if (part == null) continue;

            RememberAccess(part);
            _assemblyPartIds.TryAdd(part, male.mySnapID);
        }
    }

    void RememberTool(Grabbable tool, List<Grabbable> collection)
    {
        if (tool == null || collection.Contains(tool)) return;
        collection.Add(tool);
        RememberAccess(tool);
    }

    void RememberAccess(Grabbable target)
    {
        if (target != null) _originalGrabAccess.TryAdd(target, target.GrabEnabled);
    }

    void BindPauseController()
    {
        if (_pauseController != null) return;
        if (!PauseController.TryGetInstance(out var pause)) return;

        _pauseController = pause;
        _pauseController.ProcessRestartRequested += OnProcessRestartRequested;
    }

    void SynchronizeRunningProcess()
    {
        if (runner != null && runner.IsRunning) OnProcessChanged(runner.Process);
    }

    void OnProcessChanged(ProcessId process)
    {
        if (_activeProcess == process) return;

        _activeProcess = process;
        ResetProcessState(process);
        ApplyAccess(process);
        BindAssemblyTargets();
        SynchronizeAssemblyState(process);

        Debug.Log($"[MainPlay] 공정 연결 상태를 '{process}'에 맞췄습니다.", this);
    }

    void OnProcessCompleted(ProcessId process)
    {
        if (_activeProcess != process) return;
        ApplyAccess(null);
    }

    void ResetProcessState(ProcessId process)
    {
        var signal = SignalForProcess(process);
        if (!string.IsNullOrEmpty(signal)) ProcessSignalBus.Reset(signal);

        switch (process)
        {
            case ProcessId.Makmeok:
                _completedInkLineCount = 0;
                break;
            case ProcessId.Sawing:
                _sawingCompleted = false;
                break;
            case ProcessId.PurlinInstall:
                _completedAssemblyTargets.Clear();
                _completedPurlinCount = 0;
                break;
            case ProcessId.GongpoPuzzle:
                _completedAssemblyTargets.Clear();
                _completedGongpoCount = 0;
                break;
        }
    }

    void ResetAllSignals()
    {
        ProcessSignalBus.Reset(MakmeokSignal);
        ProcessSignalBus.Reset(SawingSignal);
        ProcessSignalBus.Reset(PlaceholderSignal(ProcessId.Chiseling));
        ProcessSignalBus.Reset(PurlinInstallSignal);
        ProcessSignalBus.Reset(GongpoPuzzleSignal);
    }

    void ApplyAccess(ProcessId? process)
    {
        SetToolAccess(_inkTools, process == ProcessId.Makmeok);
        SetToolAccess(_planeTools, process == ProcessId.Sawing);

        foreach (var pair in _assemblyPartIds)
        {
            var part = pair.Key;
            if (part == null) continue;

            var assemblyPart = part.GetComponent<AssemblyPart>() ?? part.GetComponentInChildren<AssemblyPart>();
            var assembled = assemblyPart != null && assemblyPart.isAssembled;
            var available = process.HasValue &&
                            IsAssemblyPartAvailable(process.Value, pair.Value, assembled) &&
                            _originalGrabAccess.GetValueOrDefault(part);

            SetGrabAccess(part, available);
        }
    }

    void SetToolAccess(List<Grabbable> tools, bool available)
    {
        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            if (tool == null) continue;
            SetGrabAccess(tool, available && _originalGrabAccess.GetValueOrDefault(tool));
        }
    }

    static void SetGrabAccess(Grabbable target, bool available)
    {
        if (target != null && target.GrabEnabled != available) target.GrabEnabled = available;
    }

    public static bool IsAssemblyPartAvailable(ProcessId process, string partId, bool assembled)
    {
        if (assembled || string.IsNullOrWhiteSpace(partId)) return false;

        var isPurlin = string.Equals(partId, PurlinPartId, StringComparison.Ordinal);
        return process == ProcessId.PurlinInstall ? isPurlin :
               process == ProcessId.GongpoPuzzle && !isPurlin;
    }

    void OnProcessRestartRequested()
    {
        if (_restartRequested) return;

        var sceneName = gameObject.scene.name;
        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[MainPlay] 공정 재시작 씬 '{sceneName}'을 Build Settings에서 찾을 수 없습니다.", this);
            return;
        }

        if (!SceneController.TryGetInstance(out var scenes))
        {
            Debug.LogError("[MainPlay] SceneController가 없어 공정을 다시 시작할 수 없습니다.", this);
            return;
        }

        _restartRequested = true;
        InGameDialogue.TryGetInstance(out var dialogue);
        dialogue?.Stop();
        ResetAllSignals();

        Debug.Log($"[MainPlay] 현재 공정 '{runner?.Process}'을 처음부터 다시 시작합니다.", this);
        scenes.LoadScene(sceneName);
    }

    void OnInkLineCompleted(InkLineZone zone)
    {
        if (zone == null || runner == null || runner.Process != ProcessId.Makmeok) return;

        var requiredCount = Mathf.Max(1, inkLineZones.Count);
        if (_completedInkLineCount >= requiredCount) return;

        _completedInkLineCount++;
        ProcessSignalBus.Add(MakmeokSignal);
        Debug.Log($"[MainPlay] 먹선 완료 {_completedInkLineCount}/{requiredCount}", zone);
    }

    void OnSawingCompleted()
    {
        if (_sawingCompleted || runner == null || runner.Process != ProcessId.Sawing) return;

        _sawingCompleted = true;
        ProcessSignalBus.Add(SawingSignal);
        Debug.Log("[MainPlay] Sawing 작업 완료", sawingZone);
    }

    void BindAssemblyTargets()
    {
        for (var i = 0; i < assemblyTargets.Count; i++)
        {
            var target = assemblyTargets[i];
            if (target == null || target.Snap == null || _assemblyListeners.ContainsKey(target)) continue;

            Action<Grabbable> listener = part => OnAssemblyCompleted(target, part);
            _assemblyListeners.Add(target, listener);
            target.Snap.Assembled += listener;
        }
    }

    void SynchronizeAssemblyState(ProcessId process)
    {
        if (process is not ProcessId.PurlinInstall and not ProcessId.GongpoPuzzle) return;

        for (var i = 0; i < assemblyTargets.Count; i++)
        {
            var target = assemblyTargets[i];
            if (target == null || target.Snap == null || !target.Snap.IsOccupied) continue;
            OnAssemblyCompleted(target, target.Snap.Occupant);
        }
    }

    void OnAssemblyCompleted(AssemblyTarget target, Grabbable part)
    {
        if (target == null || part == null || runner == null) return;

        var male = part.GetComponentInChildren<MaleSnapPoint>();
        if (male == null) return;

        var isPurlin = string.Equals(male.mySnapID, PurlinPartId, StringComparison.Ordinal);
        var expectedProcess = isPurlin ? ProcessId.PurlinInstall : ProcessId.GongpoPuzzle;
        if (runner.Process != expectedProcess || !_completedAssemblyTargets.Add(target)) return;

        if (isPurlin)
        {
            _completedPurlinCount++;
            ProcessSignalBus.Add(PurlinInstallSignal);
            Debug.Log($"[MainPlay] 도리 설치 {_completedPurlinCount}/1", part);
            return;
        }

        _completedGongpoCount++;
        ProcessSignalBus.Add(GongpoPuzzleSignal);
        Debug.Log($"[MainPlay] 공포 조립 {_completedGongpoCount}/{GongpoRequiredPartCount}", part);
    }

    void Update()
    {
        if (!enableDesktopPlaceholders || runner == null || !runner.IsRunning ||
            PauseService.IsPaused || runner.Process == ProcessId.Makmeok ||
            runner.Process == ProcessId.Sawing || runner.Process == ProcessId.PurlinInstall ||
            runner.Process == ProcessId.GongpoPuzzle)
            return;

        var input = UserInput.Instance;
        if (input == null || !input.GetKeyDown(completePlaceholderKey)) return;

        var key = PlaceholderSignal(runner.Process);
        if (!string.IsNullOrEmpty(key)) ProcessSignalBus.Add(key);
    }

    public static string PlaceholderSignal(ProcessId process) => process switch
    {
        ProcessId.Chiseling => "main.placeholder.chiseling",
        _ => null
    };

    public static string SignalForProcess(ProcessId process) => process switch
    {
        ProcessId.Makmeok => MakmeokSignal,
        ProcessId.Sawing => SawingSignal,
        ProcessId.Chiseling => PlaceholderSignal(ProcessId.Chiseling),
        ProcessId.PurlinInstall => PurlinInstallSignal,
        ProcessId.GongpoPuzzle => GongpoPuzzleSignal,
        _ => null
    };

    void OnGUI()
    {
        if (!showOverlay || runner == null || runner.Process == ProcessId.Makmeok ||
            runner.Process >= ProcessId.Ending)
            return;

        GUI.Box(new Rect(10f, 132f, 620f, 56f), string.Empty);

        if (runner.Process == ProcessId.Sawing)
        {
            var required = sawingZone != null ? Mathf.Max(1, sawingZone.requiredStrokes) : 1;
            var completed = sawingZone != null
                ? Mathf.Clamp(Mathf.RoundToInt(sawingZone.progress * required), 0, required)
                : 0;
            GUI.Label(new Rect(20f, 140f, 600f, 22f),
                $"Sawing: 대패 스트로크 {completed}/{required}");
            GUI.Label(new Rect(20f, 162f, 600f, 22f),
                "대패를 PlaneZone의 지정 방향으로 길게 밀어 주세요.");
            return;
        }

        if (runner.Process == ProcessId.PurlinInstall)
        {
            GUI.Label(new Rect(20f, 140f, 600f, 22f),
                $"PurlinInstall: 도리 설치 {_completedPurlinCount}/1");
            GUI.Label(new Rect(20f, 162f, 600f, 22f),
                "1floor 부재를 열린 결합 위치에 맞춘 뒤 잡기를 놓아 주세요.");
            return;
        }

        if (runner.Process == ProcessId.GongpoPuzzle)
        {
            GUI.Label(new Rect(20f, 140f, 600f, 22f),
                $"GongpoPuzzle: 공포 조립 {_completedGongpoCount}/{GongpoRequiredPartCount}");
            GUI.Label(new Rect(20f, 162f, 600f, 22f),
                "열린 결합 위치와 ID가 맞는 부재를 아래에서 위 순서로 조립하세요.");
            return;
        }

        GUI.Label(new Rect(20f, 140f, 600f, 22f),
            $"{runner.Process}: 실제 제작 도구 연결 전 임시 검증 단계");
        GUI.Label(new Rect(20f, 162f, 600f, 22f),
            $"{completePlaceholderKey} 현재 공정 완료 신호");
    }
}
