using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 한 공정의 목적지. 씬이거나 컷씬이며, 둘 다 비어 있으면 아직 만들지 않은 공정이다.
/// </summary>
[Serializable]
public sealed class FlowEntry
{
    public ProcessId Process { get; set; }

    /// <summary>Scene to load. Ignored when <see cref="Cutscene"/> is set.</summary>
    public string Scene { get; set; }

    /// <summary>Cutscene id from cutscene.json. Takes precedence over <see cref="Scene"/>.</summary>
    public string Cutscene { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Scene) && string.IsNullOrWhiteSpace(Cutscene);
}

/// <summary>
/// 공정별 목적지 표. 메인 컨텐츠는 아직 비어 있고, 공정이 생길 때마다 한 줄씩 채운다.
/// </summary>
[Serializable]
public sealed class FlowTable
{
    public List<FlowEntry> Entries { get; set; } = new();

    public static FlowTable CreateEmpty() => new();

    public bool TryGet(ProcessId process, out FlowEntry entry)
    {
        entry = null;
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i] == null || Entries[i].Process != process) continue;
            entry = Entries[i];
            return !entry.IsEmpty;
        }

        return false;
    }
}

/// <summary>
/// 진행 흐름 (F-001). Turns 저장된 진행 상태 into a destination and sends the player there.
///
/// The single place that reads <see cref="UserProgressData.NextProcess"/> to decide where to go.
/// A cutscene records which process it finished and nothing more; deciding what follows belongs
/// here, because only this knows the whole order. Splitting that across cutscene data as well would
/// mean two files naming the same destination.
///
/// 메인 컨텐츠 공정은 아직 flow.json에 없다. 목적지가 없는 공정으로 가려 하면 진행을 멈추고
/// <see cref="DestinationMissing"/>을 알린다 — 조용히 메인 화면에 머무는 것보다 낫다.
/// </summary>
public sealed class GameFlow : Singleton<GameFlow>
{
    /// <summary>Static data key registered in manifest.json.</summary>
    public const string DataKey = "flow";

    FlowTable _table;
    Task _initialization;
    CutsceneDirector _director;

    public bool IsReady { get; private set; }

    /// <summary>True when a save exists to continue from (F-001 1.5).</summary>
    public bool CanContinue =>
        DataManager.HasInstance && DataManager.Instance.IsReady &&
        DataManager.Instance.Progress.HasSaveData;

    /// <summary>Raised when a process has no destination yet. Argument is that process.</summary>
    public event Action<ProcessId> DestinationMissing;

    /// <summary>Raised just before the player is sent somewhere.</summary>
    public event Action<ProcessId> Entering;

    public event Action Ready;

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;
        _ = InitializeAsync();
    }

    public Task InitializeAsync() => _initialization ??= InitializeInternalAsync();

    async Task InitializeInternalAsync()
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            if (DataManager.Instance.Static != null &&
                DataManager.Instance.Static.TryGet<FlowTable>(DataKey, out var table) &&
                table != null)
                _table = table;
            else
                Debug.LogWarning($"[Flow] 정적 데이터 '{DataKey}'가 없어 빈 흐름표로 시작합니다.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Flow] 흐름 데이터를 불러오지 못했습니다: {exception.Message}");
        }

        _table ??= FlowTable.CreateEmpty();
        _table.Entries ??= new List<FlowEntry>();

        SubscribeToCutscenes();

        IsReady = true;
        Ready?.Invoke();
    }

    /// <summary>
    /// Listens for a cutscene that reports a finished process so the next destination follows
    /// automatically. 프롤로그가 끝나면 튜토리얼로 넘어가는 것이 이 경로다.
    /// </summary>
    void SubscribeToCutscenes()
    {
        _director = CutsceneDirector.Instance;
        if (_director == null) return;
        _director.Finished += OnCutsceneFinished;
    }

    void OnCutsceneFinished(CutsceneDefinition definition, bool completed)
    {
        if (definition == null) return;

        // 클리어. Wiping here rather than in the director keeps every progress write in one class.
        // The director raises this before loading nextScene, so the main menu that follows already
        // reads the cleared state and shows 이어하기 disabled.
        if (definition.ClearsProgress)
        {
            ClearProgress();
            return;
        }

        // A cutscene with no process to record routes itself, if at all, through its own nextScene.
        if (definition.CompletesProcess == null) return;

        // Skipped still counts: 건너뛰기 records progress too (F-003 3.5), so the player must not
        // be left sitting on the scene the cutscene played over.
        GoToCurrent();
    }

    /// <summary>
    /// 클리어 후 진행 삭제. 옵션 데이터는 건드리지 않는다 — <see cref="UserProgressData.Reset"/>이
    /// 진행만 되돌린다.
    /// </summary>
    void ClearProgress()
    {
        if (!DataManager.HasInstance || !DataManager.Instance.IsReady) return;

        DataManager.Instance.Progress.Reset();
        _ = SaveAsync();
    }

    /// <summary>
    /// 새 게임 (F-001 1.4). Clears progress, saves immediately, then enters the first process.
    /// 옵션 데이터는 초기화하지 않는다.
    /// </summary>
    public async Task StartNewGameAsync()
    {
        if (!await EnsureDataAsync()) return;

        DataManager.Instance.Progress.Reset();

        // Saved before entering rather than after: quitting during the prologue must not leave the
        // previous playthrough's progress on disk.
        await SaveAsync();

        GoTo(ProcessId.Prologue);
    }

    /// <summary>
    /// 이어하기 (F-001 1.5). 마지막으로 완료하지 못한 공정의 처음부터 다시 시작한다 — 작업 도중의
    /// 세부 가공 상태는 복원하지 않는다.
    /// </summary>
    public async Task ContinueAsync()
    {
        if (!await EnsureDataAsync()) return;
        GoToCurrent();
    }

    /// <summary>Sends the player to whatever <see cref="UserProgressData.NextProcess"/> points at.</summary>
    public void GoToCurrent()
    {
        if (!DataManager.HasInstance || !DataManager.Instance.IsReady) return;
        GoTo(DataManager.Instance.Progress.NextProcess);
    }

    public bool GoTo(ProcessId process)
    {
        if (!IsReady)
        {
            Debug.LogWarning($"[Flow] 초기화 전에 '{process}' 진입을 요청했습니다.");
            return false;
        }

        if (!_table.TryGet(process, out var entry))
        {
            // Expected while the main content is unbuilt, so this reports rather than throws.
            Debug.LogWarning($"[Flow] '{process}'의 목적지가 flow.json에 없습니다. 아직 만들지 않은 공정입니다.");
            DestinationMissing?.Invoke(process);
            return false;
        }

        Entering?.Invoke(process);

        if (!string.IsNullOrWhiteSpace(entry.Cutscene))
            return PlayCutscene(entry.Cutscene, process);

        return LoadScene(entry.Scene, process);
    }

    bool PlayCutscene(string cutsceneId, ProcessId process)
    {
        var director = CutsceneDirector.Instance;
        if (director == null || !director.IsReady)
        {
            Debug.LogWarning($"[Flow] 컷씬 시스템이 준비되지 않아 '{cutsceneId}'를 재생하지 못했습니다.");
            DestinationMissing?.Invoke(process);
            return false;
        }

        if (director.Play(cutsceneId)) return true;

        DestinationMissing?.Invoke(process);
        return false;
    }

    bool LoadScene(string sceneName, ProcessId process)
    {
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[Flow] 씬 '{sceneName}'을 Build Settings에서 찾을 수 없습니다.");
            DestinationMissing?.Invoke(process);
            return false;
        }

        SceneController.Instance.LoadScene(sceneName);
        return true;
    }

    async Task<bool> EnsureDataAsync()
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            await InitializeAsync();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Flow] 데이터 준비에 실패했습니다: {exception.Message}");
            return false;
        }
    }

    static async Task SaveAsync()
    {
        try
        {
            await DataManager.Instance.SaveUserAsync();
        }
        catch (Exception exception)
        {
            // Never fatal: the reset is already applied in memory, so play continues and the write
            // is retried the next time progress changes.
            Debug.LogError($"[Flow] 진행 저장에 실패했습니다: {exception.Message}");
        }
    }

    protected override void OnDestroy()
    {
        if (_director != null)
        {
            _director.Finished -= OnCutsceneFinished;
            _director = null;
        }

        base.OnDestroy();
    }
}
