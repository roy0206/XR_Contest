using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if IUM_SHERPA_ONNX
using Eitan.Sherpa.Onnx.Unity.Mono.Components;
#endif

/// <summary>
/// On-device speech recognition through sherpa-onnx. Nothing leaves the headset, so a dead
/// venue network can no longer stop a player from asking a question.
///
/// The whole utterance is recognized once, after PTT is released, which is why the
/// non-streaming Korean transducer is the default model.
///
/// Compiled only when the IUM_SHERPA_ONNX define is set. Use
/// <c>IUM &gt; AI &gt; Install Local STT</c> to add the package and the define; without it the
/// project still builds and the factory falls back to Google or the mock.
/// </summary>
public sealed class LocalSpeechToTextService : IAiSpeechToTextService
{
    readonly AiSttConfig _config;
    readonly bool _verbose;

    public LocalSpeechToTextService(AiConfig config)
    {
        _config = config?.Stt ?? new AiSttConfig();
        _verbose = config?.VerboseLogging ?? false;
    }

    /// <summary>False when the sherpa-onnx package is not installed in this project.</summary>
    public static bool IsAvailable =>
#if IUM_SHERPA_ONNX
        true;
#else
        false;
#endif

    public bool IsMock => false;

#if IUM_SHERPA_ONNX
    OfflineSpeechRecognizerComponent _recognizer;
    Task _initialization;

    public async Task<AiTranscript> TranscribeAsync(
        AiAudioSample audio,
        IReadOnlyList<string> phraseHints,
        CancellationToken cancellationToken)
    {
        if (audio == null || audio.IsEmpty) return AiTranscript.Empty;

        await EnsureRecognizerAsync(cancellationToken);

        var targetRate = _config.LocalSampleRate;
        var sourceRate = audio.SampleRate;
        var channels = audio.Channels;
        var samples = audio.Samples;

        // Downmix and resample are pure array work; keep them off the frame.
        var prepared = await Task.Run(
            () => WavCodec.Resample(WavCodec.Downmix(samples, channels), sourceRate, targetRate),
            cancellationToken);

        if (prepared.Length == 0) return AiTranscript.Empty;

        // AudioClip creation has to happen on the main thread, which is where we resume.
        var clip = AudioClip.Create("IeumiQuestion", prepared.Length, 1, targetRate, false);
        try
        {
            clip.SetData(prepared, 0);
            var result = await _recognizer.TranscribeClipAsync(clip, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var text = result.Text?.Trim();
            if (_verbose)
                UnityEngine.Debug.Log($"[AI] Local STT {audio.Duration:0.0}s -> \"{text}\"");

            // The recognizer has no confidence score; a non-empty result is treated as confident.
            return new AiTranscript(text, string.IsNullOrEmpty(text) ? 0f : 1f);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A local failure is never a network failure, so it must not trigger offline mode.
            throw new AiServiceException($"On-device STT failed: {exception.Message}", false, exception);
        }
        finally
        {
            UnityEngine.Object.Destroy(clip);
        }
    }

    Task EnsureRecognizerAsync(CancellationToken cancellationToken) =>
        _initialization ??= InitializeAsync(cancellationToken);

    async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var host = new GameObject("IeumiLocalStt");
            UnityEngine.Object.DontDestroyOnLoad(host);

            _recognizer = host.AddComponent<OfflineSpeechRecognizerComponent>();
            _recognizer.ModelId = _config.LocalModelId;
            _recognizer.RecognitionLanguage = _config.LocalLanguage;

            // Loading a ~120 MB model takes seconds on desktop and longer on device.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_config.LocalInitTimeoutSeconds));

            await _recognizer.StartModuleInitializationAsync(timeout.Token);

            if (!_recognizer.IsInitialized)
                throw new AiServiceException($"Local STT model '{_config.LocalModelId}' did not load.");

            UnityEngine.Debug.Log($"[AI] Local STT ready: {_config.LocalModelId}");
        }
        catch (Exception)
        {
            // Allow a later question to retry instead of failing forever on one bad start.
            _initialization = null;
            throw;
        }
    }
#else
    public Task<AiTranscript> TranscribeAsync(
        AiAudioSample audio,
        IReadOnlyList<string> phraseHints,
        CancellationToken cancellationToken) =>
        throw new AiServiceException(
            "On-device STT is not available: the sherpa-onnx package is not installed. " +
            "Run IUM > AI > Install Local STT.");
#endif
}
