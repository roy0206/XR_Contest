using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Stands in for Google STT with no credentials and no microphone. It walks the suggested
/// question list so the rest of the pipeline receives realistic Korean input every press.
/// </summary>
public sealed class MockSpeechToTextService : IAiSpeechToTextService
{
    const int LatencyMilliseconds = 250;

    readonly AiDialogueBank _dialogue;
    int _index;

    public MockSpeechToTextService(AiDialogueBank dialogue) => _dialogue = dialogue ?? AiDialogueBank.CreateDefault();

    public bool IsMock => true;

    public async Task<AiTranscript> TranscribeAsync(
        AiAudioSample audio,
        IReadOnlyList<string> phraseHints,
        CancellationToken cancellationToken)
    {
        await Task.Delay(LatencyMilliseconds, cancellationToken);

        var questions = _dialogue.GetSuggestions(AiProcessContextRegistry.Current.Process, 8);
        if (questions.Count == 0) return new AiTranscript("지금 뭘 해야 해?", 1f);

        var question = questions[_index % questions.Count].Question;
        _index++;
        return new AiTranscript(question, 1f);
    }
}

/// <summary>
/// Stands in for Solar. Answers from the knowledge base so the safety filter, subtitle timing
/// and HUD all exercise real content instead of a placeholder string.
/// </summary>
public sealed class MockChatService : IAiChatService
{
    const int LatencyMilliseconds = 450;

    readonly AiKnowledgeBase _knowledge;
    readonly AiDialogueBank _dialogue;

    public MockChatService(AiKnowledgeBase knowledge, AiDialogueBank dialogue)
    {
        _knowledge = knowledge ?? AiKnowledgeBase.CreateEmpty();
        _dialogue = dialogue ?? AiDialogueBank.CreateDefault();
    }

    public bool IsMock => true;

    public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(LatencyMilliseconds, cancellationToken);

        var question = LastUserMessage(request);
        var context = AiProcessContextRegistry.Current;

        var offline = _dialogue.GetOfflineAnswer(question, context);
        var matches = _knowledge.Search(question, context, 1);
        if (matches.Count > 0 && !string.IsNullOrWhiteSpace(matches[0].Summary))
            offline = matches[0].Summary;
        else
        {
            var process = _knowledge.GetProcess(context.Process);
            if (process != null && process.KeyPoints.Count > 0)
                offline = process.KeyPoints[0];
        }

        return new AiChatResult(offline);
    }

    static string LastUserMessage(AiChatRequest request)
    {
        if (request == null) return null;
        for (var i = request.Messages.Count - 1; i >= 0; i--)
            if (request.Messages[i].Role == AiChatRole.User)
                return request.Messages[i].Content;
        return null;
    }
}

/// <summary>
/// Stands in for CLOVA Voice. Returns no clip: the subtitle is held for the estimated
/// speaking time instead, so 답변 중 state timing still matches a real voice.
/// </summary>
public sealed class MockTextToSpeechService : IAiTextToSpeechService
{
    const int LatencyMilliseconds = 120;

    public bool IsMock => true;

    public async Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        await Task.Delay(LatencyMilliseconds, cancellationToken);
        return null;
    }
}
