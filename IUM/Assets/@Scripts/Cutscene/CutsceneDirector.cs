using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 컷씬 재생. Overlays a cutscene scene on top of the running one and holds the frame while it
/// plays: 이동·잡기·상호작용·이음이 질문이 모두 잠기고 공정 판정도 닫힌다 (F-003 3.4).
///
/// The performance itself belongs to the loaded scene's <see cref="CutsceneStage"/>. This class
/// only owns what a stage cannot: the input locks, 건너뛰기, 진행 저장, and bringing the scene in
/// and out behind a blackout. Keeping the main scene loaded means the player's position, held
/// parts and process state survive the cutscene untouched.
///
/// Phases advance from <see cref="Update"/> rather than a coroutine so that stopping the tick stops
/// the cutscene — WaitForSecondsRealtime and Task.Delay both keep running through a paused game.
/// Only scene loading is asynchronous, and it resolves back into the same phase machine.
/// </summary>
public sealed class CutsceneDirector : Singleton<CutsceneDirector>, ISceneEventListener
{
    /// <summary>Static data key registered in manifest.json.</summary>
    public const string DataKey = "cutscene";

    enum Phase
    {
        Idle,

        /// <summary>Blacking out before the cutscene scene is brought in.</summary>
        EnterFade,

        /// <summary>Scene load in flight. Advanced by the load task, not the clock.</summary>
        Loading,

        /// <summary>Fading back in with the stage already begun.</summary>
        RevealFade,

        Playing,

        /// <summary>Blacking out before the scene is unloaded.</summary>
        ExitFade,

        /// <summary>Scene unload in flight.</summary>
        Unloading
    }

    /// <summary>
    /// Supplies the TTS service for cutscene lines with no recording. Same contract as
    /// <see cref="InGameDialogue.TextToSpeechResolver"/>; null keeps playback on recorded audio.
    /// </summary>
    public static Func<DialogueSpeaker, IAiTextToSpeechService> TextToSpeechResolver { get; set; }

    // Own input sources instead of reading PlayerCommands from the player rig: 건너뛰기 is the one
    // input that must survive InputLockFlags.Cutscene, and a cutscene has to be playable in a scene
    // that has no player at all. Device mapping still lives in the sources, so no XR API is touched
    // here.
    readonly DesktopInputSource _desktop = new();
    readonly XRInputSource _xr = new();

    CutsceneTable _table;
    DialogueTable _dialogueTable;
    DialoguePlayer _player;
    DialogueVoiceLibrary _voices;
    CutsceneContext _context;
    Task _initialization;

    CutsceneDefinition _definition;
    CutsceneStage _stage;
    CutsceneVideoSurface _videoSurface;
    Camera _suspendedCamera;
    Scene _scene;
    Phase _phase = Phase.Idle;
    float _phaseEndTime;
    bool _skipped;

    IDisposable _inputLock;
    IDisposable _processHold;

    PlayerCommands _commands;
    float _skipHeld;
    float _skipProgress;

    public bool IsReady { get; private set; }
    public bool IsPlaying => _phase != Phase.Idle;
    public CutsceneDefinition Current => _definition;
    public float SkipProgress => _skipProgress;

    /// <summary>
    /// 진입 암전 길이. 주 씬의 화면 고정 UI가 같은 시간에 걸쳐 물러나는 데 쓴다 — UIToolkit 패널은
    /// <see cref="ScreenFader"/>의 캔버스 위에 그려지므로 암전이 그것들을 덮지 못한다.
    /// </summary>
    public float EnterFadeSeconds => _table?.Settings?.EnterFadeSeconds ?? 0f;

    /// <summary>뒤에 아무것도 없을 때 화면을 되돌리는 길이. 같은 UI가 돌아오는 속도이기도 하다.</summary>
    public float OutroFadeSeconds => _table?.Settings?.OutroFadeSeconds ?? 0f;

    /// <summary>Diagnostic label for the debug overlay.</summary>
    public string PhaseName => _phase.ToString();

    /// <summary>Empty text hides the caption.</summary>
    public event Action<string> CaptionChanged;

    /// <summary>0 to 1. Zero means the 건너뛰기 게이지 should be hidden.</summary>
    public event Action<float> SkipProgressChanged;

    /// <summary>Routed to the same 자막 view the in-game dialogue uses.</summary>
    public event Action<DialogueSpeaker, string> SubtitleChanged;

    public event Action<CutsceneDefinition> Started;

    /// <summary>Second argument is false when the cutscene was skipped or aborted.</summary>
    public event Action<CutsceneDefinition, bool> Finished;

    public event Action Ready;

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;

        SceneController.Instance?.RegisterListener(this);
        _ = InitializeAsync();
    }

    public Task InitializeAsync() => _initialization ??= InitializeInternalAsync();

    async Task InitializeInternalAsync()
    {
        _table = await LoadAsync<CutsceneTable>(DataKey) ?? CutsceneTable.CreateEmpty();
        _table.Prepare();

        // Loaded separately from InGameDialogue on purpose. StaticDataService deserializes on every
        // call, so sharing the instance is not on the table anyway, and this keeps a cutscene
        // playable in a scene where no InGameDialogue exists.
        _dialogueTable = await LoadAsync<DialogueTable>(InGameDialogue.DataKey) ?? DialogueTable.CreateEmpty();
        _dialogueTable.Prepare();

        _voices = new DialogueVoiceLibrary(speaker => TextToSpeechResolver?.Invoke(speaker));
        _player = new DialoguePlayer(transform, _dialogueTable.Settings, _voices);
        _player.SubtitleChanged += OnSubtitleChanged;

        _context = new CutsceneContext(_player, ResolveDialogue, RaiseCaption);

        IsReady = true;
        Ready?.Invoke();
    }

    /// <summary>Missing data is not fatal; both entries are optional in the manifest.</summary>
    async Task<T> LoadAsync<T>(string key) where T : class
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            if (DataManager.Instance.Static != null &&
                DataManager.Instance.Static.TryGet<T>(key, out var table) &&
                table != null)
                return table;

            Debug.LogWarning($"[Cutscene] 정적 데이터 '{key}'가 없습니다.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Cutscene] '{key}' 로드 실패: {exception.Message}");
        }

        return null;
    }

    DialogueSequence ResolveDialogue(string sequenceId) =>
        _dialogueTable != null && _dialogueTable.TryGet(sequenceId, out var sequence) ? sequence : null;

    /// <summary>Plays a cutscene by id. Returns false when the id is unknown or data is not ready.</summary>
    public bool Play(string cutsceneId)
    {
        if (!IsReady)
        {
            Debug.LogWarning($"[Cutscene] 초기화 전에 '{cutsceneId}' 재생을 요청했습니다.");
            return false;
        }

        if (!_table.TryGet(cutsceneId, out var definition))
        {
            Debug.LogWarning($"[Cutscene] 컷씬 '{cutsceneId}'를 찾을 수 없습니다.");
            return false;
        }

        return PlayDefinition(definition);
    }

    public bool PlayDefinition(CutsceneDefinition definition)
    {
        if (!IsReady || definition == null) return false;

        if (!definition.IsValid)
        {
            Debug.LogWarning($"[Cutscene] '{definition.Id}'에 씬 이름도 영상 경로도 없습니다.");
            return false;
        }

        if (IsPlaying)
        {
            Debug.LogWarning($"[Cutscene] '{_definition?.Id}' 재생 중이라 '{definition.Id}'를 무시했습니다.");
            return false;
        }

        _definition = definition;
        _skipped = false;
        _skipHeld = 0f;
        _context.CutsceneId = definition.Id;

        _inputLock = InputLockService.Acquire(InputLockFlags.Cutscene, $"컷씬:{definition.Id}");
        _processHold = ProcessGate.Hold($"컷씬:{definition.Id}");

        SetSkipProgress(0f);
        EnterPhase(Phase.EnterFade, _table.Settings.EnterFadeSeconds);
        ScreenFader.Instance?.FadeOut(_table.Settings.EnterFadeSeconds);

        Started?.Invoke(definition);
        return true;
    }

    /// <summary>
    /// Aborts without the 건너뛰기 blackout. Used by scene teardown, so it deliberately neither
    /// records progress nor triggers the follow-on scene: the run did not reach its end.
    /// </summary>
    public void Stop()
    {
        if (_phase == Phase.Idle) return;

        _stage?.Cancel();
        _player?.Stop();
        _videoSurface?.Stop();
        RaiseCaption(string.Empty);

        // Fire and forget: an abort usually comes from a scene change that unloads this anyway.
        var scene = _scene;
        Release();
        Finished?.Invoke(_definition, false);
        _definition = null;

        if (scene.IsValid() && scene.isLoaded)
            _ = SceneController.Instance?.UnloadAdditiveAsync(scene);
    }

    void Update()
    {
        // 일시정지 중에는 아무것도 진행하지 않는다 (F-019). Phase timing already stops on its own
        // because it runs on PauseService.Now, but 건너뛰기 does not — Skip is deliberately outside
        // InputLockFlags, so holding the button behind an open menu would otherwise fill the gauge.
        if (PauseService.IsPaused) return;

        _player?.Tick();
        if (_phase == Phase.Idle) return;

        var now = PauseService.Now;

        switch (_phase)
        {
            case Phase.EnterFade:
                if (now >= _phaseEndTime) BeginLoad();
                break;

            case Phase.RevealFade:
                if (now >= _phaseEndTime) _phase = Phase.Playing;
                break;

            case Phase.Playing:
                TickSkipInput(Time.unscaledDeltaTime);
                if (_phase == Phase.Playing && IsPerformanceOver()) BeginExit(false);
                break;

            case Phase.ExitFade:
                if (now >= _phaseEndTime) BeginUnload();
                break;

            // Loading and Unloading advance from their tasks.
        }
    }

    /// <summary>
    /// 누가 끝을 정하는가. 씬 연출이 있으면 스테이지가, 영상뿐이면 영상이 정한다. 둘 다 없는
    /// 조합은 재생할 것이 없다는 뜻이므로 즉시 끝난 것으로 본다.
    /// </summary>
    bool IsPerformanceOver()
    {
        if (_stage != null) return _stage.IsFinished;
        if (_videoSurface != null && _videoSurface.IsActive) return _videoSurface.IsFinished;
        return true;
    }

    void EnterPhase(Phase phase, float seconds)
    {
        _phase = phase;
        _phaseEndTime = PauseService.Now + Mathf.Max(0f, seconds);
    }

    void BeginLoad()
    {
        _phase = Phase.Loading;
        _ = LoadContentAsync();
    }

    /// <summary>
    /// 컷씬이 재생할 것을 준비한다. 영상, 씬, 또는 둘 다 — 어느 쪽이든 준비가 끝나면 같은 위상
    /// 기계로 돌아온다.
    /// </summary>
    async Task LoadContentAsync()
    {
        var definition = _definition;

        if (definition.HasVideo && !await PrepareVideoAsync(definition)) return;
        if (!string.IsNullOrWhiteSpace(definition.Scene) && !await LoadStageAsync(definition)) return;

        // 씬 연출보다 뒤에 시작한다. 스테이지가 첫 캡션을 세우는 동안 영상이 흘러가면 도입부를
        // 놓친다. 표면은 컷씬 사이에 살아남으므로 이번 컷씬이 실제로 영상을 열었을 때만 재생한다.
        if (_videoSurface != null && _videoSurface.IsPrepared) _videoSurface.Play();

        if (!definition.RevealOnEnter)
        {
            _phase = Phase.Playing;
            return;
        }

        EnterPhase(Phase.RevealFade, _table.Settings.EnterFadeSeconds);
        ScreenFader.Instance?.FadeIn(_table.Settings.EnterFadeSeconds);
    }

    /// <summary>
    /// 영상을 첫 프레임까지 열어 둔다. 재생은 씬까지 준비된 뒤에 시작한다 — 준비와 재생을 나누지
    /// 않으면 씬 로드가 걸리는 동안 영상만 앞서 나간다.
    ///
    /// False는 호출자가 이미 컷씬을 접었거나 접었어야 한다는 뜻이다.
    /// </summary>
    async Task<bool> PrepareVideoAsync(CutsceneDefinition definition)
    {
        var prepared = false;

        try
        {
            prepared = await EnsureVideoSurface().PrepareAsync(definition.Video);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }

        // Stop() may have run while the preparation was in flight.
        if (_phase != Phase.Loading || !ReferenceEquals(_definition, definition)) return false;
        if (prepared) return true;

        Debug.LogError($"[Cutscene] '{definition.Id}'의 영상 '{definition.Video}'을 열지 못했습니다.");

        // 씬 연출이 함께 선언되어 있으면 그것만으로 이어간다. 영상은 이미 스스로 내려갔다.
        if (!string.IsNullOrWhiteSpace(definition.Scene)) return true;

        BeginExit(false);
        return false;
    }

    async Task<bool> LoadStageAsync(CutsceneDefinition definition)
    {
        try
        {
            _scene = await SceneController.Instance.LoadAdditiveAsync(definition.Scene, definition.MakeSceneActive);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            _scene = default;
        }

        // Stop() may have run while the load was in flight.
        if (_phase != Phase.Loading || !ReferenceEquals(_definition, definition)) return false;

        if (!_scene.IsValid() || !_scene.isLoaded)
        {
            Debug.LogError($"[Cutscene] '{definition.Id}'의 씬 '{definition.Scene}'을 올리지 못했습니다.");
            BeginExit(false);
            return false;
        }

        _stage = FindStage(_scene);
        if (_stage == null)
        {
            // 영상이 있으면 스테이지 없는 씬은 배경으로 성립한다. 끝나는 시점은 영상이 정한다.
            if (definition.HasVideo)
            {
                Debug.LogWarning(
                    $"[Cutscene] 씬 '{definition.Scene}'에 CutsceneStage가 없어 배경으로만 씁니다. " +
                    "종료 시점은 영상이 정합니다.");
                return true;
            }

            Debug.LogError(
                $"[Cutscene] 씬 '{definition.Scene}'에 CutsceneStage 컴포넌트가 없습니다. " +
                "연출을 시작할 수 없어 즉시 종료합니다.");
            BeginExit(false);
            return false;
        }

        TakeOverCamera();

        // Begun before the reveal so a stage can put its first caption up while the screen is
        // still black, which is what the prologue opens with.
        _stage.Begin(_context);
        return true;
    }

    /// <summary>
    /// 영상 표면을 한 번 만들어 계속 쓴다. 컷씬에 영상이 없으면 아예 만들어지지 않는다.
    /// </summary>
    CutsceneVideoSurface EnsureVideoSurface()
    {
        if (_videoSurface != null) return _videoSurface;

        var host = new GameObject(nameof(CutsceneVideoSurface));
        host.transform.SetParent(transform, false);
        _videoSurface = host.AddComponent<CutsceneVideoSurface>();
        return _videoSurface;
    }

    /// <summary>
    /// Swaps the main camera out for the stage's own. Done behind the blackout, and only when the
    /// stage actually declares a camera — a cutscene staged in place keeps the player's view.
    ///
    /// In VR this is a comfort risk: the viewpoint moves without the player moving. It is done
    /// while the screen is black for that reason, and remains a device verification item.
    /// </summary>
    void TakeOverCamera()
    {
        var stageCamera = _stage.StageCamera;
        if (stageCamera == null) return;

        // Read before enabling the stage camera so Camera.main still resolves to the main scene's.
        _suspendedCamera = Camera.main;
        if (_suspendedCamera != null && _suspendedCamera != stageCamera)
            _suspendedCamera.enabled = false;
        else
            _suspendedCamera = null;

        stageCamera.enabled = true;

        // ScreenFader draws through a ScreenSpaceCamera canvas bound to whatever camera was active.
        // Without this the blackout stops rendering the moment the main camera switches off.
        ScreenFader.Instance?.Rebind();
    }

    /// <summary>Hands the view back before the stage's camera is destroyed with its scene.</summary>
    void RestoreCamera()
    {
        if (_suspendedCamera == null) return;
        _suspendedCamera.enabled = true;
        _suspendedCamera = null;
        ScreenFader.Instance?.Rebind();
    }

    static CutsceneStage FindStage(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        for (var i = 0; i < roots.Length; i++)
        {
            // Inactive included: a stage may sit on an object the scene enables during setup.
            var stage = roots[i].GetComponentInChildren<CutsceneStage>(true);
            if (stage != null) return stage;
        }

        return null;
    }

    void BeginExit(bool skipped)
    {
        _skipped = skipped;

        _player?.Stop();

        // 암전이 시작되는 지점에서 함께 내린다. 페이드가 끝날 때까지 두면 건너뛰기를 눌러도
        // 소리가 계속 들린다.
        _videoSurface?.Stop();

        RaiseCaption(string.Empty);
        SetSkipProgress(0f);

        var seconds = skipped ? _table.Settings.SkipFadeSeconds : _table.Settings.ExitFadeSeconds;
        EnterPhase(Phase.ExitFade, seconds);
        ScreenFader.Instance?.FadeOut(seconds);
    }

    void BeginUnload()
    {
        _phase = Phase.Unloading;
        _ = UnloadStageAsync();
    }

    async Task UnloadStageAsync()
    {
        var definition = _definition;

        // Before the unload: the stage camera is about to be destroyed with its scene, and a frame
        // with no enabled camera at all would be a black flash if the fade were shorter.
        RestoreCamera();

        if (_scene.IsValid() && _scene.isLoaded)
        {
            try
            {
                await SceneController.Instance.UnloadAdditiveAsync(_scene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        if (_phase != Phase.Unloading || !ReferenceEquals(_definition, definition)) return;

        var skipped = _skipped;
        Release();
        _definition = null;

        SaveProgress(definition);
        Finished?.Invoke(definition, !skipped);

        if (!string.IsNullOrWhiteSpace(definition?.NextScene))
        {
            // Left black: SceneController runs its own transition from here.
            SceneController.Instance?.LoadScene(definition.NextScene);
            return;
        }

        // 진행을 기록한 컷씬은 NextScene 없이도 다음 목적지로 이어진다 — GameFlow가 위의 Finished에서
        // 라우팅한다. That transition owns the screen from here, and fading in would lift the
        // blackout in the middle of its load.
        if (SceneController.IsTransitioning) return;

        // Nothing follows, so the screen has to come back or the player is left staring at black.
        ScreenFader.Instance?.FadeIn(_table.Settings.OutroFadeSeconds);
    }

    void TickSkipInput(float delta)
    {
        if (_definition == null || !_definition.Skippable) return;

        _commands.Clear();
        IPlayerInputSource source = _xr.IsAvailable ? _xr : _desktop;
        source.Read(ref _commands);

        if (_commands.Skip) _skipHeld += delta;
        else _skipHeld = 0f;

        var progress = Mathf.Clamp01(_skipHeld / _table.Settings.SkipHoldSeconds);
        SetSkipProgress(progress);

        if (progress < 1f) return;

        // Cancelled before the fade rather than after, so the performance stops the moment the
        // player finishes the hold instead of playing on behind a blackout.
        _stage?.Cancel();
        BeginExit(true);
    }

    void SetSkipProgress(float value)
    {
        if (Mathf.Approximately(value, _skipProgress)) return;
        _skipProgress = value;
        SkipProgressChanged?.Invoke(value);
    }

    /// <summary>Clears the run's state. Does not touch the scene or raise events.</summary>
    void Release()
    {
        // Idempotent, so covering the abort path here costs nothing.
        RestoreCamera();
        _videoSurface?.Stop();

        _stage = null;
        _scene = default;
        _phase = Phase.Idle;
        _skipHeld = 0f;
        _skipped = false;

        _inputLock?.Dispose();
        _inputLock = null;
        _processHold?.Dispose();
        _processHold = null;
    }

    /// <summary>
    /// Recorded even when skipped: 건너뛰기 is the player saying they have seen it, and the
    /// document only asks that completion be stored (F-003 3.5).
    /// </summary>
    void SaveProgress(CutsceneDefinition definition)
    {
        if (definition?.CompletesProcess == null) return;
        if (!DataManager.HasInstance || !DataManager.Instance.IsReady) return;

        DataManager.Instance.Progress.Complete(definition.CompletesProcess.Value);
        _ = DataManager.Instance.SaveUserAsync();
    }

    void RaiseCaption(string text) => CaptionChanged?.Invoke(text);

    void OnSubtitleChanged(DialogueSpeaker speaker, string text) => SubtitleChanged?.Invoke(speaker, text);

    public void OnSceneLoadStart(string sceneName)
    {
        Stop();
        _voices?.ReleaseAll();
    }

    public void OnSceneLoadComplete(string sceneName) { }

    protected override void OnDestroy()
    {
        if (SceneController.TryGetInstance(out var controller))
            controller.UnregisterListener(this);

        _stage?.Cancel();
        Release();
        _player?.Dispose();
        _voices?.ReleaseAll();
        base.OnDestroy();
    }
}
