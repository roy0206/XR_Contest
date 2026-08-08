using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 컷씬 씬이 <see cref="CutsceneDirector"/>에게서 받는 도구. 대사·자막·암전·캡션처럼 컷씬 바깥의
/// 시스템과 맞물리는 것만 여기에 있다.
///
/// Everything else a cutscene does — moving objects, lighting, particles, animation, its own
/// scripts — is plain scene work and needs nothing from this. That is the whole point of staging a
/// cutscene as a scene: the author keeps the editor they already know.
/// </summary>
public sealed class CutsceneContext
{
    readonly DialoguePlayer _dialogue;
    readonly Action<string> _setCaption;

    internal CutsceneContext(DialoguePlayer dialogue, Func<string, DialogueSequence> resolve, Action<string> setCaption)
    {
        _dialogue = dialogue;
        _setCaption = setCaption;
        Resolve = resolve;
    }

    Func<string, DialogueSequence> Resolve { get; }

    /// <summary>Id of the cutscene being played, as written in cutscene.json.</summary>
    public string CutsceneId { get; internal set; }

    public bool IsDialoguePlaying => _dialogue != null && _dialogue.IsPlaying;

    /// <summary>Plays a sequence from dialogue.json. Returns false when the id is unknown.</summary>
    public bool PlayDialogue(string sequenceId)
    {
        var sequence = Resolve?.Invoke(sequenceId);
        if (sequence == null)
        {
            Debug.LogWarning($"[Cutscene] 대사 '{sequenceId}'를 찾을 수 없습니다.");
            return false;
        }

        _dialogue.Play(sequence);
        return true;
    }

    /// <summary>Plays lines authored in the scene rather than in dialogue.json.</summary>
    public void PlayDialogue(DialogueSequence sequence)
    {
        if (sequence == null || !sequence.HasLines) return;

        // The cutscene already holds ProcessGate for its whole run, so the sequence must not try
        // to hold it again on its own.
        sequence.BlocksProcess = false;
        _dialogue.Play(sequence);
    }

    public void StopDialogue() => _dialogue?.Stop();

    /// <summary>전체화면 캡션. Empty hides it. Speaker subtitles go through PlayDialogue instead.</summary>
    public void SetCaption(string text) => _setCaption?.Invoke(text ?? string.Empty);

    /// <summary>화면 암전. 1 blacks out, 0 returns the view.</summary>
    public Tween Fade(float alpha, float seconds) => ScreenFader.Instance?.FadeTo(alpha, seconds);
}

/// <summary>
/// 컷씬 씬의 진입점. 컷씬마다 이걸 상속해 연출을 구현하고, 그 씬의 루트 오브젝트에 붙인다.
/// <see cref="CutsceneDirector"/>가 씬을 겹쳐 올린 뒤 <see cref="Begin"/>을 호출한다.
///
/// Implementations own their own pacing. Deriving from MonoBehaviour means Update, Invoke,
/// animation events or a PlayableDirector are all available — the director only needs to be told
/// when the performance is over, through <see cref="Finish"/>.
/// </summary>
public abstract class CutsceneStage : MonoBehaviour
{
    [Tooltip("컷씬 동안 화면을 담당할 카메라. 비우면 주 씬 카메라를 그대로 쓴다.")]
    [SerializeField] Camera stageCamera;

    /// <summary>
    /// Taken over by the director for the run and handed back before the scene unloads. Null keeps
    /// the main scene's camera, which is right for a cutscene staged in place.
    /// </summary>
    public Camera StageCamera => stageCamera;

    /// <summary>Dialogue, captions and fading. Valid from <see cref="OnBegin"/> onward.</summary>
    protected CutsceneContext Context { get; private set; }

    /// <summary>True once the stage reported its end. The director polls this.</summary>
    public bool IsFinished { get; private set; }

    public bool HasBegun { get; private set; }

    /// <summary>
    /// What a stage's Update should check on entry: not started, already over, or 일시정지 중.
    ///
    /// Time an implementation measures should come from <see cref="PauseService.Now"/> for the same
    /// reason — it stops while paused, so a beat resumed after the menu closes picks up where it
    /// left off instead of finding its deadline long past.
    /// </summary>
    protected bool ShouldTick => HasBegun && !IsFinished && !PauseService.IsPaused;

    internal void Begin(CutsceneContext context)
    {
        if (HasBegun) return;

        Context = context;
        HasBegun = true;

        try
        {
            OnBegin();
        }
        catch (Exception exception)
        {
            // A stage that throws on entry would otherwise hang the cutscene forever, holding
            // every input lock with it.
            Debug.LogException(exception, this);
            Finish();
        }
    }

    /// <summary>Torn down early by 건너뛰기 or an abort. The scene is unloaded right after.</summary>
    internal void Cancel()
    {
        if (!HasBegun || IsFinished) return;

        try
        {
            OnCancel();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        IsFinished = true;
    }

    /// <summary>Starts the performance. Called once, after the scene is loaded and faded in.</summary>
    protected abstract void OnBegin();

    /// <summary>
    /// Optional cleanup for a skip. Unloading the scene already destroys everything in it, so this
    /// is only for effects that outlive it — a tween on a persistent object, a paused AudioSource.
    /// </summary>
    protected virtual void OnCancel() { }

    /// <summary>Reports that the performance is over. Safe to call more than once.</summary>
    protected void Finish() => IsFinished = true;
}
