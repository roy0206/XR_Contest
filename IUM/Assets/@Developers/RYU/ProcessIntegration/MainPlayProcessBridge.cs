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
    const string PurlinPartId = "1floor";
    const int GongpoRequiredPartCount = 37;

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
    UnityAction _sawingListener;
    int _completedInkLineCount;
    int _completedPurlinCount;
    int _completedGongpoCount;
    bool _sawingCompleted;
    bool _started;

    void Awake()
    {
        if (runner == null) runner = FindAnyObjectByType<ProcessRunner>();
        if (sawingZone == null) sawingZone = FindAnyObjectByType<PlaneZone>();
        assemblyTargets ??= new List<AssemblyTarget>();
        if (assemblyTargets.Count == 0)
            assemblyTargets.AddRange(FindObjectsByType<AssemblyTarget>(FindObjectsSortMode.None));
    }

    void OnEnable()
    {
        ProcessSignalBus.Reset(MakmeokSignal);
        ProcessSignalBus.Reset(SawingSignal);
        ProcessSignalBus.Reset(PlaceholderSignal(ProcessId.Chiseling));
        ProcessSignalBus.Reset(PurlinInstallSignal);
        ProcessSignalBus.Reset(GongpoPuzzleSignal);

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

        if (_started) BindAssemblyTargets();
    }

    void Start()
    {
        _started = true;
        BindAssemblyTargets();
    }

    void OnDisable()
    {
        foreach (var pair in _inkListeners)
            if (pair.Key != null) pair.Key.OnWorkCompleted.RemoveListener(pair.Value);

        if (sawingZone != null && _sawingListener != null)
            sawingZone.OnWorkCompleted.RemoveListener(_sawingListener);

        foreach (var pair in _assemblyListeners)
            if (pair.Key != null && pair.Key.Snap != null)
                pair.Key.Snap.Assembled -= pair.Value;

        _inkListeners.Clear();
        _assemblyListeners.Clear();
        _completedAssemblyTargets.Clear();
        _sawingListener = null;
        _completedInkLineCount = 0;
        _completedPurlinCount = 0;
        _completedGongpoCount = 0;
        _sawingCompleted = false;
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

            if (target.Snap.IsOccupied)
                OnAssemblyCompleted(target, target.Snap.Occupant);
        }
    }

    void OnAssemblyCompleted(AssemblyTarget target, Grabbable part)
    {
        if (target == null || part == null || !_completedAssemblyTargets.Add(target)) return;

        var male = part.GetComponentInChildren<MaleSnapPoint>();
        if (male != null && string.Equals(male.mySnapID, PurlinPartId, StringComparison.Ordinal))
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
