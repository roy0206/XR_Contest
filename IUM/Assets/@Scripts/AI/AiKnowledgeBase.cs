using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>One piece of grounding material: a term, a technique or a verified reference.</summary>
[Serializable]
public sealed class AiKnowledgeEntry
{
    public string Id { get; set; }
    public string Title { get; set; }

    /// <summary>Other spellings players may use, including STT mistakes worth catching.</summary>
    public List<string> Aliases { get; set; } = new();

    /// <summary>The text handed to the model. Keep it short and factual.</summary>
    public string Summary { get; set; }

    public List<string> Keywords { get; set; } = new();

    /// <summary>Where the fact came from. Shown in the prompt so the model can attribute it.</summary>
    public string Source { get; set; }

    /// <summary>Restricts the entry to one process. Null means it applies everywhere.</summary>
    public ProcessId? Process { get; set; }
}

/// <summary>현재 공정 설명 데이터 (F-012 2.3). Filled per process as 3~6단계 are built.</summary>
[Serializable]
public sealed class AiProcessKnowledge
{
    public ProcessId Process { get; set; }
    public string Title { get; set; }

    /// <summary>What the process is for, in one sentence.</summary>
    public string Goal { get; set; }

    /// <summary>Ordered points 이음이 may explain.</summary>
    public List<string> KeyPoints { get; set; } = new();

    /// <summary>Typical failures, used to explain 직전 실패 원인.</summary>
    public List<string> CommonMistakes { get; set; } = new();
}

/// <summary>
/// 응답 근거 자료 (F-012 2.3). Retrieval is deliberately simple keyword containment:
/// Korean particles attach to nouns, so substring matching beats token equality here,
/// and a wrong retrieval only costs one unused sentence in the prompt.
/// </summary>
[Serializable]
public sealed class AiKnowledgeBase
{
    public List<AiKnowledgeEntry> Terms { get; set; } = new();
    public List<AiProcessKnowledge> Processes { get; set; } = new();
    public List<AiKnowledgeEntry> References { get; set; } = new();

    [JsonIgnore] readonly List<AiKnowledgeEntry> _all = new();
    [JsonIgnore] readonly List<string> _phraseHints = new();

    public static AiKnowledgeBase CreateEmpty() => new();

    /// <summary>Phrase hints for STT so 전통 목조건축 용어 survives recognition.</summary>
    [JsonIgnore] public IReadOnlyList<string> PhraseHints => _phraseHints;

    [JsonIgnore] public bool IsEmpty => _all.Count == 0 && Processes.Count == 0;

    public void Prepare()
    {
        Terms ??= new List<AiKnowledgeEntry>();
        Processes ??= new List<AiProcessKnowledge>();
        References ??= new List<AiKnowledgeEntry>();

        _all.Clear();
        _all.AddRange(Terms);
        _all.AddRange(References);

        _phraseHints.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _all.Count; i++)
        {
            var entry = _all[i];
            entry.Aliases ??= new List<string>();
            entry.Keywords ??= new List<string>();
            AddHint(seen, entry.Title);
            for (var a = 0; a < entry.Aliases.Count; a++) AddHint(seen, entry.Aliases[a]);
            for (var k = 0; k < entry.Keywords.Count; k++) AddHint(seen, entry.Keywords[k]);
        }

        for (var i = 0; i < Processes.Count; i++)
        {
            Processes[i].KeyPoints ??= new List<string>();
            Processes[i].CommonMistakes ??= new List<string>();
            AddHint(seen, Processes[i].Title);
        }
    }

    public AiProcessKnowledge GetProcess(ProcessId process)
    {
        for (var i = 0; i < Processes.Count; i++)
            if (Processes[i].Process == process) return Processes[i];
        return null;
    }

    /// <summary>Highest-scoring entries for the question, biased toward the current process.</summary>
    public List<AiKnowledgeEntry> Search(string question, AiProcessContext context, int maxResults)
    {
        var results = new List<AiKnowledgeEntry>();
        if (_all.Count == 0 || maxResults <= 0) return results;

        var haystack = question ?? string.Empty;
        var scored = new List<(AiKnowledgeEntry entry, int score)>();

        for (var i = 0; i < _all.Count; i++)
        {
            var entry = _all[i];
            var score = 0;

            if (Contains(haystack, entry.Title)) score += 5;
            for (var a = 0; a < entry.Aliases.Count; a++)
                if (Contains(haystack, entry.Aliases[a])) score += 4;
            for (var k = 0; k < entry.Keywords.Count; k++)
                if (Contains(haystack, entry.Keywords[k])) score += 3;

            if (context != null)
            {
                for (var f = 0; f < context.FocusKeywords.Count; f++)
                {
                    var focus = context.FocusKeywords[f];
                    if (Contains(entry.Title, focus) || ContainsAny(entry.Keywords, focus)) score += 2;
                }

                // A process-scoped entry is only useful during that process.
                if (entry.Process.HasValue)
                    score += entry.Process.Value == context.Process ? 2 : -6;
            }

            if (score > 0) scored.Add((entry, score));
        }

        scored.Sort((left, right) => right.score.CompareTo(left.score));
        for (var i = 0; i < scored.Count && results.Count < maxResults; i++)
            results.Add(scored[i].entry);

        return results;
    }

    void AddHint(HashSet<string> seen, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        if (trimmed.Length < 2) return;
        if (seen.Add(trimmed)) _phraseHints.Add(trimmed);
    }

    static bool Contains(string haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack) &&
        !string.IsNullOrWhiteSpace(needle) &&
        haystack.IndexOf(needle.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

    static bool ContainsAny(List<string> values, string needle)
    {
        for (var i = 0; i < values.Count; i++)
            if (Contains(values[i], needle)) return true;
        return false;
    }
}
