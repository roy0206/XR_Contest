using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Whether a process may start judging the player's work. Separate from
/// <see cref="InputLockService"/> because the two answer different questions: input locking asks
/// "may the player act?", this asks "does the act count?". 노장 대사 blocks judgement without
/// blocking action (F-014 5.3), and the 평가 창 will do the same.
///
/// Every process reads <see cref="IsOpen"/> as a single entry condition, so a new blocking
/// source costs one Hold call instead of a condition in every process.
/// </summary>
public static class ProcessGate
{
    static readonly List<string> Reasons = new();

    /// <summary>True when nothing is holding the gate. Processes judge only while this is true.</summary>
    public static bool IsOpen => Reasons.Count == 0;

    public static event Action<bool> Changed;

    /// <summary>
    /// Blocks judgement until the returned token is disposed. Reference counted, so an
    /// intro line and a pause menu holding at the same time need both to release.
    /// </summary>
    public static IDisposable Hold(string reason)
    {
        var wasOpen = IsOpen;
        Reasons.Add(reason);
        if (wasOpen) Changed?.Invoke(false);
        return new GateToken(reason);
    }

    /// <summary>Drops every hold. Used on scene load so a destroyed holder cannot stall a process.</summary>
    public static void ReleaseAll()
    {
        if (Reasons.Count == 0) return;
        Reasons.Clear();
        Changed?.Invoke(true);
    }

    public static string Describe() => IsOpen ? "열림" : $"닫힘 ({string.Join(", ", Reasons)})";

    static void Release(string reason)
    {
        if (!Reasons.Remove(reason)) return;
        if (IsOpen) Changed?.Invoke(true);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Reasons.Clear();
        Changed = null;
    }

    sealed class GateToken : IDisposable
    {
        readonly string _reason;
        bool _released;

        public GateToken(string reason) => _reason = reason;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            Release(_reason);
        }
    }
}
