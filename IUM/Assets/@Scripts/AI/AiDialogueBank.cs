using System;
using System.Collections.Generic;

/// <summary>
/// A question the player can pick with the ray + trigger when speech recognition keeps failing
/// (F-013 3.4). <see cref="FixedAnswer"/> also makes the question answerable while offline.
/// </summary>
[Serializable]
public sealed class AiSuggestedQuestion
{
    /// <summary>Null means the question fits any process.</summary>
    public ProcessId? Process { get; set; }

    public string Question { get; set; }

    /// <summary>Used offline or when the model fails. Empty means "ask the model".</summary>
    public string FixedAnswer { get; set; }
}

/// <summary>Per-process fallback line used when no AI answer can be produced.</summary>
[Serializable]
public sealed class AiFallbackHint
{
    public ProcessId Process { get; set; }
    public string Hint { get; set; }
}

/// <summary>
/// 고정 대사 모음 (7단계 6번). Every line the player can hear without the model lives here so
/// writers can edit them without a code change, and so an outage still produces in-character text.
/// </summary>
[Serializable]
public sealed class AiDialogueBank
{
    public string RecognitionFirstFailure { get; set; } = "잘 못 들었어. 다시 한 번 말해 줄래?";
    public string RecognitionRepeatedFailure { get; set; } = "아직 잘 안 들리네. 아래에서 궁금한 걸 골라 볼래?";
    public string RecordingTooShort { get; set; } = "버튼을 누른 채로 천천히 말해 줄래?";
    public string MicrophoneUnavailable { get; set; } = "마이크를 쓸 수 없어서 지금은 목록에서 골라야 해.";
    public string Offline { get; set; } = "지금은 연결이 끊겨서 준비된 설명만 해 줄 수 있어. 아래에서 골라 볼래?";
    public string ServiceError { get; set; } = "지금은 대답을 만들지 못했어. 잠시 뒤에 다시 물어봐 줄래?";
    public string Blocked { get; set; } = "그건 내가 도와줄 수 있는 내용이 아니야. 지금 하는 작업 이야기를 해 볼까?";
    public string Unknown { get; set; } = "그 부분은 내가 가진 자료만으로는 정확히 말하기 어려워.";
    public string InputLocked { get; set; } = "지금은 노장님 말씀에 집중하자.";
    public string OffTopicSteer { get; set; } = "그 얘기는 나중에 더 하고, 지금은 하던 작업부터 마무리해 볼까?";

    /// <summary>Spoken while the answer is being generated is not desirable; this is text-only.</summary>
    public string Thinking { get; set; } = "잠깐만, 생각해 볼게.";

    public List<AiSuggestedQuestion> Suggestions { get; set; } = new();
    public List<AiFallbackHint> FallbackHints { get; set; } = new();

    public static AiDialogueBank CreateDefault() => new();

    public void Prepare()
    {
        Suggestions ??= new List<AiSuggestedQuestion>();
        FallbackHints ??= new List<AiFallbackHint>();

        if (Suggestions.Count != 0) return;

        // Without a data file the selection list would be empty, which is the one state
        // that leaves a player with a broken microphone unable to ask anything.
        Suggestions.Add(new AiSuggestedQuestion
        {
            Question = "지금 뭘 해야 해?",
            FixedAnswer = "지금 하는 작업의 목표부터 다시 보자. 노장님이 말씀하신 순서대로 하나씩 해 보면 돼."
        });
        Suggestions.Add(new AiSuggestedQuestion
        {
            Question = "이 도구는 어떻게 써?",
            FixedAnswer = "도구는 손잡이를 잡고 몸에서 멀어지는 방향으로 천천히 움직이면 돼. 힘보다 방향이 중요해."
        });
        Suggestions.Add(new AiSuggestedQuestion
        {
            Question = "왜 이렇게 해야 해?",
            FixedAnswer = "전통 목조건축은 못 없이 부재끼리 맞물려 힘을 받아. 그래서 치수와 방향이 조금만 어긋나도 짜임이 헐거워져."
        });
    }

    /// <summary>Process-specific questions first, then the general ones, up to <paramref name="max"/>.</summary>
    public List<AiSuggestedQuestion> GetSuggestions(ProcessId process, int max)
    {
        var results = new List<AiSuggestedQuestion>();
        if (max <= 0) return results;

        for (var i = 0; i < Suggestions.Count && results.Count < max; i++)
            if (Suggestions[i].Process == process) results.Add(Suggestions[i]);

        for (var i = 0; i < Suggestions.Count && results.Count < max; i++)
            if (!Suggestions[i].Process.HasValue) results.Add(Suggestions[i]);

        return results;
    }

    public string GetFallbackHint(AiProcessContext context)
    {
        if (context != null)
            for (var i = 0; i < FallbackHints.Count; i++)
                if (FallbackHints[i].Process == context.Process && !string.IsNullOrWhiteSpace(FallbackHints[i].Hint))
                    return FallbackHints[i].Hint;

        return Unknown;
    }

    /// <summary>Answer prepared for a picked question, or the process fallback hint.</summary>
    public string GetOfflineAnswer(string question, AiProcessContext context)
    {
        if (!string.IsNullOrWhiteSpace(question))
        {
            for (var i = 0; i < Suggestions.Count; i++)
            {
                var suggestion = Suggestions[i];
                if (string.IsNullOrWhiteSpace(suggestion.FixedAnswer)) continue;
                if (string.Equals(suggestion.Question, question, StringComparison.OrdinalIgnoreCase))
                    return suggestion.FixedAnswer;
            }
        }

        return GetFallbackHint(context);
    }
}
