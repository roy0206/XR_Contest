using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Naver CLOVA Voice Premium. WAV is requested instead of MP3 so decoding is handled by
/// <see cref="WavCodec"/> and never depends on a platform audio decoder.
/// </summary>
public sealed class ClovaVoiceTextToSpeechService : IAiTextToSpeechService
{
    /// <summary>CLOVA Voice rejects requests over 2000 characters.</summary>
    const int MaxRequestChars = 2000;

    readonly AiTtsConfig _config;
    readonly AiSecrets _secrets;
    readonly bool _verbose;

    public ClovaVoiceTextToSpeechService(AiConfig config, AiSecrets secrets)
    {
        _config = config?.Tts ?? new AiTtsConfig();
        _secrets = secrets ?? new AiSecrets();
        _verbose = config?.VerboseLogging ?? false;
    }

    public bool IsMock => false;

    public async Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > MaxRequestChars) text = text.Substring(0, MaxRequestChars);

        var form = new StringBuilder()
            .Append("speaker=").Append(UnityWebRequest.EscapeURL(_config.Speaker))
            .Append("&volume=").Append(_config.Volume)
            .Append("&speed=").Append(_config.Speed)
            .Append("&pitch=").Append(_config.Pitch)
            .Append("&format=").Append(UnityWebRequest.EscapeURL(_config.Format))
            .Append("&text=").Append(UnityWebRequest.EscapeURL(text))
            .ToString();

        var headers = new Dictionary<string, string>
        {
            ["X-NCP-APIGW-API-KEY-ID"] = _secrets.ClovaClientId,
            ["X-NCP-APIGW-API-KEY"] = _secrets.ClovaClientSecret
        };

        var response = await AiHttp.PostFormAsync(
            _config.Endpoint, form, headers, _config.TimeoutSeconds, cancellationToken);

        if (!response.Success)
            throw response.ToException("CLOVA Voice");

        cancellationToken.ThrowIfCancellationRequested();

        var clip = WavCodec.DecodeToClip(response.Data, "IeumiVoice");
        if (clip == null)
            throw new AiServiceException("CLOVA Voice returned audio that could not be decoded.");

        if (_verbose)
            Debug.Log($"[AI] TTS {text.Length}자 -> {clip.length:0.0}s ({_config.Speaker})");

        return clip;
    }
}
