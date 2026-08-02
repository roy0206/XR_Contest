using UnityEngine;

/// <summary>
/// 프롤로그 연출 (F-003 3.3). 숭례문 화재에서 복원 현장으로 넘어가는 도입부다.
///
/// Doubles as the reference for what a <see cref="CutsceneStage"/> looks like: scene objects come
/// from serialized fields, timing is plain Update arithmetic, and only dialogue, captions and the
/// blackout go through <see cref="CutsceneContext"/>. Anything this needs that the context does not
/// provide is just scene work — move a transform, enable a light, play an Animator.
/// </summary>
public sealed class PrologueStage : CutsceneStage
{
    enum Beat
    {
        /// <summary>"2008년 2월 10일" over black.</summary>
        Date,

        Fire,
        Collapse,

        /// <summary>노장의 목소리. Runs until the lines finish rather than on a timer.</summary>
        Nojang,

        /// <summary>"2008년 숭례문 복원 현장" over black.</summary>
        Closing,

        Done
    }

    [Header("연출 대상")]
    [Tooltip("화재 장면 동안 천천히 밀고 들어가는 카메라 리그.")]
    [SerializeField] Transform cameraRig;

    [Tooltip("리그가 이동할 로컬 오프셋. 카메라 워크가 필요 없으면 0으로 둔다.")]
    [SerializeField] Vector3 cameraTravel = new(0f, 0f, 4f);

    [Tooltip("화염을 표현하는 광원. 없으면 무시한다.")]
    [SerializeField] Light fireLight;

    [Tooltip("붕괴 연출에서 쓰러뜨릴 오브젝트들.")]
    [SerializeField] Transform[] collapsingParts;

    [Header("대사")]
    [SerializeField] string nojangSequenceId = "prologue_nojang";

    [Header("길이")]
    [SerializeField, Min(0f)] float dateSeconds = 3.5f;
    [SerializeField, Min(0f)] float fireSeconds = 4f;
    [SerializeField, Min(0f)] float collapseSeconds = 3f;
    [SerializeField, Min(0f)] float closingSeconds = 3f;

    [Tooltip("노장 대사가 끝난 뒤 잠시 두는 시간.")]
    [SerializeField, Min(0f)] float nojangTailSeconds = 0.8f;

    Beat _beat;
    float _beatEndTime;
    float _fireElapsed;
    Vector3 _cameraStart;
    float _nojangTailEndTime;

    protected override void OnBegin()
    {
        if (cameraRig != null) _cameraStart = cameraRig.localPosition;
        if (fireLight != null) fireLight.enabled = false;

        // The screen is still black here: this cutscene is declared with revealOnEnter false, so
        // the date card reads over the blackout before BeginFire opens the view.
        Context.SetCaption("2008년 2월 10일");
        EnterBeat(Beat.Date, dateSeconds);
    }

    void Update()
    {
        if (!ShouldTick) return;

        var now = PauseService.Now;
        if (_beat == Beat.Fire || _beat == Beat.Collapse) DriveCamera();

        switch (_beat)
        {
            case Beat.Date:
                if (now >= _beatEndTime) BeginFire();
                break;

            case Beat.Fire:
                FlickerFire();
                if (now >= _beatEndTime) BeginCollapse();
                break;

            case Beat.Collapse:
                DriveCollapse(now);
                if (now >= _beatEndTime) BeginNojang();
                break;

            case Beat.Nojang:
                TickNojang(now);
                break;

            case Beat.Closing:
                if (now >= _beatEndTime) EndPrologue();
                break;
        }
    }

    void EnterBeat(Beat beat, float seconds)
    {
        _beat = beat;
        _beatEndTime = PauseService.Now + Mathf.Max(0f, seconds);
    }

    void BeginFire()
    {
        Context.SetCaption(string.Empty);
        if (fireLight != null) fireLight.enabled = true;

        // The director already faded in when the stage began; this reveals the scene itself.
        Context.Fade(0f, 1.5f);
        EnterBeat(Beat.Fire, fireSeconds);
    }

    void BeginCollapse()
    {
        EnterBeat(Beat.Collapse, collapseSeconds);
    }

    void BeginNojang()
    {
        _beat = Beat.Nojang;
        _nojangTailEndTime = 0f;

        // A missing sequence must not stall the prologue, so failure falls through to the tail
        // timer and the cutscene still reaches its end.
        if (!Context.PlayDialogue(nojangSequenceId))
            _nojangTailEndTime = PauseService.Now + nojangTailSeconds;
    }

    void TickNojang(float now)
    {
        if (Context.IsDialoguePlaying)
        {
            _nojangTailEndTime = 0f;
            return;
        }

        if (_nojangTailEndTime <= 0f)
        {
            _nojangTailEndTime = now + nojangTailSeconds;
            return;
        }

        if (now < _nojangTailEndTime) return;

        Context.Fade(1f, 1.5f);
        Context.SetCaption("2008년 숭례문 복원 현장");
        EnterBeat(Beat.Closing, closingSeconds);
    }

    void EndPrologue()
    {
        Context.SetCaption(string.Empty);
        _beat = Beat.Done;
        Finish();
    }

    /// <summary>Slow push-in. Unscaled so it keeps the same pace the rest of the cutscene uses.</summary>
    void DriveCamera()
    {
        if (cameraRig == null) return;

        _fireElapsed += Time.unscaledDeltaTime;
        var total = Mathf.Max(0.01f, fireSeconds + collapseSeconds);
        var t = Mathf.Clamp01(_fireElapsed / total);
        cameraRig.localPosition = _cameraStart + cameraTravel * Mathf.SmoothStep(0f, 1f, t);
    }

    void FlickerFire()
    {
        if (fireLight == null) return;
        fireLight.intensity = 2f + Mathf.PerlinNoise(PauseService.Now * 6f, 0f) * 1.5f;
    }

    void DriveCollapse(float now)
    {
        if (collapsingParts == null) return;

        var remaining = Mathf.Max(0f, _beatEndTime - now);
        var t = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, collapseSeconds));

        for (var i = 0; i < collapsingParts.Length; i++)
        {
            var part = collapsingParts[i];
            if (part == null) continue;

            // Staggered so the parts do not fall in lockstep.
            var offset = i * 0.15f;
            var local = Mathf.Clamp01((t - offset) / Mathf.Max(0.01f, 1f - offset));
            part.localRotation = Quaternion.Euler(local * 78f, 0f, local * 12f);
        }
    }

    /// <summary>건너뛰기로 중단된 경우. 씬이 곧 내려가므로 지속되는 것만 되돌린다.</summary>
    protected override void OnCancel() => Context.SetCaption(string.Empty);
}
