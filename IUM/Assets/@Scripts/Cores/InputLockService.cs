using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a holder blocks. Flags rather than a single bool because the two callers block
/// different things: 컷씬 takes movement, grabbing and PTT (F-003 3.4), while an in-game
/// 노장 대사 only takes PTT so the player can keep moving (F-011 1.4).
/// </summary>
[Flags]
public enum InputLockFlags
{
    None = 0,
    Locomotion = 1 << 0,
    Look = 1 << 1,
    Grab = 1 << 2,
    Interact = 1 << 3,
    PushToTalk = 1 << 4,
    Pause = 1 << 5,

    /// <summary>What a cutscene holds. Pause stays free so the player is never trapped.</summary>
    Cutscene = Locomotion | Look | Grab | Interact | PushToTalk,

    /// <summary>
    /// What an open menu holds. Same combination as <see cref="Cutscene"/> — Pause has to stay
    /// free or the menu could not be closed — but named apart because the two are unrelated
    /// reasons and either may change on its own.
    /// </summary>
    Menu = Locomotion | Look | Grab | Interact | PushToTalk,

    All = Locomotion | Look | Grab | Interact | PushToTalk | Pause
}

/// <summary>
/// Reference-counted input gate applied inside <see cref="PlayerInputModule"/>, the single
/// point where a device becomes intent. Counting matters because locks nest: a cutscene holds
/// PTT for its whole run while each 대사 line inside it holds PTT again, and the inner release
/// must not open the gate early.
/// </summary>
public static class InputLockService
{
    /// <summary>One counter per bit in <see cref="InputLockFlags"/>.</summary>
    const int FlagCount = 6;

    static readonly int[] Counts = new int[FlagCount];
    static readonly List<string> Reasons = new();

    public static InputLockFlags ActiveLocks { get; private set; }

    /// <summary>Raised whenever the combined lock set changes, never on a redundant re-acquire.</summary>
    public static event Action<InputLockFlags> Changed;

    public static bool IsLocked(InputLockFlags flags) => (ActiveLocks & flags) != 0;

    /// <summary>
    /// Holds <paramref name="flags"/> until the returned token is disposed. Disposing twice is
    /// safe, so a caller may release early and still dispose in its teardown path.
    /// </summary>
    public static IDisposable Acquire(InputLockFlags flags, string reason)
    {
        if (flags == InputLockFlags.None) return EmptyLock.Instance;

        for (var i = 0; i < FlagCount; i++)
            if (HasBit(flags, i)) Counts[i]++;

        Reasons.Add(reason);
        Recalculate();
        return new LockToken(flags, reason);
    }

    /// <summary>
    /// Zeroes every command a lock covers. Called once per frame after the active input source
    /// has written its commands, so desktop and XR are gated by the exact same code.
    /// </summary>
    public static void Apply(ref PlayerCommands commands)
    {
        var locks = ActiveLocks;
        if (locks == InputLockFlags.None) return;

        if ((locks & InputLockFlags.Locomotion) != 0)
        {
            commands.Move = Vector2.zero;
            commands.SnapTurn = 0f;
        }

        // Only the desktop look delta can be suppressed. An HMD drives the head transform
        // directly, so a locked look never means a frozen view in VR — cutscenes must be
        // authored on the assumption that the player can still turn their head.
        if ((locks & InputLockFlags.Look) != 0)
            commands.Look = Vector2.zero;

        if ((locks & InputLockFlags.Grab) != 0)
        {
            commands.LeftGrab = MaskGrab(commands.LeftGrab);
            commands.RightGrab = MaskGrab(commands.RightGrab);
        }

        if ((locks & InputLockFlags.Interact) != 0)
        {
            commands.InteractLeft = false;
            commands.InteractRight = false;
        }
        if ((locks & InputLockFlags.PushToTalk) != 0) commands.PushToTalk = false;
        if ((locks & InputLockFlags.Pause) != 0) commands.Pause = false;
    }

    /// <summary>
    /// Blocks a new grab but lets an existing one continue and release. Swallowing Released
    /// would leave an object welded to the hand for the rest of the scene.
    /// </summary>
    static GrabPhase MaskGrab(GrabPhase phase) =>
        phase == GrabPhase.Pressed ? GrabPhase.None : phase;

    /// <summary>Drops every lock. Used on scene load so a holder destroyed mid-hold cannot strand input.</summary>
    public static void ReleaseAll()
    {
        Array.Clear(Counts, 0, FlagCount);
        Reasons.Clear();
        Recalculate();
    }

    /// <summary>Lists what is currently holding input, for the dev scene and log output.</summary>
    public static string Describe() =>
        ActiveLocks == InputLockFlags.None ? "없음" : $"{ActiveLocks} ({string.Join(", ", Reasons)})";

    static void Release(InputLockFlags flags, string reason)
    {
        for (var i = 0; i < FlagCount; i++)
            if (HasBit(flags, i) && Counts[i] > 0) Counts[i]--;

        Reasons.Remove(reason);
        Recalculate();
    }

    static void Recalculate()
    {
        var next = InputLockFlags.None;
        for (var i = 0; i < FlagCount; i++)
            if (Counts[i] > 0) next |= (InputLockFlags)(1 << i);

        if (next == ActiveLocks) return;
        ActiveLocks = next;
        Changed?.Invoke(next);
    }

    static bool HasBit(InputLockFlags flags, int index) => ((int)flags & (1 << index)) != 0;

    // Statics survive entering play mode when domain reload is disabled, which would carry a
    // stale lock into the next session and silently disable input.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Array.Clear(Counts, 0, FlagCount);
        Reasons.Clear();
        ActiveLocks = InputLockFlags.None;
        Changed = null;
    }

    sealed class LockToken : IDisposable
    {
        readonly InputLockFlags _flags;
        readonly string _reason;
        bool _released;

        public LockToken(InputLockFlags flags, string reason)
        {
            _flags = flags;
            _reason = reason;
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            Release(_flags, _reason);
        }
    }

    sealed class EmptyLock : IDisposable
    {
        public static readonly EmptyLock Instance = new();
        public void Dispose() { }
    }
}
