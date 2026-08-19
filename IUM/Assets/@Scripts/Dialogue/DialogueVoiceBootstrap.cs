using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 노장·이음이·내레이션 대사에 TTS를 연결한다 (F-011 1.3). 사전 녹음이 확정될 때까지의 임시
/// 음성이며, 녹음 클립이 들어오면 <see cref="DialogueVoiceLibrary"/>가 그쪽을 먼저 쓰므로
/// 이 경로는 저절로 뒤로 물러난다 (07 문서 미정 16번).
///
/// 씬에 아무것도 배치하지 않아도 되도록 진입점에서 자기를 걸고, 준비도 그 자리에서 시작한다.
///
/// 준비를 첫 대사까지 미뤘던 적이 있는데, <c>DialoguePlayer</c>가 시퀀스의 모든 줄을 미리
/// 합성한 뒤 재생을 시작하는 구조라 **첫 시퀀스가 통째로 무음**이 됐다 (ISSUE-021). 대사 없는
/// 씬에서 아끼는 것은 JSON 몇 개를 읽고 서비스 객체 셋을 만드는 비용뿐이고, 네트워크는 첫 합성
/// 때 처음 발생한다. 그 정도를 아끼려고 첫 시퀀스를 잃을 이유가 없다.
///
/// 화자마다 서비스 인스턴스가 따로 있다. 목소리가 설정에 박혀 생성 시점에 정해지기 때문이며,
/// 자세한 사정은 <see cref="AiServiceFactory.CreateTextToSpeech(AiRuntimeSettings, AiVoiceOverride)"/>에 있다.
/// </summary>
public static class DialogueVoiceBootstrap
{
    static readonly Dictionary<DialogueSpeaker, IAiTextToSpeechService> Services = new();

    static Task _preparation;
    static bool _ready;
    static bool _failed;

    static int _failureThreshold = 3;
    static float _cooldownSeconds = 30f;
    static int _consecutiveFailures;
    static DateTime _openUntilUtc = DateTime.MinValue;

    /// <summary>
    /// 대사 TTS를 끈다. 오프라인 시연이나 요금을 아껴야 할 때 쓴다. 이음이 AI 대화의 음성은
    /// 별도 경로라 영향을 받지 않는다.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        // 도메인 리로드를 끈 채 플레이에 들어가면 이전 세션의 인스턴스가 남는다.
        Services.Clear();
        _preparation = null;
        _ready = false;
        _failed = false;
        _consecutiveFailures = 0;
        _openUntilUtc = DateTime.MinValue;

        InGameDialogue.TextToSpeechResolver = Resolve;
        CutsceneDirector.TextToSpeechResolver = Resolve;

        // 첫 대사를 기다리지 않는다. 첫 씬이 대사를 재생할 때쯤에는 준비가 끝나 있어야 한다.
        _preparation = PrepareAsync();
    }

    /// <summary>
    /// 화자에 맞는 서비스를 돌려준다. 아직 준비 중이면 null이고 그 줄은 자막만으로 진행된다.
    /// 실패도 null이며, 자막 진행은 정상 동작이지 오류가 아니다.
    /// </summary>
    static IAiTextToSpeechService Resolve(DialogueSpeaker speaker)
    {
        if (!Enabled || _failed) return null;

        // 차단기가 열려 있는 동안에는 시도하지 않는다. 토큰 만료나 네트워크 단절처럼 곧 낫지
        // 않는 고장에서, 대사 줄마다 왕복을 반복하면 재생이 그만큼 밀리고 로그가 뒤덮인다.
        if (DateTime.UtcNow < _openUntilUtc) return null;

        // 정상 경로에서는 Install이 이미 걸어 두었다. 여기서 다시 거는 것은 Enabled를 껐다
        // 켠 경우처럼 준비가 시작되지 않은 채로 도달했을 때의 대비다.
        if (!_ready)
        {
            _preparation ??= PrepareAsync();
            return null;
        }

        return Services.GetValueOrDefault(speaker);
    }

    static void NoteSuccess()
    {
        _consecutiveFailures = 0;
        _openUntilUtc = DateTime.MinValue;
    }

    /// <summary>
    /// 연속 실패를 센다. 문턱을 넘으면 냉각 시간 동안 시도를 멈춘다. 냉각이 끝나면 다시 한 번
    /// 시도해 보고, 또 실패하면 카운트가 문턱에 그대로 남아 있으므로 바로 다시 닫힌다.
    /// </summary>
    static void NoteFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures < _failureThreshold) return;

        _openUntilUtc = DateTime.UtcNow.AddSeconds(_cooldownSeconds);
        Debug.LogWarning(
            $"[Dialogue] TTS가 {_consecutiveFailures}회 연속 실패해 {_cooldownSeconds:F0}초 동안 " +
            "합성을 건너뜁니다. 그동안 대사는 자막으로 진행됩니다.");
    }

    static async Task PrepareAsync()
    {
        try
        {
            var settings = await AiConfigLoader.LoadAsync(null);
            var voices = settings.Config?.DialogueVoices;

            // 이음이 대화가 오프라인을 판정하는 기준을 그대로 쓴다. 같은 성질의 고장이고,
            // 값을 두 벌 두면 한쪽만 조정되는 일이 생긴다.
            var conversation = settings.Config?.Conversation;
            if (conversation != null)
            {
                _failureThreshold = Mathf.Max(1, conversation.OfflineFailureThreshold);
                _cooldownSeconds = Mathf.Max(0f, conversation.OfflineCooldownSeconds);
            }

            foreach (DialogueSpeaker speaker in Enum.GetValues(typeof(DialogueSpeaker)))
            {
                // 음색 지정이 없으면 기본 TTS 설정을 그대로 쓴다. 모두가 같은 목소리가 되지만
                // 소리가 아예 없는 것보다는 낫다.
                var service = AiServiceFactory.CreateTextToSpeech(settings, voices?.For(speaker));
                if (service != null) Services[speaker] = new BreakerAwareTextToSpeech(service);
            }

            _ready = Services.Count > 0;
            if (!_ready) _failed = true;
        }
        catch (Exception exception)
        {
            // 대사는 자막만으로도 진행된다. 음성 준비 실패가 게임을 막아서는 안 된다 (01 문서 복구 원칙).
            _failed = true;
            Debug.LogWarning($"[Dialogue] 대사 TTS를 준비하지 못해 자막으로만 진행합니다: {exception.Message}");
        }
    }

    /// <summary>
    /// 합성 결과를 차단기에 알리는 얇은 껍데기. 예외는 그대로 다시 던져 <c>DialogueVoiceLibrary</c>의
    /// 기존 처리(경고 후 자막 진행)를 건드리지 않는다.
    ///
    /// 데코레이터를 쓴 이유는 실패를 아는 지점과 시도를 멈출 지점이 다르기 때문이다. 실패는
    /// 라이브러리가 알고 중단은 여기가 결정하는데, 라이브러리에 보고 통로를 새로 뚫으면
    /// 그 클래스와 호출자 둘의 시그니처가 함께 바뀐다.
    /// </summary>
    sealed class BreakerAwareTextToSpeech : IAiTextToSpeechService
    {
        readonly IAiTextToSpeechService _inner;

        public BreakerAwareTextToSpeech(IAiTextToSpeechService inner) => _inner = inner;

        public bool IsMock => _inner.IsMock;

        public async Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                var clip = await _inner.SynthesizeAsync(text, cancellationToken);
                NoteSuccess();
                return clip;
            }
            catch (OperationCanceledException)
            {
                // 취소는 고장이 아니다. 씬을 넘기거나 대사를 끊으면 늘 일어난다.
                throw;
            }
            catch
            {
                NoteFailure();
                throw;
            }
        }
    }
}
