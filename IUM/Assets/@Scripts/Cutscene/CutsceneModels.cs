using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컷씬 한 편의 정의. 연출 자체는 <see cref="Scene"/>이 들고 있고, 여기에는 그 씬을 언제 어떻게
/// 걸고 내릴지만 적는다.
///
/// The staging deliberately lives in a scene rather than in this data. A step list in JSON cannot
/// reference scene objects, cannot be previewed, and grows a new step kind every time a cutscene
/// wants to do something new. A scene gives the author the editor, the inspector references and
/// arbitrary code for free.
/// </summary>
[Serializable]
public sealed class CutsceneDefinition
{
    public string Id { get; set; }

    /// <summary>Name of the scene to overlay, as registered in Build Settings. Optional when
    /// <see cref="Video"/> is set.</summary>
    public string Scene { get; set; }

    /// <summary>
    /// 재생할 영상. StreamingAssets 기준 상대 경로이며(`Cutscenes/prologue.mp4`), 스킴이 붙어 있으면
    /// URL로 그대로 쓴다.
    ///
    /// 영상만 지정하면 씬을 올리지 않고 <see cref="CutsceneVideoSurface"/>가 재생을 맡는다. mp4 한
    /// 편마다 빈 씬과 Build Settings 항목을 만들지 않기 위해서다. <see cref="Scene"/>과 함께 쓰면
    /// 영상 위에 씬 연출을 얹으며, 그때는 스테이지가 끝나는 시점이 컷씬의 끝이 된다.
    /// </summary>
    public string Video { get; set; }

    public bool HasVideo => !string.IsNullOrWhiteSpace(Video);

    /// <summary>
    /// False blocks 건너뛰기 outright. 최초 플레이의 프롤로그를 건너뛸 수 있게 할지는 팀 협의
    /// 사항이므로(F-003 3.5) 코드가 아니라 데이터에서 정한다.
    /// </summary>
    public bool Skippable { get; set; } = true;

    /// <summary>
    /// Makes the overlay the active scene, which is only needed when its lighting settings should
    /// apply. Off by default: the main scene stays in charge of where new objects land.
    /// </summary>
    public bool MakeSceneActive { get; set; }

    /// <summary>
    /// Fades the screen back in once the scene is up. False leaves it black and hands the timing to
    /// the stage — 프롤로그처럼 암전 상태에서 자막부터 띄우는 연출이 그렇다 (F-003 3.3). Defaults
    /// to true so a stage that forgets to fade in is not left staring at black.
    /// </summary>
    public bool RevealOnEnter { get; set; } = true;

    /// <summary>Scene loaded when the cutscene ends, skipped or not. Empty stays in place.</summary>
    public string NextScene { get; set; }

    /// <summary>
    /// Progress recorded on completion. 건너뛰어도 기록한다 — 건너뛰기는 "이미 본 것"이라는
    /// 의사 표시이고, 문서도 완료 여부를 저장하라고만 한다 (F-003 3.5).
    /// </summary>
    public ProcessId? CompletesProcess { get; set; }

    /// <summary>
    /// 클리어 처리. Wipes progress when this cutscene ends, which turns 이어하기 off — the run is
    /// over and there is nothing left to resume. Set on the ending. Options are untouched.
    ///
    /// Applies to a skipped ending too: reaching it at all means the game was cleared.
    /// </summary>
    public bool ClearsProgress { get; set; }

    /// <summary>둘 중 하나는 있어야 재생할 것이 있다.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Id) &&
                           (!string.IsNullOrWhiteSpace(Scene) || HasVideo);
}

/// <summary>Timing shared by every cutscene. Tuned from data, not code.</summary>
[Serializable]
public sealed class CutsceneSettings
{
    /// <summary>건너뛰기 홀드 시간 (F-003 3.5).</summary>
    public float SkipHoldSeconds { get; set; } = 2f;

    /// <summary>
    /// Blackout before the cutscene scene is brought in. Loading pops a whole set of objects into
    /// view at once, and in VR that has to happen behind black.
    /// </summary>
    public float EnterFadeSeconds { get; set; } = 0.5f;

    /// <summary>Blackout before the scene is unloaded, for the same reason.</summary>
    public float ExitFadeSeconds { get; set; } = 0.5f;

    /// <summary>Blackout when 건너뛰기 fires. Short on purpose: the player asked to leave.</summary>
    public float SkipFadeSeconds { get; set; } = 0.35f;

    /// <summary>Fade back in after the cutscene ends and no scene change follows.</summary>
    public float OutroFadeSeconds { get; set; } = 0.6f;

    public void Clamp()
    {
        SkipHoldSeconds = Mathf.Max(0.1f, SkipHoldSeconds);
        EnterFadeSeconds = Mathf.Max(0f, EnterFadeSeconds);
        ExitFadeSeconds = Mathf.Max(0f, ExitFadeSeconds);
        SkipFadeSeconds = Mathf.Max(0f, SkipFadeSeconds);
        OutroFadeSeconds = Mathf.Max(0f, OutroFadeSeconds);
    }
}

/// <summary>컷씬 목록. Loaded from static data alongside dialogue.json.</summary>
[Serializable]
public sealed class CutsceneTable
{
    public CutsceneSettings Settings { get; set; } = new();
    public List<CutsceneDefinition> Cutscenes { get; set; } = new();

    public static CutsceneTable CreateEmpty() => new();

    public void Prepare()
    {
        Settings ??= new CutsceneSettings();
        Settings.Clamp();
        Cutscenes ??= new List<CutsceneDefinition>();
    }

    public bool TryGet(string id, out CutsceneDefinition definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(id)) return false;

        for (var i = 0; i < Cutscenes.Count; i++)
        {
            if (!string.Equals(Cutscenes[i]?.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
            definition = Cutscenes[i];
            return true;
        }

        return false;
    }
}
