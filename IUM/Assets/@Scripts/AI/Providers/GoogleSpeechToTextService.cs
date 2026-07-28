using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Google Cloud Speech-to-Text, synchronous recognize. The captured audio is downmixed to mono
/// and sent at its native sample rate, so no resampling error can degrade recognition.
/// </summary>
public sealed class GoogleSpeechToTextService : IAiSpeechToTextService
{
    readonly AiSttConfig _config;
    readonly AiSecrets _secrets;
    readonly IGoogleTokenProvider _tokens;
    readonly bool _verbose;

    public GoogleSpeechToTextService(
        AiConfig config,
        AiSecrets secrets,
        IGoogleTokenProvider tokens = null)
    {
        _config = config?.Stt ?? new AiSttConfig();
        _secrets = secrets ?? new AiSecrets();
        _tokens = tokens != null && tokens.IsUsable ? tokens : null;
        _verbose = config?.VerboseLogging ?? false;
    }

    public bool IsMock => false;

    public async Task<AiTranscript> TranscribeAsync(
        AiAudioSample audio,
        IReadOnlyList<string> phraseHints,
        CancellationToken cancellationToken)
    {
        if (audio == null || audio.IsEmpty) return AiTranscript.Empty;

        // Encoding a 15 second clip is millions of samples; keep it off the frame.
        var content = await Task.Run(
            () => Convert.ToBase64String(WavCodec.EncodeRawPcm16(WavCodec.Downmix(audio.Samples, audio.Channels))),
            cancellationToken);

        var recognitionConfig = new JObject
        {
            ["encoding"] = "LINEAR16",
            ["sampleRateHertz"] = audio.SampleRate,
            ["audioChannelCount"] = 1,
            ["languageCode"] = _config.LanguageCode,
            ["enableAutomaticPunctuation"] = _config.EnableAutomaticPunctuation
        };

        if (!string.IsNullOrWhiteSpace(_config.Model))
            recognitionConfig["model"] = _config.Model;

        var hints = BuildPhraseHints(phraseHints);
        if (hints != null)
            recognitionConfig["speechContexts"] = new JArray { hints };

        var body = new JObject
        {
            ["config"] = recognitionConfig,
            ["audio"] = new JObject { ["content"] = content }
        }.ToString(Newtonsoft.Json.Formatting.None);

        var headers = new Dictionary<string, string>();
        var url = _config.Endpoint;

        if (_tokens != null)
            headers = await _tokens.BuildAuthorizationHeadersAsync(cancellationToken);
        else if (!string.IsNullOrWhiteSpace(_secrets.GoogleAccessToken))
            headers["Authorization"] = $"Bearer {_secrets.GoogleAccessToken}";
        else
            url = $"{url}?key={UnityWebRequest.EscapeURL(_secrets.GoogleApiKey)}";

        var response = await AiHttp.PostJsonAsync(url, body, headers, _config.TimeoutSeconds, cancellationToken);
        if (!response.Success)
            throw response.ToException("Google STT");

        var transcript = ParseTranscript(response.Text, out var confidence);
        if (_verbose)
            Debug.Log($"[AI] STT {audio.Duration:0.0}s -> \"{transcript}\" ({confidence:0.00})");

        return new AiTranscript(transcript, confidence);
    }

    JObject BuildPhraseHints(IReadOnlyList<string> phraseHints)
    {
        if (!_config.UsePhraseHints || phraseHints == null || phraseHints.Count == 0) return null;

        var phrases = new JArray();
        var limit = Mathf.Min(phraseHints.Count, Mathf.Max(0, _config.MaxPhraseHints));
        for (var i = 0; i < limit; i++)
        {
            if (string.IsNullOrWhiteSpace(phraseHints[i])) continue;
            phrases.Add(phraseHints[i]);
        }

        if (phrases.Count == 0) return null;

        return new JObject
        {
            ["phrases"] = phrases,
            ["boost"] = _config.PhraseHintBoost
        };
    }

    static string ParseTranscript(string json, out float confidence)
    {
        confidence = 0f;
        if (string.IsNullOrWhiteSpace(json)) return null;

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception exception)
        {
            throw new AiServiceException($"Google STT returned unreadable JSON: {exception.Message}");
        }

        if (root["results"] is not JArray results || results.Count == 0) return null;

        var builder = new System.Text.StringBuilder();
        var scored = 0;

        foreach (var result in results)
        {
            if (result["alternatives"] is not JArray alternatives || alternatives.Count == 0) continue;

            var best = alternatives[0];
            var text = best.Value<string>("transcript");
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (builder.Length > 0) builder.Append(' ');
            builder.Append(text.Trim());

            var value = best["confidence"];
            if (value != null)
            {
                confidence += value.Value<float>();
                scored++;
            }
        }

        confidence = scored > 0 ? confidence / scored : 0f;
        return builder.ToString();
    }
}
