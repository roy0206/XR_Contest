using System;
using Newtonsoft.Json;

/// <summary>
/// Tunable settings for the 이음이 voice pipeline. Everything here is safe to commit;
/// credentials live in <see cref="AiSecrets"/>, which is never committed.
/// Loaded from StreamingAssets/ai_config.json by <see cref="AiConfigLoader"/>.
/// </summary>
[Serializable]
public sealed class AiConfig
{
    public AiSttConfig Stt { get; set; } = new();
    public AiLlmConfig Llm { get; set; } = new();
    public AiTtsConfig Tts { get; set; } = new();

    /// <summary>
    /// 노장·이음이·내레이션의 음색. <see cref="Tts"/> 위에 덮어쓸 값만 담는다. 비우면 모든 화자가
    /// 같은 목소리로 말한다.
    /// </summary>
    public AiDialogueVoicesConfig DialogueVoices { get; set; } = new();

    public AiConversationConfig Conversation { get; set; } = new();

    /// <summary>Patterns used by <see cref="AiSafetyFilter"/>. Defaults apply when omitted.</summary>
    public AiSafetyRules Safety { get; set; } = new();

    /// <summary>Forces mock services even when credentials exist. Useful for offline demos.</summary>
    public bool ForceMockServices { get; set; }

    /// <summary>Logs every request/response summary. Never logs credentials.</summary>
    public bool VerboseLogging { get; set; }

    public void Clamp()
    {
        Stt ??= new AiSttConfig();
        Llm ??= new AiLlmConfig();
        Tts ??= new AiTtsConfig();
        DialogueVoices ??= new AiDialogueVoicesConfig();
        Conversation ??= new AiConversationConfig();
        Safety ??= new AiSafetyRules();
        Stt.Clamp();
        Llm.Clamp();
        Tts.Clamp();
        Conversation.Clamp();
    }

    /// <summary>
    /// TTS 설정만 교체한 사본. 나머지 구획은 참조를 공유한다 — 화자별 서비스를 만들 때 STT나 LLM
    /// 설정까지 복제할 이유가 없고, 어느 쪽도 생성 후에 값을 바꾸지 않는다.
    /// </summary>
    public AiConfig CloneWithTts(AiTtsConfig tts)
    {
        var copy = (AiConfig)MemberwiseClone();
        copy.Tts = tts;
        return copy;
    }
}

/// <summary>Which recognizer the factory should pick.</summary>
public enum AiSttProvider
{
    /// <summary>On-device first, then Google when a key exists, then mock.</summary>
    Auto,

    /// <summary>Force on-device recognition. Falls back only if the package is missing.</summary>
    Local,

    /// <summary>Force Google Cloud Speech-to-Text.</summary>
    Google,

    /// <summary>Force the canned-question mock.</summary>
    Mock
}

/// <summary>Speech recognition: on-device (sherpa-onnx) or Google Cloud Speech-to-Text.</summary>
[Serializable]
public sealed class AiSttConfig
{
    public AiSttProvider Provider { get; set; } = AiSttProvider.Auto;

    /// <summary>
    /// sherpa-onnx model id. The default is the non-streaming Korean transducer, which matches
    /// push-to-talk: the whole utterance is recognized once, after the button is released.
    /// </summary>
    public string LocalModelId { get; set; } = "sherpa-onnx-zipformer-korean-2024-06-24";

    public string LocalLanguage { get; set; } = "ko";

    /// <summary>Model loading happens once and can take a while on device.</summary>
    public float LocalInitTimeoutSeconds { get; set; } = 60f;

    /// <summary>The recognizer expects 16 kHz mono; captured audio is resampled to this rate.</summary>
    public int LocalSampleRate { get; set; } = 16000;

    public string Endpoint { get; set; } = "https://speech.googleapis.com/v1/speech:recognize";
    public string LanguageCode { get; set; } = "ko-KR";

    /// <summary>"latest_short" fits push-to-talk utterances better than the default model.</summary>
    public string Model { get; set; } = "latest_short";

    public int SampleRateHertz { get; set; } = 16000;
    public bool EnableAutomaticPunctuation { get; set; } = true;

    /// <summary>Feeds the glossary into recognition so 전통 목조건축 용어 survives transcription.</summary>
    public bool UsePhraseHints { get; set; } = true;
    public int PhraseHintBoost { get; set; } = 15;
    public int MaxPhraseHints { get; set; } = 40;

    public float TimeoutSeconds { get; set; } = 10f;

    public void Clamp()
    {
        if (SampleRateHertz < 8000) SampleRateHertz = 16000;
        if (PhraseHintBoost < 0) PhraseHintBoost = 0;
        if (MaxPhraseHints < 0) MaxPhraseHints = 0;
        if (TimeoutSeconds < 1f) TimeoutSeconds = 10f;
        if (LocalSampleRate < 8000) LocalSampleRate = 16000;
        if (LocalInitTimeoutSeconds < 5f) LocalInitTimeoutSeconds = 60f;
        if (string.IsNullOrWhiteSpace(LocalLanguage)) LocalLanguage = "ko";
    }
}

/// <summary>Upstage Solar chat completions (OpenAI-compatible schema).</summary>
[Serializable]
public sealed class AiLlmConfig
{
    public string Endpoint { get; set; } = "https://api.upstage.ai/v1/chat/completions";
    public string Model { get; set; } = "solar-pro3";

    /// <summary>
    /// Solar Pro 3 reasoning budget ("low", "high", …). Left empty on purpose: 이음이 answers
    /// are one or two sentences, and reasoning only adds latency and output tokens here.
    /// </summary>
    public string ReasoningEffort { get; set; } = string.Empty;

    public float Temperature { get; set; } = 0.4f;
    public int MaxTokens { get; set; } = 400;
    public float TimeoutSeconds { get; set; } = 15f;

    /// <summary>How many previous question/answer pairs are replayed as context.</summary>
    public int HistoryTurns { get; set; } = 4;

    /// <summary>Asks Solar for a small JSON envelope so topic and grounding can be inspected.</summary>
    public bool UseStructuredResponse { get; set; } = true;

    /// <summary>One retry with a stricter instruction when the safety filter rejects an answer.</summary>
    public bool RetryOnSafetyRejection { get; set; } = true;

    public void Clamp()
    {
        Temperature = UnityEngine.Mathf.Clamp(Temperature, 0f, 2f);
        if (MaxTokens < 32) MaxTokens = 400;
        if (TimeoutSeconds < 1f) TimeoutSeconds = 15f;
        HistoryTurns = UnityEngine.Mathf.Clamp(HistoryTurns, 0, 12);
    }
}

/// <summary>Which synthesizer the factory should pick.</summary>
public enum AiTtsProvider
{
    /// <summary>Google when its key exists, then CLOVA, then mock.</summary>
    Auto,

    /// <summary>Google Cloud Text-to-Speech. Free tier covers this project's volume.</summary>
    Google,

    /// <summary>Naver CLOVA Voice Premium. Best Korean quality, but a fixed monthly base fee.</summary>
    Clova,

    /// <summary>No audio; subtitles only.</summary>
    Mock
}

/// <summary>Speech synthesis: Google Cloud Text-to-Speech or Naver CLOVA Voice Premium.</summary>
[Serializable]
public sealed class AiTtsConfig
{
    public AiTtsProvider Provider { get; set; } = AiTtsProvider.Auto;

    public string GoogleEndpoint { get; set; } = "https://texttospeech.googleapis.com/v1/text:synthesize";
    public string GoogleLanguageCode { get; set; } = "ko-KR";

    /// <summary>
    /// WaveNet shares the Standard pricing SKU and its free tier, which is the largest of the
    /// Korean voice families. Swap for a Neural2 or Chirp voice if the character needs it.
    /// </summary>
    public string GoogleVoiceName { get; set; } = "ko-KR-Wavenet-A";

    /// <summary>0.25~4.0, 1.0 is normal speed.</summary>
    public float GoogleSpeakingRate { get; set; } = 1f;

    /// <summary>-20.0~20.0 semitones.</summary>
    public float GooglePitch { get; set; }

    /// <summary>-96.0~16.0 dB.</summary>
    public float GoogleVolumeGainDb { get; set; }

    public int GoogleSampleRateHertz { get; set; } = 24000;

    public string Endpoint { get; set; } = "https://naveropenapi.apigw.ntruss.com/tts-premium/v1/tts";

    /// <summary>Child-voice speaker chosen for 이음이. See CLOVA Voice speaker list to change.</summary>
    public string Speaker { get; set; } = "ngaram";

    /// <summary>-5..5, higher is slower.</summary>
    public int Speed { get; set; }

    /// <summary>-5..5, higher is lower pitch.</summary>
    public int Pitch { get; set; }

    /// <summary>-5..5 volume offset.</summary>
    public int Volume { get; set; }

    /// <summary>wav keeps decoding deterministic on every platform.</summary>
    public string Format { get; set; } = "wav";

    public float TimeoutSeconds { get; set; } = 12f;

    public void Clamp()
    {
        Speed = UnityEngine.Mathf.Clamp(Speed, -5, 5);
        Pitch = UnityEngine.Mathf.Clamp(Pitch, -5, 5);
        Volume = UnityEngine.Mathf.Clamp(Volume, -5, 5);
        if (string.IsNullOrWhiteSpace(Format)) Format = "wav";
        if (TimeoutSeconds < 1f) TimeoutSeconds = 12f;
        GoogleSpeakingRate = UnityEngine.Mathf.Clamp(GoogleSpeakingRate, 0.25f, 4f);
        GooglePitch = UnityEngine.Mathf.Clamp(GooglePitch, -20f, 20f);
        GoogleVolumeGainDb = UnityEngine.Mathf.Clamp(GoogleVolumeGainDb, -96f, 16f);
        if (GoogleSampleRateHertz < 8000) GoogleSampleRateHertz = 24000;
        if (string.IsNullOrWhiteSpace(GoogleLanguageCode)) GoogleLanguageCode = "ko-KR";
    }

    /// <summary>화자별 음색을 만들 때 쓰는 얕은 사본. 값 타입과 문자열뿐이라 이걸로 충분하다.</summary>
    public AiTtsConfig Clone() => (AiTtsConfig)MemberwiseClone();
}

/// <summary>
/// 화자 하나의 음색. <see cref="AiTtsConfig"/> 위에 덮어쓸 값만 적고 나머지는 물려받는다.
///
/// 기본값을 "비어 있음"으로 표현해야 해서 문자열은 null, 수치는 범위 밖 값을 센티널로 쓴다.
/// 0은 유효한 피치라 센티널이 될 수 없다.
/// </summary>
[Serializable]
public sealed class AiVoiceOverride
{
    public const float NoFloat = float.NaN;
    public const int NoInt = int.MinValue;

    /// <summary>Google 보이스 이름. ko-KR-Wavenet-A·B는 여성, C·D는 남성이다.</summary>
    public string GoogleVoiceName { get; set; }

    public float GoogleSpeakingRate { get; set; } = NoFloat;
    public float GooglePitch { get; set; } = NoFloat;

    /// <summary>CLOVA 화자 ID.</summary>
    public string Speaker { get; set; }

    public int Speed { get; set; } = NoInt;
    public int Pitch { get; set; } = NoInt;

    /// <summary>덮어쓴 사본을 돌려준다. 원본은 건드리지 않는다.</summary>
    public AiTtsConfig ApplyTo(AiTtsConfig baseConfig)
    {
        var copy = baseConfig.Clone();

        if (!string.IsNullOrWhiteSpace(GoogleVoiceName)) copy.GoogleVoiceName = GoogleVoiceName;
        if (!float.IsNaN(GoogleSpeakingRate)) copy.GoogleSpeakingRate = GoogleSpeakingRate;
        if (!float.IsNaN(GooglePitch)) copy.GooglePitch = GooglePitch;

        if (!string.IsNullOrWhiteSpace(Speaker)) copy.Speaker = Speaker;
        if (Speed != NoInt) copy.Speed = Speed;
        if (Pitch != NoInt) copy.Pitch = Pitch;

        copy.Clamp();
        return copy;
    }
}

/// <summary>
/// 화자별 음색 (F-011). 기본 <see cref="AiTtsConfig"/>는 이음이 기준으로 잡혀 있어서, 노장 대사를
/// 그대로 태우면 노인이 아이 목소리로 말한다. 그래서 화자마다 덮어쓸 값을 따로 둔다.
///
/// 사전 녹음이 확정되면 이 설정은 쓰이지 않는다 — 녹음 클립이 TTS보다 우선한다 (F-011 1.3).
/// </summary>
[Serializable]
public sealed class AiDialogueVoicesConfig
{
    public AiVoiceOverride Nojang { get; set; }
    public AiVoiceOverride Ieumi { get; set; }
    public AiVoiceOverride Narration { get; set; }

    public AiVoiceOverride For(DialogueSpeaker speaker) => speaker switch
    {
        DialogueSpeaker.Nojang => Nojang,
        DialogueSpeaker.Ieumi => Ieumi,
        DialogueSpeaker.Narration => Narration,
        _ => null
    };
}

/// <summary>Conversation policy: F-013 3.4~3.6 and the offline/failure rules.</summary>
[Serializable]
public sealed class AiConversationConfig
{
    /// <summary>PTT hold below this is treated as a mis-press instead of a question.</summary>
    public float MinRecordSeconds { get; set; } = 0.4f;

    /// <summary>Hard stop for a single question so a stuck button cannot record forever.</summary>
    public float MaxRecordSeconds { get; set; } = 15f;

    /// <summary>Peak amplitude below this counts as silence and skips the STT call.</summary>
    public float SilenceThreshold { get; set; } = 0.012f;

    /// <summary>기본 응답 5~10초, 최대 15초 (F-013 3.6). Korean TTS speaks ~5.5 chars/second.</summary>
    public int TargetResponseChars { get; set; } = 70;
    public int MaxResponseChars { get; set; } = 100;

    /// <summary>세 번째 실패 후 노장 시연 단계에서는 구체적인 설명을 허용한다 (F-012 2.4).</summary>
    public int DirectAnswerMaxChars { get; set; } = 160;

    /// <summary>Measured with ko-KR-Wavenet-A: 24 characters took 3.4 seconds.</summary>
    public float SpeechCharsPerSecond { get; set; } = 7f;

    /// <summary>Player utterances longer than this are truncated before they reach the model.</summary>
    public int MaxUserChars { get; set; } = 200;

    /// <summary>두 번째 연속 인식 실패에서 선택형 질문 목록을 띄운다 (F-013 3.4).</summary>
    public int SuggestionsAfterFailures { get; set; } = 2;

    /// <summary>처음 1~2회는 짧게 응답하고 이후 현재 공정으로 유도한다 (F-013 3.5).</summary>
    public int OffTopicToleranceCount { get; set; } = 2;

    /// <summary>Consecutive network errors before the pipeline drops to offline answers.</summary>
    public int OfflineFailureThreshold { get; set; } = 3;

    /// <summary>How long offline mode sticks before the next request is allowed to try again.</summary>
    public float OfflineCooldownSeconds { get; set; } = 30f;

    /// <summary>Subtitle hold time added after speech ends.</summary>
    public float SubtitleTailSeconds { get; set; } = 1.2f;

    public void Clamp()
    {
        MinRecordSeconds = UnityEngine.Mathf.Clamp(MinRecordSeconds, 0.05f, 3f);
        MaxRecordSeconds = UnityEngine.Mathf.Clamp(MaxRecordSeconds, 2f, 60f);
        SilenceThreshold = UnityEngine.Mathf.Clamp(SilenceThreshold, 0f, 0.5f);
        if (TargetResponseChars < 20) TargetResponseChars = 70;
        if (MaxResponseChars < TargetResponseChars) MaxResponseChars = TargetResponseChars + 30;
        if (DirectAnswerMaxChars < MaxResponseChars) DirectAnswerMaxChars = MaxResponseChars + 60;
        if (SpeechCharsPerSecond < 1f) SpeechCharsPerSecond = 7f;
        if (MaxUserChars < 20) MaxUserChars = 200;
        if (SuggestionsAfterFailures < 1) SuggestionsAfterFailures = 2;
        if (OffTopicToleranceCount < 0) OffTopicToleranceCount = 2;
        if (OfflineFailureThreshold < 1) OfflineFailureThreshold = 3;
        if (OfflineCooldownSeconds < 0f) OfflineCooldownSeconds = 30f;
        if (SubtitleTailSeconds < 0f) SubtitleTailSeconds = 1.2f;
    }
}

/// <summary>
/// Credentials. Loaded from a git-ignored file, never from a committed asset, and never logged.
/// Each service falls back to its mock independently, so a partial key set still runs.
/// </summary>
[Serializable]
public sealed class AiSecrets
{
    /// <summary>Upstage Console API key (Bearer).</summary>
    public string UpstageApiKey { get; set; }

    /// <summary>Google Cloud API key used as the ?key= query parameter.</summary>
    public string GoogleApiKey { get; set; }

    /// <summary>Optional OAuth access token. Short-lived; useful only for quick tests.</summary>
    public string GoogleAccessToken { get; set; }

    /// <summary>
    /// Long-lived OAuth refresh token, obtained once through the consent flow. Used with
    /// ai_oauth_client.json when the organisation blocks service account keys.
    /// </summary>
    public string GoogleRefreshToken { get; set; }

    /// <summary>NCP API Gateway key id (X-NCP-APIGW-API-KEY-ID).</summary>
    public string ClovaClientId { get; set; }

    /// <summary>NCP API Gateway key (X-NCP-APIGW-API-KEY).</summary>
    public string ClovaClientSecret { get; set; }

    [JsonIgnore] public bool HasUpstage => !string.IsNullOrWhiteSpace(UpstageApiKey);
    [JsonIgnore] public bool HasGoogle =>
        !string.IsNullOrWhiteSpace(GoogleApiKey) || !string.IsNullOrWhiteSpace(GoogleAccessToken);

    [JsonIgnore] public bool HasGoogleRefreshToken => !string.IsNullOrWhiteSpace(GoogleRefreshToken);
    [JsonIgnore] public bool HasClova =>
        !string.IsNullOrWhiteSpace(ClovaClientId) && !string.IsNullOrWhiteSpace(ClovaClientSecret);
}
