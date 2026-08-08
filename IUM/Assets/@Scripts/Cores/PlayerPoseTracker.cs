using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 플레이어 위치를 진행 데이터에 싣고 되돌린다 (F-018). 이어하기는 공정의 처음부터 다시 시작하지만,
/// 자유롭게 돌아다니는 구간까지 입구로 되돌리면 어디까지 갔었는지가 사라진다.
///
/// 자기 씬 이름으로 저장하고 자기 씬 이름일 때만 되돌린다. 씬마다 하나씩 두면 되고, 저장된 좌표가
/// 다른 씬에서 적용되는 일은 없다.
///
/// 저장 시점은 셋이다. 씬을 떠날 때, 애플리케이션이 내려갈 때, 그리고 일정 간격의 자동 저장이다.
/// 앞의 둘만으로는 에디터 정지 버튼에 아무것도 남지 않는다 — 그때는 <see cref="OnApplicationQuit"/>가
/// 부른 비동기 쓰기가 끝나기 전에 플레이 모드가 사라진다.
/// </summary>
public sealed class PlayerPoseTracker : MonoBehaviour, ISceneEventListener
{
    [SerializeField] Player player;

    [Header("Auto Save")]
    [Tooltip("0이면 자동 저장을 끈다. 에디터 정지에 대비한 마지막 방어선이다.")]
    [SerializeField, Min(0f)] float autoSaveInterval = 5f;

    [Tooltip("이 거리만큼 움직였을 때만 다시 쓴다. 제자리에서의 반복 저장을 막는다.")]
    [SerializeField, Min(0f)] float moveThreshold = 0.25f;

    [SerializeField] bool restoreOnStart = true;

    float _nextAutoSave;
    Vector3 _lastSaved;
    bool _hasLastSaved;

    void Awake()
    {
        if (player == null) player = FindAnyObjectByType<Player>(FindObjectsInactive.Include);
        if (player == null)
        {
            Debug.LogWarning("[Pose] 씬에서 Player를 찾지 못해 위치 저장을 끕니다.", this);
            enabled = false;
            return;
        }

        SceneController.Instance?.RegisterListener(this);
    }

    void Start() => _ = RestoreAsync();

    /// <summary>
    /// 저장된 자리로 되돌린다. Waits for the data layer because a scene loaded straight from the
    /// editor reaches Start long before the user file has been read.
    /// </summary>
    async Task RestoreAsync()
    {
        if (!restoreOnStart) return;

        try
        {
            await DataManager.Instance.InitializeAsync();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Pose] 데이터를 준비하지 못해 위치를 복원하지 않습니다: {exception.Message}");
            return;
        }

        if (this == null || player == null) return;

        var pose = DataManager.Instance.Progress.PlayerPose;
        if (pose == null || !pose.Matches(gameObject.scene.name)) return;

        Apply(pose);
        _lastSaved = player.transform.position;
        _hasLastSaved = true;
    }

    void Apply(PlayerPoseData pose)
    {
        var controller = player.Controller;

        // CharacterController keeps its own copy of the position and writes it back over the
        // transform, so it has to be off for the move to survive the next frame.
        var wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;

        player.transform.SetPositionAndRotation(pose.Position, Quaternion.Euler(0f, pose.Yaw, 0f));

        if (wasEnabled) controller.enabled = true;
    }

    void Update()
    {
        if (autoSaveInterval <= 0f || player == null) return;
        if (PauseService.IsPaused) return;
        if (CutsceneDirector.HasInstance && CutsceneDirector.Instance.IsPlaying) return;

        var now = PauseService.Now;
        if (now < _nextAutoSave) return;
        _nextAutoSave = now + autoSaveInterval;

        var position = player.transform.position;
        if (_hasLastSaved && (position - _lastSaved).sqrMagnitude < moveThreshold * moveThreshold) return;

        _lastSaved = position;
        _hasLastSaved = true;
        CaptureAndSave();
    }

    /// <summary>현재 자세를 진행 데이터에 쓴다. 저장은 하지 않는다.</summary>
    public void Capture()
    {
        if (player == null) return;
        if (!DataManager.HasInstance || !DataManager.Instance.IsReady) return;

        var progress = DataManager.Instance.Progress;

        // 진행이 없으면 기억할 자리도 없다. This also keeps the wipe at the end of the game from
        // being undone by the scene change that immediately follows it.
        if (!progress.HasSaveData) return;

        progress.PlayerPose ??= new PlayerPoseData();

        var pose = progress.PlayerPose;
        pose.Scene = gameObject.scene.name;
        pose.Position = player.transform.position;
        pose.Yaw = player.transform.eulerAngles.y;
    }

    public void CaptureAndSave()
    {
        Capture();
        if (!DataManager.HasInstance || !DataManager.Instance.IsReady) return;
        _ = SaveAsync();
    }

    static async Task SaveAsync()
    {
        try
        {
            await DataManager.Instance.SaveUserAsync();
        }
        catch (Exception exception)
        {
            // Never fatal: the pose is already in memory and the next write will carry it.
            Debug.LogError($"[Pose] 위치 저장에 실패했습니다: {exception.Message}");
        }
    }

    public void OnSceneLoadStart(string sceneName) => CaptureAndSave();

    public void OnSceneLoadComplete(string sceneName) { }

    void OnApplicationQuit() => CaptureAndSave();

    void OnApplicationPause(bool paused)
    {
        // Quest sends this when the headset is removed or the app is backgrounded, and there is no
        // guarantee of an OnApplicationQuit afterwards.
        if (paused) CaptureAndSave();
    }

    void OnDestroy()
    {
        if (SceneController.TryGetInstance(out var controller))
            controller.UnregisterListener(this);
    }
}
