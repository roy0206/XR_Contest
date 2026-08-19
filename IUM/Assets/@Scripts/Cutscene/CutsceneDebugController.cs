using UnityEngine;

/// <summary>
/// Keyboard driver for 컷씬 verification without a headset, plus a live readout of the gates a
/// cutscene holds. Stands in for the 메인 화면 and 공정 시스템 that will eventually request these.
/// Add it to development scenes only.
///
/// 1 프롤로그 · 2 영상 컷씬 · Backspace 중단 · Space 2초 유지로 건너뛰기
/// </summary>
public sealed class CutsceneDebugController : MonoBehaviour
{
    [SerializeField] bool showOverlay = true;

    void Update()
    {
        var input = UserInput.Instance;
        if (input == null) return;

        if (input.GetKeyDown(KeyCode.Alpha1)) CutsceneDirector.Instance.Play("prologue");

        // 영상 재생 경로 검증용. cutscene.json의 video_test와 함께 지운다.
        if (input.GetKeyDown(KeyCode.Alpha2)) CutsceneDirector.Instance.Play("video_test");

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
            $"컷씬: {(current != null ? $"{current.Id} → {Destination(current)}" : "없음")}   단계: {director.PhaseName}",
            $"건너뛰기: {(skip > 0f ? $"{skip * 100f:F0}%" : "대기")}",
            $"공정 판정: {ProcessGate.Describe()}",
            $"입력 잠금: {InputLockService.Describe()}",
            $"영상 볼륨: {VideoVolume()}",
            "1 프롤로그 · 2 영상 컷씬 · Backspace 중단 · Space 2초 유지로 건너뛰기 · Esc 일시정지"
        };

        GUI.Box(new Rect(10f, 10f, 620f, 22f * lines.Length + 12f), string.Empty);
        for (var i = 0; i < lines.Length; i++)
            GUI.Label(new Rect(20f, 16f + i * 22f, 600f, 22f), lines[i]);
    }

    /// <summary>
    /// 재생 중 일시정지 → 옵션에서 영상 볼륨을 움직였을 때 값이 실제로 따라오는지 눈으로 보기
    /// 위한 것이다. 귀로만 판단하면 실시간 반영 여부를 가리기 어렵다.
    /// </summary>
    static string VideoVolume()
    {
        if (!DataManager.HasInstance || !DataManager.Instance.IsReady) return "데이터 준비 중";
        return $"{DataManager.Instance.Settings.VideoVolume * 100f:F0}";
    }

    /// <summary>씬 컷씬인지 영상 컷씬인지, 아니면 둘을 겹친 것인지 한 줄로 보여준다.</summary>
    static string Destination(CutsceneDefinition definition)
    {
        if (!definition.HasVideo) return definition.Scene;
        return string.IsNullOrWhiteSpace(definition.Scene)
            ? definition.Video
            : $"{definition.Video} + {definition.Scene}";
    }
}
