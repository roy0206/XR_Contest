using System.Text;
using UnityEngine;

/// <summary>The three services the conversation pipeline runs on, plus what they resolved to.</summary>
public sealed class AiServiceSet
{
    public AiServiceSet(IAiSpeechToTextService stt, IAiChatService chat, IAiTextToSpeechService tts)
    {
        Stt = stt;
        Chat = chat;
        Tts = tts;
    }

    public IAiSpeechToTextService Stt { get; }
    public IAiChatService Chat { get; }
    public IAiTextToSpeechService Tts { get; }

    public bool UsesAnyMock => Stt.IsMock || Chat.IsMock || Tts.IsMock;
    public bool IsFullyMocked => Stt.IsMock && Chat.IsMock && Tts.IsMock;

    public string Describe()
    {
        var builder = new StringBuilder();
        builder.Append("STT ").Append(Stt switch
        {
            LocalSpeechToTextService => "로컬(sherpa-onnx)",
            GoogleSpeechToTextService => "Google",
            _ => "Mock"
        });
        builder.Append(" · LLM ").Append(Chat.IsMock ? "Mock" : "Upstage Solar");
        builder.Append(" · TTS ").Append(Tts switch
        {
            GoogleTextToSpeechService => "Google",
            ClovaVoiceTextToSpeechService => "CLOVA Voice",
            _ => "Mock"
        });
        return builder.ToString();
    }
}

/// <summary>
/// Picks a real service per credential, not all-or-nothing: a project with only an Upstage key
/// still gets real answers with mock speech, which is the normal state during development.
/// </summary>
public static class AiServiceFactory
{
    public static AiServiceSet Create(AiRuntimeSettings settings)
    {
        settings ??= new AiRuntimeSettings();
        var config = settings.Config ?? new AiConfig();
        var secrets = settings.Secrets ?? new AiSecrets();
        var force = config.ForceMockServices;

        var tokens = CreateGoogleTokenProvider(settings, secrets);
        var stt = CreateSpeechToText(config, secrets, settings, tokens, force);

        IAiChatService chat = !force && secrets.HasUpstage
            ? new UpstageSolarChatService(config, secrets)
            : new MockChatService(settings.Knowledge, settings.Dialogue);

        var tts = CreateTextToSpeech(config, secrets, tokens, force);

        var set = new AiServiceSet(stt, chat, tts);
        Debug.Log($"[AI] Services: {set.Describe()}" +
                  (force ? " (forceMockServices)" : string.Empty));
        return set;
    }

    /// <summary>
    /// 화자 하나를 위한 TTS 서비스를 만든다. 대사 재생이 화자별로 다른 목소리를 쓰려면
    /// (<see cref="AiDialogueVoicesConfig"/>) 서비스 인스턴스도 화자 수만큼 필요하다 —
    /// <see cref="IAiTextToSpeechService.SynthesizeAsync"/>가 화자를 인자로 받지 않고, 목소리는
    /// 설정에 박혀 생성 시점에 정해지기 때문이다.
    ///
    /// 인터페이스를 바꾸는 대신 이 길을 택했다. 인자를 늘리면 구현 셋과 이음이 대화 경로까지
    /// 전부 따라 바뀌는데, 그쪽은 목소리가 하나여도 아무 문제가 없다.
    /// </summary>
    public static IAiTextToSpeechService CreateTextToSpeech(AiRuntimeSettings settings, AiVoiceOverride voice)
    {
        settings ??= new AiRuntimeSettings();
        var config = settings.Config ?? new AiConfig();
        var secrets = settings.Secrets ?? new AiSecrets();

        if (voice != null)
            config = config.CloneWithTts(voice.ApplyTo(config.Tts ?? new AiTtsConfig()));

        var tokens = CreateGoogleTokenProvider(settings, secrets);
        return CreateTextToSpeech(config, secrets, tokens, config.ForceMockServices);
    }

    /// <summary>
    /// On-device recognition is preferred because it keeps working without a network.
    /// Google is the fallback when the package is missing, and the mock is the last resort
    /// so the pipeline always has a recognizer.
    /// </summary>
    /// <summary>
    /// Service account is Google's documented method, but organisations often block key
    /// creation; the OAuth refresh token covers that case with the same result at runtime.
    /// </summary>
    static IGoogleTokenProvider CreateGoogleTokenProvider(AiRuntimeSettings settings, AiSecrets secrets)
    {
        if (settings.GoogleServiceAccount != null && settings.GoogleServiceAccount.IsUsable)
            return new GoogleServiceAccountTokenProvider(settings.GoogleServiceAccount);

        if (settings.GoogleOAuthClient != null &&
            settings.GoogleOAuthClient.IsUsable &&
            secrets.HasGoogleRefreshToken)
            return new GoogleOAuthTokenProvider(settings.GoogleOAuthClient, secrets.GoogleRefreshToken);

        return null;
    }

    static IAiSpeechToTextService CreateSpeechToText(
        AiConfig config,
        AiSecrets secrets,
        AiRuntimeSettings settings,
        IGoogleTokenProvider tokens,
        bool force)
    {
        if (force) return new MockSpeechToTextService(settings.Dialogue);

        var provider = config.Stt.Provider;
        if (provider == AiSttProvider.Mock) return new MockSpeechToTextService(settings.Dialogue);

        var hasGoogle = secrets.HasGoogle || tokens != null;

        if (provider is AiSttProvider.Auto or AiSttProvider.Local)
        {
            if (LocalSpeechToTextService.IsAvailable)
                return new LocalSpeechToTextService(config);

            Debug.LogWarning("[AI] On-device STT was requested but the sherpa-onnx package is " +
                             "not installed. Run IUM > AI > Install Local STT.");

            if (provider == AiSttProvider.Local && !hasGoogle)
                return new MockSpeechToTextService(settings.Dialogue);
        }

        return hasGoogle
            ? new GoogleSpeechToTextService(config, secrets, tokens)
            : new MockSpeechToTextService(settings.Dialogue);
    }

    /// <summary>
    /// Google is preferred in auto mode: its free tier covers this project's volume, while
    /// CLOVA Voice bills a fixed monthly base fee whether or not it is called.
    /// </summary>
    static IAiTextToSpeechService CreateTextToSpeech(
        AiConfig config,
        AiSecrets secrets,
        IGoogleTokenProvider tokens,
        bool force)
    {
        if (force) return new MockTextToSpeechService();

        var hasGoogle = secrets.HasGoogle || tokens != null;

        switch (config.Tts.Provider)
        {
            case AiTtsProvider.Mock:
                return new MockTextToSpeechService();

            case AiTtsProvider.Google:
                if (hasGoogle) return new GoogleTextToSpeechService(config, secrets, tokens);
                Debug.LogWarning("[AI] Google TTS was requested but no Google credential is set. " +
                                 "Place the service account key at StreamingAssets/ai_service_account.json.");
                break;

            case AiTtsProvider.Clova:
                if (secrets.HasClova) return new ClovaVoiceTextToSpeechService(config, secrets);
                Debug.LogWarning("[AI] CLOVA Voice was requested but no CLOVA credential is set.");
                break;
        }

        if (hasGoogle) return new GoogleTextToSpeechService(config, secrets, tokens);
        if (secrets.HasClova) return new ClovaVoiceTextToSpeechService(config, secrets);
        return new MockTextToSpeechService();
    }
}
