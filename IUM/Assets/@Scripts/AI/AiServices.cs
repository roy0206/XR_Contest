using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Raw microphone capture handed to the STT service.</summary>
public sealed class AiAudioSample
{
    public AiAudioSample(float[] samples, int sampleRate, int channels)
    {
        Samples = samples ?? new float[0];
        SampleRate = Mathf.Max(1, sampleRate);
        Channels = Mathf.Max(1, channels);

        var peak = 0f;
        for (var i = 0; i < Samples.Length; i++)
        {
            var value = Samples[i] < 0f ? -Samples[i] : Samples[i];
            if (value > peak) peak = value;
        }

        PeakAmplitude = peak;
    }

    public float[] Samples { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public float PeakAmplitude { get; }
    public float Duration => Samples.Length / (float)(SampleRate * Channels);
    public bool IsEmpty => Samples.Length == 0;
}

public readonly struct AiTranscript
{
    public AiTranscript(string text, float confidence)
    {
        Text = text;
        Confidence = confidence;
    }

    public string Text { get; }
    public float Confidence { get; }
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public static AiTranscript Empty => new(null, 0f);
}

public enum AiChatRole
{
    System,
    User,
    Assistant
}

public sealed class AiChatMessage
{
    public AiChatMessage(AiChatRole role, string content)
    {
        Role = role;
        Content = content;
    }

    public AiChatRole Role { get; }
    public string Content { get; }

    public string RoleName => Role switch
    {
        AiChatRole.System => "system",
        AiChatRole.Assistant => "assistant",
        _ => "user"
    };
}

public sealed class AiChatRequest
{
    public List<AiChatMessage> Messages { get; } = new();
    public float Temperature { get; set; } = 0.4f;
    public int MaxTokens { get; set; } = 400;
}

public readonly struct AiChatResult
{
    public AiChatResult(string content) => Content = content;

    public string Content { get; }
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);
}

/// <summary>
/// 음성 인식. Implementations throw <see cref="AiServiceException"/> and never return
/// a partial result, so the conversation layer only has to handle success or failure.
/// </summary>
public interface IAiSpeechToTextService
{
    bool IsMock { get; }

    Task<AiTranscript> TranscribeAsync(
        AiAudioSample audio,
        IReadOnlyList<string> phraseHints,
        CancellationToken cancellationToken);
}

/// <summary>이음이 응답 생성. Upstage Solar in production, canned answers in mock mode.</summary>
public interface IAiChatService
{
    bool IsMock { get; }

    Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Supplies the Authorization header for Google Cloud calls. Implemented by the service
/// account flow and by the OAuth refresh-token flow, so the services do not care which
/// credential type the project was able to issue.
/// </summary>
public interface IGoogleTokenProvider
{
    bool IsUsable { get; }

    Task<Dictionary<string, string>> BuildAuthorizationHeadersAsync(CancellationToken cancellationToken);
}

/// <summary>음성 합성. A null clip means "no audio"; the caller still shows the subtitle.</summary>
public interface IAiTextToSpeechService
{
    bool IsMock { get; }

    Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken);
}
