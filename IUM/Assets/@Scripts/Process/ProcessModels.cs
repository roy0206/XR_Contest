using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 단계를 통과시키는 조건. 판정에 필요한 값은 전부 <see cref="PlayerCommands"/>나 상호작용
/// 계층이 이미 공개하고 있어, 새 조건을 늘려도 타인 소유 파일을 건드릴 일이 없다.
///
/// 새 조건을 추가할 때는 여기와 <see cref="ProcessStep.Evaluate"/>의 switch 두 곳만 채운다.
/// </summary>
public enum StepCondition
{
    /// <summary>판정 없음. 대사가 끝나면 통과한다. 설명만 하고 넘어가는 단계에 쓴다.</summary>
    None,

    /// <summary>시점을 돌린 누적량(도).</summary>
    Look,

    /// <summary>이동한 누적 거리에 비례하는 값.</summary>
    Move,

    /// <summary>스냅 턴 횟수.</summary>
    SnapTurn,

    /// <summary>대상을 손이나 레이저로 가리킨 채 유지.</summary>
    Point,

    /// <summary>대상을 쥔 채 유지 (F-004 4.4 "지정 도구를 1초 이상").</summary>
    Grab,

    /// <summary>대상 소켓에 부재가 안착 (F-004 4.4 "지정 영역 안에 도구 배치").</summary>
    Place,

    /// <summary>PTT 입력 유지.</summary>
    PushToTalk,

    /// <summary>
    /// 제작 도구나 작업 구역이 <see cref="ProcessSignalBus"/>에 보고한 누적값.
    /// 타인 소유 상호작용 코드를 공정 시스템에 직접 의존시키지 않고 연결할 때 사용한다.
    /// </summary>
    Signal
}

/// <summary>
/// 공정을 이루는 한 단계. 대상은 씬 오브젝트지만 JSON은 그것을 참조할 수 없으므로
/// <see cref="ProcessTarget"/>이 등록한 문자열 키로 가리킨다. 단계의 순서와 수치는 데이터가,
/// 실제 오브젝트는 씬이 들고 있는 구조다.
/// </summary>
[Serializable]
public sealed class ProcessStepData
{
    public string Id { get; set; }

    public StepCondition Condition { get; set; } = StepCondition.None;

    /// <summary>
    /// 판정 대상 키. Point·Grab·Place는 <see cref="ProcessTarget.Key"/>, Signal은
    /// <see cref="ProcessSignalBus"/>의 신호 키를 가리킨다.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    /// 이 단계에서 잡을 수 있게 열어 둘 대상들 (F-014 5.3 공정 잠금). 비우면 <see cref="Target"/>
    /// 하나만 열린다.
    ///
    /// 놓기 단계 때문에 따로 있다. 그때 <see cref="Target"/>은 소켓이므로, 손에 든 도구까지
    /// 잠기면 내려놓을 방법이 없어진다.
    /// </summary>
    public List<string> Unlock { get; set; }

    /// <summary>임계값. 조건마다 단위가 다르다 — Look은 도, Move는 거리, SnapTurn은 횟수.</summary>
    public float Amount { get; set; } = 1f;

    /// <summary>조건을 유지해야 하는 시간. Point·Grab·PushToTalk에서 쓴다.</summary>
    public float HoldSeconds { get; set; }

    public string IntroDialogue { get; set; }

    /// <summary>일정 시간 진전이 없을 때 재생 (F-004 4.5의 10초 재안내).</summary>
    public string RetryDialogue { get; set; }

    public string SuccessDialogue { get; set; }

    /// <summary>현재 목표 안내 문구. 지금은 디버그 오버레이가 쓰고, 안내 UI가 생기면 그쪽이 쓴다.</summary>
    public string Goal { get; set; }
}

/// <summary>공정 하나의 정의. <see cref="ProcessId"/>로 <see cref="UserProgressData.NextProcess"/>와 이어진다.</summary>
[Serializable]
public sealed class ProcessDefinition
{
    public ProcessId Process { get; set; }

    /// <summary>공정 진입 대사. 노장의 공정 설명이 여기 온다.</summary>
    public string IntroDialogue { get; set; }

    /// <summary>모든 단계를 마친 뒤의 대사.</summary>
    public string CompleteDialogue { get; set; }

    /// <summary>
    /// 완료 시 기록할 등급. 평가 시스템이 생기기 전까지의 고정값이다 — 튜토리얼은 문서상
    /// 불합격이 없으므로(F-004 4.5) 고정값으로 충분하고, 공정 평가가 붙으면 이 필드 대신
    /// 실제 판정 결과를 넘기게 된다.
    /// </summary>
    public ProcessGrade Grade { get; set; } = ProcessGrade.None;

    public List<ProcessStepData> Steps { get; set; } = new();

    public bool HasSteps => Steps != null && Steps.Count > 0;
}

/// <summary>모든 공정에 공통인 값.</summary>
[Serializable]
public sealed class ProcessSettings
{
    /// <summary>
    /// 진전 없이 이 시간이 지나면 재안내 대사를 재생한다 (F-004 4.5의 10초).
    ///
    /// 같은 문서의 5초 아이콘 강조와 20초 컨트롤러 표시는 안내 UI가 있어야 하므로 여기 없다.
    /// UI가 생기면 단계 값을 둘 더 늘린다.
    /// </summary>
    public float RetryAfterSeconds { get; set; } = 10f;

    /// <summary>단계 사이에 두는 짧은 간격. 성공 대사와 다음 설명이 붙어 들리지 않게 한다.</summary>
    public float StepGapSeconds { get; set; } = 0.4f;

    public void Clamp()
    {
        RetryAfterSeconds = Mathf.Max(0f, RetryAfterSeconds);
        StepGapSeconds = Mathf.Max(0f, StepGapSeconds);
    }
}

/// <summary>공정 정의 전체. 대사와 마찬가지로 정적 데이터라 코드 변경 없이 단계를 늘릴 수 있다.</summary>
[Serializable]
public sealed class ProcessTable
{
    public ProcessSettings Settings { get; set; } = new();
    public List<ProcessDefinition> Processes { get; set; } = new();

    public static ProcessTable CreateEmpty() => new();

    public void Prepare()
    {
        Settings ??= new ProcessSettings();
        Settings.Clamp();
        Processes ??= new List<ProcessDefinition>();

        for (var i = 0; i < Processes.Count; i++)
        {
            var definition = Processes[i];
            if (definition == null) continue;
            definition.Steps ??= new List<ProcessStepData>();
        }
    }

    public bool TryGet(ProcessId process, out ProcessDefinition definition)
    {
        definition = null;
        for (var i = 0; i < Processes.Count; i++)
        {
            if (Processes[i] == null || Processes[i].Process != process) continue;
            definition = Processes[i];
            return true;
        }

        return false;
    }
}
