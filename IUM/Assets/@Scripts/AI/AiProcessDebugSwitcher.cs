using System;
using UnityEngine;

/// <summary>
/// Stands in for the 3~6단계 process systems: it pushes a process, a step and failure counts into
/// <see cref="AiProcessContextRegistry"/> from the keyboard, so 이음이's context handling can be
/// verified before those systems exist. Add it to development scenes only.
///
/// 1 튜토리얼 · 2 먹매김 · 3 톱질 · 4 끌질 · 5 도리 설치 · 6 공포 퍼즐 ·
/// 8 선택형 질문 목록 표시 · 9 실패 보고(3회 누적 시 직접 설명 허용) · 0 실패 초기화 ·
/// L 노장 대사 잠금 토글
/// </summary>
public sealed class AiProcessDebugSwitcher : MonoBehaviour
{
    [SerializeField] bool logChanges = true;

    IDisposable _pttLock;

    void Update()
    {
        var input = UserInput.Instance;
        if (input == null) return;

        if (input.GetKeyDown(KeyCode.Alpha1)) Apply(ProcessId.Tutorial, "도구 잡기");
        else if (input.GetKeyDown(KeyCode.Alpha2)) Apply(ProcessId.Makmeok, "먹선 긋기");
        else if (input.GetKeyDown(KeyCode.Alpha3)) Apply(ProcessId.Sawing, "먹선 따라 자르기");
        else if (input.GetKeyDown(KeyCode.Alpha4)) Apply(ProcessId.Chiseling, "결구 홈 파기");
        else if (input.GetKeyDown(KeyCode.Alpha5)) Apply(ProcessId.PurlinInstall, "도리 걸기");
        else if (input.GetKeyDown(KeyCode.Alpha6)) Apply(ProcessId.GongpoPuzzle, "공포 짜 올리기");

        if (input.GetKeyDown(KeyCode.Alpha9))
        {
            AiProcessContextRegistry.ReportFailure("절단면이 먹선에서 벗어남");
            Log($"실패 {AiProcessContextRegistry.Current.FailureCount}회 " +
                $"(직접 설명 허용: {AiProcessContextRegistry.Current.AllowDirectAnswer})");
        }

        if (input.GetKeyDown(KeyCode.Alpha8))
        {
            AiConversationManager.Instance.ShowSuggestions();
            Log("선택형 질문 목록 표시");
        }

        if (input.GetKeyDown(KeyCode.Alpha0))
        {
            AiProcessContextRegistry.ReportSuccess();
            Log("실패 횟수 초기화");
        }

        if (!input.GetKeyDown(KeyCode.L)) return;

        // 노장 대사 재생 중 PTT 잠금 (F-011 1.4) without the 노장 system existing yet.
        //
        // Held through InputLockService rather than by calling SetInputLocked directly: the
        // 대사 시스템 already holds PTT that way, and PushToTalkLockBridge is the single writer
        // that follows the combined result. A second direct caller would let a 대사 ending
        // release this toggle — and this toggle release a 대사 that is still playing.
        if (_pttLock == null)
        {
            _pttLock = InputLockService.Acquire(InputLockFlags.PushToTalk, "노장 대사 (디버그)");
            Log("노장 대사 잠금 켜짐");
        }
        else
        {
            _pttLock.Dispose();
            _pttLock = null;
            Log("노장 대사 잠금 꺼짐");
        }
    }

    // Without this a disabled switcher would strand PTT for the rest of the scene: the bridge
    // only unlocks 이음이 when the reference count reaches zero, and nothing else would release it.
    void OnDisable()
    {
        _pttLock?.Dispose();
        _pttLock = null;
    }

    void Apply(ProcessId process, string step)
    {
        AiProcessContextRegistry.SetProcess(process, step);
        Log($"공정 -> {AiProcessContextRegistry.Current.ProcessLabel} / {step}");
    }

    void Log(string message)
    {
        if (logChanges) Debug.Log($"[AI Debug] {message}");
    }
}
