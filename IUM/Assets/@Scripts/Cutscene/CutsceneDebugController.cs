using UnityEngine;

/// <summary>
/// Keyboard driver for 컷씬 verification without a headset, plus a live readout of the gates a
/// cutscene holds. Stands in for the 메인 화면 and 공정 시스템 that will eventually request these.
/// Add it to development scenes only.
///
/// 1 프롤로그 · Backspace 중단 · Space 2초 유지로 건너뛰기
/// </summary>
public sealed class CutsceneDebugController : MonoBehaviour
{
    [SerializeField] bool showOverlay = true;

    void Update()
    {
        var input = UserInput.Instance;
        if (input == null) return;

        if (input.GetKeyDown(KeyCode.Alpha1)) CutsceneDirector.Instance.Play("prologue");

        // Backspace rather than Esc, which now belongs to 일시정지. Aborting is not the same path
        // as 건너뛰기: no blackout, no progress saved, no scene change. It stands in for whatever
        // tears a cutscene down early.
        if (input.GetKeyDown(KeyCode.Backspace)) CutsceneDirector.Instance.Stop();
    }

    void OnGUI()
    {
        if (!showOverlay) return;

        var director = CutsceneDirector.Instance;
        if (director == null) return;

        var current = director.Current;
        var skip = director.SkipProgress;
        var lines = new[]
        {
            $"컷씬: {(current != null ? $"{current.Id} → {current.Scene}" : "없음")}   단계: {director.PhaseName}",
            $"건너뛰기: {(skip > 0f ? $"{skip * 100f:F0}%" : "대기")}",
            $"공정 판정: {ProcessGate.Describe()}",
            $"입력 잠금: {InputLockService.Describe()}",
            "1 프롤로그 · Backspace 중단 · Space 2초 유지로 건너뛰기 · Esc 일시정지"
        };

        GUI.Box(new Rect(10f, 10f, 620f, 22f * lines.Length + 12f), string.Empty);
        for (var i = 0; i < lines.Length; i++)
            GUI.Label(new Rect(20f, 16f + i * 22f, 600f, 22f), lines[i]);
    }
}
