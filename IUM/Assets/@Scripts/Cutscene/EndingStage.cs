using UnityEngine;

/// <summary>
/// 엔딩 연출 (F-021 6.3). 복원된 숭례문을 보여주고 노장과 이음이의 마무리 대사를 지난 뒤 최종
/// 문구를 남긴다. 메인 화면 복귀는 cutscene.json의 nextScene이 처리한다.
///
/// 6.5의 결과 표시(완료한 공정, 공정별 최고 평가)는 아직 넣지 않았다. 표시할 공정 자체가 없어
/// 빈 표가 되기 때문으로, 공정이 생기면 <see cref="ShowResults"/> 자리에 채운다.
/// </summary>
public sealed class EndingStage : CutsceneStage
{
    enum Beat
    {
        /// <summary>복원된 숭례문 모습. 카메라가 천천히 물러나며 전경을 보여준다.</summary>
        Reveal,

        Nojang,
        Ieumi,

        /// <summary>최종 문구 (F-021 6.4).</summary>
        Message,

        Done
    }

    [Header("연출 대상")]
    [Tooltip("복원된 모습을 보여주며 물러나는 카메라 리그.")]
    [SerializeField] Transform cameraRig;

    [Tooltip("리그가 이동할 로컬 오프셋. 카메라 워크가 필요 없으면 0으로 둔다.")]
    [SerializeField] Vector3 cameraTravel = new(0f, 1.5f, -6f);

    [Header("대사")]
    [SerializeField] string nojangSequenceId = "ending_nojang";
    [SerializeField] string ieumiSequenceId = "ending_ieumi";

    [Header("길이")]
    [SerializeField, Min(0f)] float revealSeconds = 4f;
    [SerializeField, Min(0f)] float messageSeconds = 6f;

    [Tooltip("대사가 끝난 뒤 잠시 두는 시간.")]
    [SerializeField, Min(0f)] float lineTailSeconds = 0.8f;

    Beat _beat;
    float _beatEndTime;
    float _tailEndTime;
    float _revealElapsed;
    Vector3 _cameraStart;

    protected override void OnBegin()
    {
        if (cameraRig != null) _cameraStart = cameraRig.localPosition;

        ShowResults();
        EnterBeat(Beat.Reveal, revealSeconds);
    }

    /// <summary>
    /// 결과 표시 자리 (F-021 6.5). 총점과 순위는 표시하지 않는다는 것이 문서의 요구이므로, 채울
    /// 때도 완료한 공정과 공정별 최고 평가만 나열한다. <see cref="UserProgressData.GetGrade"/>가
    /// 그 값을 이미 들고 있다.
    /// </summary>
    void ShowResults() { }

    void Update()
    {
        if (!ShouldTick) return;

        var now = PauseService.Now;
        if (_beat == Beat.Reveal) DriveCamera();

        switch (_beat)
        {
            case Beat.Reveal:
                if (now >= _beatEndTime) BeginLine(Beat.Nojang, nojangSequenceId);
                break;

            case Beat.Nojang:
                if (IsLineOver(now)) BeginLine(Beat.Ieumi, ieumiSequenceId);
                break;

            case Beat.Ieumi:
                if (IsLineOver(now)) BeginMessage();
                break;

            case Beat.Message:
                if (now >= _beatEndTime) EndEnding();
                break;
        }
    }

    void EnterBeat(Beat beat, float seconds)
    {
        _beat = beat;
        _beatEndTime = PauseService.Now + Mathf.Max(0f, seconds);
    }

    void BeginLine(Beat beat, string sequenceId)
    {
        _beat = beat;
        _tailEndTime = 0f;

        // A missing sequence must not stall the ending, so failure falls through to the tail timer.
        if (!Context.PlayDialogue(sequenceId))
            _tailEndTime = PauseService.Now + lineTailSeconds;
    }

    /// <summary>True once the line has finished and its tail has elapsed.</summary>
    bool IsLineOver(float now)
    {
        if (Context.IsDialoguePlaying)
        {
            _tailEndTime = 0f;
            return false;
        }

        if (_tailEndTime <= 0f)
        {
            _tailEndTime = now + lineTailSeconds;
            return false;
        }

        return now >= _tailEndTime;
    }

    void BeginMessage()
    {
        Context.SetCaption("기술은 기록으로 남을 수 있지만,\n전통은 사람이 이어갈 때 비로소 살아남는다.");
        EnterBeat(Beat.Message, messageSeconds);
    }

    void EndEnding()
    {
        Context.SetCaption(string.Empty);
        _beat = Beat.Done;

        // The director fades out and unloads from here, then cutscene.json's nextScene takes the
        // player back to the main menu (F-021 6.3).
        Finish();
    }

    void DriveCamera()
    {
        if (cameraRig == null) return;

        _revealElapsed += Time.unscaledDeltaTime;
        var t = Mathf.Clamp01(_revealElapsed / Mathf.Max(0.01f, revealSeconds));
        cameraRig.localPosition = _cameraStart + cameraTravel * Mathf.SmoothStep(0f, 1f, t);
    }

    protected override void OnCancel() => Context.SetCaption(string.Empty);
}
