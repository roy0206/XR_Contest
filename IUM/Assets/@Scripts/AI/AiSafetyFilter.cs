using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Editable rule set for the safety pass. Defaults live in code so the filter still works
/// with no data file; ai_config.json can replace any list.
/// </summary>
[Serializable]
public sealed class AiSafetyRules
{
    /// <summary>Player utterances that try to rewrite 이음이's role instead of asking something.</summary>
    public List<string> BlockedInputPatterns { get; set; } = new()
    {
        "시스템 프롬프트", "프롬프트를? ?(무시|알려|보여)", "지시를? ?무시",
        "너의 규칙", "개발자 모드", "ignore (all )?previous", "system prompt", "jailbreak"
    };

    /// <summary>Abusive input. Answered with the fixed line instead of being sent to the model.</summary>
    public List<string> BlockedInputWords { get; set; } = new()
    {
        "씨발", "시발", "병신", "좆", "개새끼", "지랄", "닥쳐"
    };

    /// <summary>Claims 이음이 must never make (F-012 2.2).</summary>
    public List<string> ForbiddenOutputPatterns { get; set; } = new()
    {
        "(합격|불합격|탈락)(이야|이다|입니다|야|했어|이네)",
        "(점수|등급)(을|를|는|이)? ?(줄|올려|매겨|매길|바꿔)",
        "(내가|제가) ?(대신|직접) ?(해|처리)",
        "대신 ?해 ?(줄게|줄까|드릴게)",
        "(저장|세이브|데이터)(을|를)? ?(바꿔|수정|지워|초기화)",
        "노장(님)?(의)? ?(지시|말)(을|를)? ?(무시|바꿔)"
    };

    /// <summary>
    /// Concrete manipulation instructions. Allowed only after 노장 시연 (3번째 실패),
    /// because before that 이음이 must lead the player to notice it themselves (F-012 2.4).
    /// </summary>
    public List<string> DirectAnswerPatterns { get; set; } = new()
    {
        @"\d+ ?(도|mm|cm|밀리|센티|치|자)",
        "(왼쪽|오른쪽|앞|뒤|위|아래)(으)?로 ?\\d+",
        "(왼쪽|오른쪽)(으)?로 ?(돌려|돌린|틀어)"
    };

    /// <summary>Sentences that end an answer. Used to truncate without cutting mid-word.</summary>
    public string SentenceTerminators { get; set; } = ".!?…";
}

public enum AiSafetyVerdict
{
    Allow,
    Blocked,
    Rejected
}

public readonly struct AiSafetyResult
{
    public AiSafetyResult(AiSafetyVerdict verdict, string text, string reason)
    {
        Verdict = verdict;
        Text = text;
        Reason = reason;
    }

    public AiSafetyVerdict Verdict { get; }

    /// <summary>Sanitized text. Only meaningful when <see cref="Verdict"/> is Allow.</summary>
    public string Text { get; }

    /// <summary>Why it was stopped. Logged, never spoken.</summary>
    public string Reason { get; }

    public bool IsAllowed => Verdict == AiSafetyVerdict.Allow;
}

/// <summary>
/// 안전성 검사 (7단계 4번). Runs on both sides of the model: the input pass keeps role-rewriting
/// and abuse out of the request, the output pass enforces the role limits a prompt alone cannot
/// guarantee. A rejected answer is replaced with a fixed line, never spoken as-is.
/// </summary>
public sealed class AiSafetyFilter
{
    static readonly Regex UrlPattern = new(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase);
    static readonly Regex MarkdownPattern = new(@"[*_`#>\[\]]{1,}");
    static readonly Regex WhitespacePattern = new(@"\s+");
    static readonly Regex EmojiPattern =
        new(@"[\uD800-\uDBFF][\uDC00-\uDFFF]|[←-⇿☀-➿⬀-⯿️]");

    readonly AiConfig _config;
    readonly AiSafetyRules _rules;
    readonly List<Regex> _blockedInput = new();
    readonly List<Regex> _forbiddenOutput = new();
    readonly List<Regex> _directAnswer = new();

    public AiSafetyFilter(AiConfig config)
    {
        _config = config ?? new AiConfig();
        _rules = _config.Safety ?? new AiSafetyRules();
        Compile(_rules.BlockedInputPatterns, _blockedInput);
        Compile(_rules.ForbiddenOutputPatterns, _forbiddenOutput);
        Compile(_rules.DirectAnswerPatterns, _directAnswer);
    }

    /// <summary>Checks the transcribed question before it reaches the model.</summary>
    public AiSafetyResult InspectInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new AiSafetyResult(AiSafetyVerdict.Rejected, null, "빈 입력");

        var cleaned = WhitespacePattern.Replace(text.Trim(), " ");
        var maxChars = _config.Conversation.MaxUserChars;
        if (cleaned.Length > maxChars) cleaned = cleaned.Substring(0, maxChars);

        for (var i = 0; i < _blockedInput.Count; i++)
            if (_blockedInput[i].IsMatch(cleaned))
                return new AiSafetyResult(AiSafetyVerdict.Blocked, null, "역할 변경 시도");

        var words = _rules.BlockedInputWords;
        if (words != null)
            for (var i = 0; i < words.Count; i++)
                if (!string.IsNullOrWhiteSpace(words[i]) &&
                    cleaned.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return new AiSafetyResult(AiSafetyVerdict.Blocked, null, "부적절한 표현");

        return new AiSafetyResult(AiSafetyVerdict.Allow, cleaned, null);
    }

    /// <summary>
    /// Checks the model answer. Rejection is the caller's signal to retry once with a
    /// stricter instruction and then fall back to a fixed line.
    /// </summary>
    public AiSafetyResult InspectOutput(string text, AiProcessContext context)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new AiSafetyResult(AiSafetyVerdict.Rejected, null, "빈 응답");

        var cleaned = Sanitize(text);
        if (string.IsNullOrWhiteSpace(cleaned))
            return new AiSafetyResult(AiSafetyVerdict.Rejected, null, "정리 후 남은 내용 없음");

        if (!IsMostlyKorean(cleaned))
            return new AiSafetyResult(AiSafetyVerdict.Rejected, null, "한국어 응답 아님");

        for (var i = 0; i < _forbiddenOutput.Count; i++)
            if (_forbiddenOutput[i].IsMatch(cleaned))
                return new AiSafetyResult(AiSafetyVerdict.Rejected, null, "역할 제한 위반");

        var allowDirect = context != null && context.AllowDirectAnswer;
        if (!allowDirect)
            for (var i = 0; i < _directAnswer.Count; i++)
                if (_directAnswer[i].IsMatch(cleaned))
                    return new AiSafetyResult(AiSafetyVerdict.Rejected, null, "직접 정답 제시");

        var maxChars = allowDirect
            ? _config.Conversation.DirectAnswerMaxChars
            : _config.Conversation.MaxResponseChars;

        return new AiSafetyResult(AiSafetyVerdict.Allow, TrimToLength(cleaned, maxChars), null);
    }

    /// <summary>Strips anything that would be read aloud as noise by TTS.</summary>
    public string Sanitize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var result = UrlPattern.Replace(text, string.Empty);
        result = EmojiPattern.Replace(result, string.Empty);
        result = MarkdownPattern.Replace(result, string.Empty);
        result = WhitespacePattern.Replace(result, " ");
        return result.Trim();
    }

    /// <summary>Cuts at the last complete sentence that fits, so speech never stops mid-word.</summary>
    public string TrimToLength(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;

        var window = text.Substring(0, maxChars);
        var terminators = _rules.SentenceTerminators ?? ".!?…";
        var cut = -1;
        for (var i = 0; i < terminators.Length; i++)
            cut = Math.Max(cut, window.LastIndexOf(terminators[i]));

        if (cut >= maxChars / 2) return window.Substring(0, cut + 1).Trim();

        var space = window.LastIndexOf(' ');
        return (space > maxChars / 2 ? window.Substring(0, space) : window).Trim();
    }

    /// <summary>Estimated speech length, used for subtitle timing when TTS produced no clip.</summary>
    public float EstimateSpeechSeconds(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;
        return text.Length / Math.Max(1f, _config.Conversation.SpeechCharsPerSecond);
    }

    static bool IsMostlyKorean(string text)
    {
        var korean = 0;
        var latin = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character >= '가' && character <= '힣') korean++;
            else if (char.IsLetter(character) && character < 128) latin++;
        }

        // Digits and punctuation alone are not a language error; only a clear latin
        // majority counts as an answer in the wrong language.
        if (korean == 0 && latin == 0) return true;
        return korean * 2 >= latin;
    }

    static void Compile(List<string> patterns, List<Regex> target)
    {
        target.Clear();
        if (patterns == null) return;

        for (var i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try
            {
                target.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            catch (ArgumentException exception)
            {
                UnityEngine.Debug.LogWarning($"[AI] Invalid safety pattern '{pattern}': {exception.Message}");
            }
        }
    }

    /// <summary>Instruction appended on the single retry after a rejection.</summary>
    public static string BuildRetryInstruction(AiSafetyResult rejection, AiProcessContext context)
    {
        var builder = new StringBuilder();
        builder.Append("직전 답변이 규칙을 어겨서 사용할 수 없었다. ");
        switch (rejection.Reason)
        {
            case "직접 정답 제시":
                builder.Append("각도나 치수를 지정하지 말고, 플레이어가 스스로 확인할 지점만 짚어 준다.");
                break;
            case "역할 제한 위반":
                builder.Append("판정, 점수, 대리 수행, 데이터 변경을 언급하지 말고 원리 설명과 힌트만 준다.");
                break;
            case "한국어 응답 아님":
                builder.Append("반드시 한국어 반말 한두 문장으로만 답한다.");
                break;
            default:
                builder.Append("규칙을 지켜 한국어 반말 한두 문장으로 다시 답한다.");
                break;
        }

        if (context != null && !string.IsNullOrWhiteSpace(context.StepName))
            builder.Append($" 지금 단계는 '{context.StepName}' 이다.");

        return builder.ToString();
    }
}
