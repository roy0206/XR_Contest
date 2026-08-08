using System;
using UnityEngine;

/// <summary>
/// 일시정지 메뉴 (F-019). Owns the pause state and the menu's actions; <see cref="PauseMenuView"/>
/// draws it and calls back in.
///
/// Reads its own input for the same reason <see cref="CutsceneDirector"/> does: pausing must work
/// while every other input is locked, and in scenes that have no player rig at all. Device mapping
/// still lives in the input sources, so no XR API is touched here.
/// </summary>
public sealed class PauseController : Singleton<PauseController>, ISceneEventListener
{
    [Tooltip("메인 화면으로 돌아갈 때 이동할 씬. Build Settings에 등록되어 있어야 한다.")]
    [SerializeField] string mainSceneName = "StartScene";

    [Tooltip("컷씬 재생 중에도 일시정지를 허용할지. 문서상 허용이 기본이다 (F-003 3.4).")]
    [SerializeField] bool allowDuringCutscene = true;

    readonly DesktopInputSource _desktop = new();
    readonly XRInputSource _xr = new();

    PlayerCommands _commands;
    IDisposable _inputLock;

    public bool IsOpen => PauseService.IsPaused;

    /// <summary>True when the menu should be on screen.</summary>
    public event Action<bool> VisibilityChanged;

    /// <summary>
    /// 공정 다시 시작 (F-019 2.3). Raised for whichever process system is running to act on; this
    /// class deliberately knows nothing about process state.
    /// </summary>
    public event Action ProcessRestartRequested;

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;

        SceneController.Instance?.RegisterListener(this);
    }

    void Update()
    {
        _commands.Clear();
        IPlayerInputSource source = _xr.IsAvailable ? _xr : _desktop;
        source.Read(ref _commands);

        if (_commands.Pause) Toggle();
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;

        // Never during a scene transition. SceneController's loading loop runs on unscaled time and
        // would carry timeScale zero into the next scene, where OnSceneLoadStart — the callback that
        // closes this menu — has already fired and cannot save it.
        if (SceneController.IsTransitioning) return;

        if (!allowDuringCutscene && CutsceneDirector.HasInstance && CutsceneDirector.Instance.IsPlaying)
            return;

        // Acquired on top of whatever a cutscene already holds. The counting in InputLockService is
        // what makes that safe: closing the menu releases only this hold.
        _inputLock = InputLockService.Acquire(InputLockFlags.Menu, "일시정지");
        PauseService.SetPaused(true);
        VisibilityChanged?.Invoke(true);
    }

    public void Close()
    {
        if (!IsOpen) return;

        PauseService.SetPaused(false);
        ReleaseLock();
        VisibilityChanged?.Invoke(false);
    }

    /// <summary>
    /// 공정 다시 시작. Closes the menu first so the process system resumes into a running game
    /// rather than a frozen one.
    /// </summary>
    public void RequestProcessRestart()
    {
        Close();

        if (ProcessRestartRequested == null)
        {
            Debug.LogWarning("[Pause] 공정 다시 시작을 받을 시스템이 아직 없습니다.");
            return;
        }

        ProcessRestartRequested.Invoke();
    }

    /// <summary>
    /// 메인 화면으로 (F-019 2.4). 마지막으로 완료한 공정까지만 저장되고 현재 공정의 부분 작업은
    /// 폐기된다 — 진행 저장이 공정 완료 시점에만 일어나므로 별도 처리 없이 그렇게 된다.
    /// </summary>
    public void ReturnToMainMenu()
    {
        // Unpaused before the load so the next scene does not start with timeScale at zero.
        Close();

        if (!Application.CanStreamedLevelBeLoaded(mainSceneName))
        {
            Debug.LogError($"[Pause] 메인 화면 씬 '{mainSceneName}'을 Build Settings에서 찾을 수 없습니다.");
            return;
        }

        SceneController.Instance.LoadScene(mainSceneName);
    }

    void ReleaseLock()
    {
        _inputLock?.Dispose();
        _inputLock = null;
    }

    public void OnSceneLoadStart(string sceneName)
    {
        // A scene change while paused would carry timeScale zero into the next scene.
        if (IsOpen) Close();
    }

    public void OnSceneLoadComplete(string sceneName) { }

    protected override void OnDestroy()
    {
        if (SceneController.TryGetInstance(out var controller))
            controller.UnregisterListener(this);

        ReleaseLock();
        if (PauseService.IsPaused) PauseService.Reset();
        base.OnDestroy();
    }
}
