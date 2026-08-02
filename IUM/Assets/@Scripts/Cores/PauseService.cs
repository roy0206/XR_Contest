using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 일시정지 상태 (F-019). Systems that advance on their own clock ask this before ticking.
///
/// A single flag rather than the reference counting <see cref="InputLockService"/> and
/// <see cref="ProcessGate"/> use: pausing is one explicit player action, and two overlapping
/// reasons to be paused would mean the menu could be dismissed while the game stays frozen.
///
/// 대사와 컷씬은 <see cref="Time.unscaledTime"/>으로 진행하고 AudioSource는 timeScale을 아예
/// 무시하므로, timeScale만으로는 멈추지 않는다. That is why this exists separately.
/// </summary>
public static class PauseService
{
    static float _resumeTimeScale = 1f;
    static float _pausedTotal;
    static float _pauseStartedAt;

    public static bool IsPaused { get; private set; }

    /// <summary>
    /// 대사·컷씬 진행의 기준 시각. Unscaled time with the paused stretches subtracted, so it simply
    /// stops while the menu is open.
    ///
    /// This is what makes pausing work for systems that cannot use timeScale. Suspending their tick
    /// would not be enough on its own: unscaledTime keeps running regardless, so a line resumed
    /// after a ten second pause would find its end time long past and snap forward. Meanwhile
    /// AudioListener.pause holds the voice exactly where it was, and the two would drift apart.
    /// </summary>
    public static float Now =>
        IsPaused ? _pauseStartedAt - _pausedTotal : Time.unscaledTime - _pausedTotal;

    /// <summary>Raised after the state changes. True means the game is now paused.</summary>
    public static event Action<bool> Changed;

    public static void SetPaused(bool value)
    {
        if (IsPaused == value) return;
        IsPaused = value;

        if (value)
        {
            _pauseStartedAt = Time.unscaledTime;

            // Remembered rather than assumed to be 1: a slow-motion effect or a debug speed change
            // would otherwise be wiped out by resuming.
            _resumeTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            _pausedTotal += Time.unscaledTime - _pauseStartedAt;
            Time.timeScale = _resumeTimeScale;
        }

        // Stops every AudioSource at once, including ones already playing. UI sounds that must stay
        // audible set AudioSource.ignoreListenerPause.
        AudioListener.pause = value;

        // Closes the DOTween hole. A tween built with SetUpdate(true) — ScreenFader's fades are —
        // explicitly ignores timeScale and would run to completion behind an open menu, leaving the
        // screen black for seconds after the cutscene resumes. The final scale of such a tween is
        // DOTween.unscaledTimeScale * DOTween.timeScale * tween.timeScale, so zeroing the first
        // factor stops all of them. Scaled tweens need nothing: their deltaTime is already zero.
        DOTween.unscaledTimeScale = value ? 0f : 1f;

        Changed?.Invoke(value);
    }

    public static void Toggle() => SetPaused(!IsPaused);

    /// <summary>Clears the state without raising events. For scene teardown and play-mode exit.</summary>
    public static void Reset()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        DOTween.unscaledTimeScale = 1f;
        _resumeTimeScale = 1f;
        _pausedTotal = 0f;
        _pauseStartedAt = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        // Entering play mode with domain reload disabled keeps statics alive, and a paused timeScale
        // carried into the next session would look like a frozen game with no menu.
        Reset();
        Changed = null;
    }
}
