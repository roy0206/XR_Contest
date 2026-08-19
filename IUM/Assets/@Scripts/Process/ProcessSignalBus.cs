using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬 상호작용과 데이터 기반 공정 판정을 느슨하게 연결하는 런타임 신호 저장소.
/// 도구 쪽은 키와 수치만 보고하고, <see cref="ProcessStep"/>은 현재 단계의 키만 읽는다.
/// </summary>
public static class ProcessSignalBus
{
    static readonly Dictionary<string, float> Values = new(StringComparer.Ordinal);

    public static float Read(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0f;
        return Values.TryGetValue(key, out var value) ? value : 0f;
    }

    public static void Add(string key, float amount = 1f)
    {
        if (string.IsNullOrWhiteSpace(key) || amount <= 0f) return;
        Values[key] = Read(key) + amount;
    }

    public static void Reset(string key)
    {
        if (!string.IsNullOrWhiteSpace(key)) Values.Remove(key);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAll() => Values.Clear();
}
