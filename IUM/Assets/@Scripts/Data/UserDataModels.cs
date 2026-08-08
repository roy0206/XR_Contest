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

/// <summary>
/// 이어하기로 돌아왔을 때 되돌릴 플레이어 자세. 어느 씬에서 저장했는지 함께 들고 있어, 좌표가
/// 엉뚱한 씬에 적용되는 일이 없다.
///
/// Flat floats rather than Vector3 and Quaternion: Newtonsoft walks Unity's derived properties on
/// those types (normalized, magnitude, eulerAngles) and writes a bloated, self-referential document.
///
/// Yaw only. Pitch and roll belong to the head — the HMD's in VR, the look module's on the desktop —
/// and restoring them would fight whatever is driving the view on the next run.
/// </summary>
[Serializable]
public sealed class PlayerPoseData
{
    public string Scene { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    [JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(Scene);

    [JsonIgnore]
    public Vector3 Position
    {
        get => new(X, Y, Z);
        set
        {
            X = value.x;
            Y = value.y;
            Z = value.z;
        }
    }

    public bool Matches(string sceneName) =>
        IsValid && string.Equals(Scene, sceneName, StringComparison.OrdinalIgnoreCase);
}

[Serializable]
public sealed class UserProgressData
{
    public ProcessId NextProcess { get; set; } = ProcessId.Prologue;

    /// <summary>
    /// 마지막으로 서 있던 자리. Null until a scene reports one, and cleared with the rest of the
    /// progress. 공정 자체는 처음부터 다시 시작하되 위치만 되돌린다.
    /// </summary>
    public PlayerPoseData PlayerPose { get; set; }
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
        PlayerPose = null;
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
