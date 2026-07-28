using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Google Cloud Text-to-Speech. LINEAR16 is requested so the response decodes through
/// <see cref="WavCodec"/> instead of a platform audio decoder, and the same Google credential
/// as the STT fallback is reused.
/// </summary>
public sealed class GoogleTextToSpeechService : IAiTextToSpeechService
{
    /// <summary>The API rejects requests over 5000 bytes; answers are far shorter than this.</summary>
    const int MaxRequestChars = 2000;

    readonly AiTtsConfig _config;
    readonly AiSecrets _secrets;
    readonly IGoogleTokenProvider _tokens;
    readonly bool _verbose;

    public GoogleTextToSpeechService(
        AiConfig config,
        AiSecrets secrets,
        IGoogleTokenProvider tokens = null)
    {
        _config = config?.Tts ?? new AiTtsConfig();
        _secrets = secrets ?? new AiSecrets();
        _tokens = tokens != null && tokens.IsUsable ? tokens : null;
        _verbose = config?.VerboseLogging ?? false;
    }

    public bool IsMock => false;

    public async Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > MaxRequestChars) text = text.Substring(0, MaxRequestChars);

        var body = new JObject
        {
            ["input"] = new JObject { ["text"] = text },
            ["voice"] = new JObject
            {
                ["languageCode"] = _config.GoogleLanguageCode,
                ["name"] = _config.GoogleVoiceName
            },
            ["audioConfig"] = new JObject
            {
                ["audioEncoding"] = "LINEAR16",
                ["sampleRateHertz"] = _config.GoogleSampleRateHertz,
                ["speakingRate"] = _config.GoogleSpeakingRate,
                ["pitch"] = _config.GooglePitch,
                ["volumeGainDb"] = _config.GoogleVolumeGainDb
            }
        }.ToString(Newtonsoft.Json.Formatting.None);

        // A service account is the documented method; the other two are convenience paths.
        var headers = new Dictionary<string, string>();
        var url = _config.GoogleEndpoint;

        if (_tokens != null)
            headers = await _tokens.BuildAuthorizationHeadersAsync(cancellationToken);
        else if (!string.IsNullOrWhiteSpace(_secrets.GoogleAccessToken))
            headers["Authorization"] = $"Bearer {_secrets.GoogleAccessToken}";
        else
            url = $"{url}?key={UnityWebRequest.EscapeURL(_secrets.GoogleApiKey)}";

        var response = await AiHttp.PostJsonAsync(url, body, headers, _config.TimeoutSeconds, cancellationToken);
        if (!response.Success)
            throw response.ToException("Google TTS");

        cancellationToken.ThrowIfCancellationRequested();

        var audio = ParseAudioContent(response.Text);
        var clip = WavCodec.DecodeToClip(audio, "IeumiVoice");

        // LINEAR16 normally arrives as a WAV container; fall back to raw PCM just in case.
        if (clip == null)
        {
            var samples = WavCodec.DecodeRawPcm16(audio);
            if (samples.Length == 0)
                throw new AiServiceException("Google TTS returned audio that could not be decoded.");

            clip = AudioClip.Create("IeumiVoice", samples.Length, 1, _config.GoogleSampleRateHertz, false);
            clip.SetData(samples, 0);
        }

        if (_verbose)
            Debug.Log($"[AI] TTS {text.Length}자 -> {clip.length:0.0}s ({_config.GoogleVoiceName})");

        return clip;
    }

    static byte[] ParseAudioContent(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new AiServiceException("Google TTS returned an empty response.");

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception exception)
        {
            throw new AiServiceException($"Google TTS returned unreadable JSON: {exception.Message}");
        }

        if (root["error"] is JObject error)
            throw new AiServiceException($"Google TTS error: {error.Value<string>("message")}");

        var content = root.Value<string>("audioContent");
        if (string.IsNullOrWhiteSpace(content))
            throw new AiServiceException("Google TTS response contained no audio.");

        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException exception)
        {
            throw new AiServiceException($"Google TTS audio was not valid base64: {exception.Message}");
        }
    }
}
