using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 메인 컨텐츠 자리를 대신하는 자유 플레이 구간. flow.json의 tutorial 항목이 이 씬을 가리키고,
/// 여기서 <see cref="firstProcess"/>부터 <see cref="lastProcess"/>까지를 한 번에 완료 처리해
/// 다음 목적지를 엔딩으로 만든다.
///
/// 공정을 하나씩 올리지 않는 이유는 그다음 공정에도 목적지가 없어 같은 자리에서 다시 막히기
/// 때문이다. 구간 전체를 건너뛰어야 흐름이 엔딩까지 닿는다.
///
/// 실제 공정 씬이 생기면 flow.json의 해당 줄을 그 씬으로 바꾸고 이 컴포넌트를 떼면 된다.
/// </summary>
public sealed class PlaceholderProcess : MonoBehaviour
{
    [Header("Range")]
    [SerializeField] ProcessId firstProcess = ProcessId.Tutorial;
    [SerializeField] ProcessId lastProcess = ProcessId.GongpoPuzzle;

    [Header("Desktop")]
    [SerializeField] KeyCode completeKey = KeyCode.Return;
    [SerializeField] bool showOverlay = true;

    bool _completing;

    void Update()
    {
        if (_completing || PauseService.IsPaused) return;
        if (CutsceneDirector.HasInstance && CutsceneDirector.Instance.IsPlaying) return;

        var input = UserInput.Instance;
        if (input == null || !input.GetKeyDown(completeKey)) return;

        _completing = true;
        _ = CompleteAsync();
    }

    async Task CompleteAsync()
    {
        if (!DataManager.HasInstance || !DataManager.Instance.IsReady)
        {
            Debug.LogWarning("[Placeholder] 데이터가 준비되지 않아 완료 처리를 하지 못했습니다.");
            _completing = false;
            return;
        }

        var progress = DataManager.Instance.Progress;
        for (var process = firstProcess; process <= lastProcess; process++)
            progress.Complete(process);

        // 이 구간을 떠나므로 남은 위치는 의미가 없다. Left behind, it would be restored the next
        // time this scene happens to be entered with different progress.
        progress.PlayerPose = null;

        // Awaited rather than fired and forgotten: the ending clears progress and writes again as
        // soon as it finishes, and the two writes must not overlap.
        try
        {
            await DataManager.Instance.SaveUserAsync();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Placeholder] 진행 저장에 실패했습니다: {exception.Message}");
        }

        if (this == null) return;

        Debug.Log($"[Placeholder] {firstProcess}~{lastProcess} 완료 처리. 다음 공정: {progress.NextProcess}");
        GameFlow.Instance.GoToCurrent();
    }

    void OnGUI()
    {
        if (!showOverlay) return;

        var progress = DataManager.HasInstance && DataManager.Instance.IsReady
            ? DataManager.Instance.Progress
            : null;

        var pose = progress?.PlayerPose;
        var lines = new[]
        {
            $"자유 플레이 (임시 구간 · {firstProcess}~{lastProcess} 대체)",
            $"다음 공정: {(progress != null ? progress.NextProcess.ToString() : "데이터 준비 중")}",
            $"저장된 위치: {(pose != null && pose.IsValid ? $"{pose.Scene} ({pose.X:F1}, {pose.Z:F1})" : "없음")}",
            $"{completeKey} 구간 완료 → 엔딩 · Esc 일시정지",
            "WASD 이동 · 우클릭 시점 · Q/E 스냅 턴 · F 왼손 · 좌클릭 오른손"
        };

        GUI.Box(new Rect(10f, 10f, 620f, 22f * lines.Length + 12f), string.Empty);
        for (var i = 0; i < lines.Length; i++)
            GUI.Label(new Rect(20f, 16f + i * 22f, 600f, 22f), lines[i]);
    }
}
