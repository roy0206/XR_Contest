using System;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Play order of every process. Progress is stored as the process to play next,
/// so completion of earlier processes is a comparison instead of a saved flag.
/// </summary>
public enum ProcessId
{
    Prologue,
    Tutorial,
    Makmeok,
    Sawing,
    Chiseling,
    PurlinInstall,
    GongpoPuzzle,
    Ending,
    Completed
}

/// <summary>Ordered worst to best so the higher value wins when keeping a best grade.</summary>
public enum ProcessGrade
{
    None,
    Fail,
    Assisted,
    Pass,
    Excellent
}

[Serializable]
public sealed class UserDataDocument
{
    public UserSettingsData Settings { get; set; } = new();
    public UserProgressData Progress { get; set; } = new();
}

[Serializable]
public sealed class UserSettingsData
{
    public float MasterVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 0.7f;
    public float DialogueVolume { get; set; } = 1f;
    public float EnvironmentVolume { get; set; } = 0.8f;

    public void Clamp()
    {
        MasterVolume = Mathf.Clamp01(MasterVolume);
        MusicVolume = Mathf.Clamp01(MusicVolume);
        DialogueVolume = Mathf.Clamp01(DialogueVolume);
        EnvironmentVolume = Mathf.Clamp01(EnvironmentVolume);
    }
}

[Serializable]
public sealed class UserProgressData
{
    public ProcessId NextProcess { get; set; } = ProcessId.Prologue;
    public ProcessGrade MakmeokGrade { get; set; } = ProcessGrade.None;
    public ProcessGrade SawingGrade { get; set; } = ProcessGrade.None;
    public ProcessGrade ChiselingGrade { get; set; } = ProcessGrade.None;

    [JsonIgnore] public bool HasSaveData => NextProcess != ProcessId.Prologue;

    public bool IsCompleted(ProcessId process) => NextProcess > process;

    public ProcessGrade GetGrade(ProcessId process) => process switch
    {
        ProcessId.Makmeok => MakmeokGrade,
        ProcessId.Sawing => SawingGrade,
        ProcessId.Chiseling => ChiselingGrade,
        _ => ProcessGrade.None
    };

    /// <summary>
    /// Moves to the next process. A retried process keeps its best grade (CR-11),
    /// and finishing an already-passed process never rewinds progress.
    /// </summary>
    public void Complete(ProcessId process, ProcessGrade grade = ProcessGrade.None)
    {
        if (grade > GetGrade(process))
            SetGrade(process, grade);

        if (NextProcess <= process)
            NextProcess = process < ProcessId.Completed ? process + 1 : ProcessId.Completed;
    }

    /// <summary>Clears progress for a new game. Settings are intentionally untouched.</summary>
    public void Reset()
    {
        NextProcess = ProcessId.Prologue;
        MakmeokGrade = ProcessGrade.None;
        SawingGrade = ProcessGrade.None;
        ChiselingGrade = ProcessGrade.None;
    }

    void SetGrade(ProcessId process, ProcessGrade grade)
    {
        switch (process)
        {
            case ProcessId.Makmeok: MakmeokGrade = grade; break;
            case ProcessId.Sawing: SawingGrade = grade; break;
            case ProcessId.Chiseling: ChiselingGrade = grade; break;
        }
    }
}
