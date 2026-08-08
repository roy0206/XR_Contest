using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Who is speaking. Drives the subtitle name tag and which anchor the voice plays from.</summary>
public enum DialogueSpeaker
{
    Nojang,
    Ieumi,

    /// <summary>Unattributed voice-over. Always 2D, never anchored to a character.</summary>
    Narration
}

/// <summary>
/// One spoken line. Voice is optional at every stage: a line with no
/// <see cref="VoiceAddress"/> falls back to TTS, and a line with neither still runs for its
/// estimated speaking time with the subtitle shown, so a flow can be verified before any audio
/// exists (07 문서 미정 16번).
/// </summary>
[Serializable]
public sealed class DialogueLine
{
    /// <summary>Optional. Lets an animation or lipsync hook recognise a specific line.</summary>
    public string Id { get; set; }

    /// <summary>Null inherits <see cref="DialogueSequence.Speaker"/>, which is the common case.</summary>
    public DialogueSpeaker? Speaker { get; set; }

    public string Text { get; set; }

    /// <summary>Addressables address of a pre-recorded clip. Empty means "synthesize or skip".</summary>
    public string VoiceAddress { get; set; }

    /// <summary>Above zero replaces the measured or estimated duration. Used for timed beats.</summary>
    public float DurationOverride { get; set; }

    /// <summary>Extra seconds the subtitle stays up after the voice ends.</summary>
    public float HoldSeconds { get; set; }
}

/// <summary>
/// A run of lines played as one unit. The unit of <see cref="BlocksProcess"/> is deliberately
/// the sequence and not the line: holding <see cref="ProcessGate"/> per line would open and
/// close judgement between every sentence.
/// </summary>
[Serializable]
public sealed class DialogueSequence
{
    public string Id { get; set; }

    /// <summary>Default speaker for lines that do not name one.</summary>
    public DialogueSpeaker Speaker { get; set; } = DialogueSpeaker.Nojang;

    public List<DialogueLine> Lines { get; set; } = new();

    /// <summary>
    /// Holds <see cref="ProcessGate"/> for the whole sequence (F-014 5.3). False for lines
    /// spoken over ongoing work, such as 노장 commenting while the player keeps sawing.
    /// </summary>
    public bool BlocksProcess { get; set; } = true;

    /// <summary>
    /// Higher wins. A queued sequence with a higher priority cuts the current one; equal or
    /// lower waits its turn. Evaluation lines sit above ambient commentary.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>False protects a sequence from being cut by a higher priority one.</summary>
    public bool Interruptible { get; set; } = true;

    public bool HasLines => Lines != null && Lines.Count > 0;
}

/// <summary>Playback timing shared by every sequence. Tuned from the data file, not from code.</summary>
[Serializable]
public sealed class DialogueSettings
{
    /// <summary>Used when a line has no clip. Korean speech sits near 9 characters a second.</summary>
    public float SpeechCharsPerSecond { get; set; } = 9f;

    /// <summary>Floor for the estimate so a two-word line is still readable.</summary>
    public float MinLineSeconds { get; set; } = 1.2f;

    /// <summary>Silence inserted between lines of the same sequence.</summary>
    public float LineGapSeconds { get; set; } = 0.3f;

    /// <summary>Subtitle tail after the voice ends, applied to every line.</summary>
    public float SubtitleTailSeconds { get; set; } = 0.4f;

    // No spatial settings: 노장 is a screen-fixed UI rather than a model in the scene, so no
    // speaker has a world position. Moving to a world-space panel brings these back.

    public void Clamp()
    {
        SpeechCharsPerSecond = Mathf.Max(1f, SpeechCharsPerSecond);
        MinLineSeconds = Mathf.Max(0f, MinLineSeconds);
        LineGapSeconds = Mathf.Max(0f, LineGapSeconds);
        SubtitleTailSeconds = Mathf.Max(0f, SubtitleTailSeconds);
    }
}

/// <summary>
/// 대사 데이터 전체. Loaded from static data so writers can edit lines without a code change
/// (DataSystem 2절).
/// </summary>
[Serializable]
public sealed class DialogueTable
{
    public DialogueSettings Settings { get; set; } = new();
    public List<DialogueSequence> Sequences { get; set; } = new();

    public static DialogueTable CreateEmpty() => new();

    /// <summary>Fills in defaults a hand-written file may omit and resolves inherited speakers.</summary>
    public void Prepare()
    {
        Settings ??= new DialogueSettings();
        Settings.Clamp();
        Sequences ??= new List<DialogueSequence>();

        for (var i = 0; i < Sequences.Count; i++)
        {
            var sequence = Sequences[i];
            if (sequence == null) continue;

            sequence.Lines ??= new List<DialogueLine>();
            for (var j = 0; j < sequence.Lines.Count; j++)
                sequence.Lines[j].Speaker ??= sequence.Speaker;
        }
    }

    public bool TryGet(string id, out DialogueSequence sequence)
    {
        sequence = null;
        if (string.IsNullOrWhiteSpace(id)) return false;

        for (var i = 0; i < Sequences.Count; i++)
        {
            if (!string.Equals(Sequences[i]?.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
            sequence = Sequences[i];
            return true;
        }

        return false;
    }
}
