using System.Collections.Generic;
using UnityEngine;
using CoreAudioManager = Core.Audio.AudioManager;

/// <summary>
/// <c>Core.Audio</c> 모듈과 DIALOGUE 버스를 키보드로 확인하는 개발 도구. 개발 씬 전용이다.
///
/// 1 배치한 클립 재생(녹음 음성 상당) · 2 런타임 생성 클립 재생(TTS 합성 상당) ·
/// 3 클립 없이 재생(무음 폴백) · S 정지 ·
/// [ ] 대사 볼륨 · M 대사 뮤트 · , . 마스터 볼륨 · N 마스터 뮤트 · V 현재 값 출력
///
/// 2번이 핵심이다. 범용 매니저는 문자열 ID로 사전 로드된 클립만 재생하므로, 런타임에 만들어진
/// 클립이 DIALOGUE 버스 볼륨을 타고 나오는지가 이번 이식에서 검증할 지점이다.
///
/// 실제 <c>DialogueVoiceLibrary</c>를 태우지 않는 이유는 그쪽이 Addressables 주소와 AI 설정을
/// 요구하기 때문이다. 여기서는 클립의 출처만 같게 만들어(런타임 생성) 재생 경로를 확인한다.
/// </summary>
public sealed class CoreAudioTestDriver : MonoBehaviour
{
    [SerializeField] DialogueAudioHost host;
    [SerializeField] CoreAudioManager audioManager;

    [Header("녹음 음성 대역")]
    [SerializeField] AudioClip recordedClip;

    [Header("합성 음성 대역")]
    [SerializeField, Min(0.1f)] float toneSeconds = 1.5f;
    [SerializeField, Min(50f)] float toneHertz = 330f;

    [SerializeField] float volumeStep = 0.1f;

    readonly List<AudioClip> _generated = new();

    void Awake()
    {
        if (host == null) host = FindAnyObjectByType<DialogueAudioHost>();
        if (audioManager == null) audioManager = FindAnyObjectByType<CoreAudioManager>();
    }

    // OnEnable이 아니라 Start인 이유는 host.Voice가 DialogueAudioHost.Awake에서 만들어지기
    // 때문이다. 같은 오브젝트에 붙어 있어도 컴포넌트 추가 순서가 바뀌면 OnEnable 시점에는
    // 아직 null일 수 있다. Start는 모든 Awake 뒤에 돈다.
    void Start()
    {
        if (host != null && host.Voice != null) host.Voice.Finished += OnFinished;
    }

    void OnDestroy()
    {
        if (host != null && host.Voice != null) host.Voice.Finished -= OnFinished;

        // 모듈은 클립 소유권을 갖지 않는다. 여기서 만든 것은 여기서 정리한다.
        for (var i = 0; i < _generated.Count; i++)
            if (_generated[i] != null) Destroy(_generated[i]);
        _generated.Clear();
    }

    void Update()
    {
        var input = UserInput.Instance;
        if (input == null || host == null || host.Voice == null) return;

        if (input.GetKeyDown(KeyCode.Alpha1))
        {
            if (recordedClip == null) Log("배치한 클립이 없습니다. 인스펙터에 지정하십시오.");
            else
            {
                host.Voice.Play(recordedClip);
                Log($"녹음 음성 재생: {recordedClip.name}");
            }
        }

        if (input.GetKeyDown(KeyCode.Alpha2))
        {
            var clip = CreateTone();
            _generated.Add(clip);
            host.Voice.Play(clip);
            Log($"합성 음성 재생: {toneHertz:0}Hz {toneSeconds:0.0}s (런타임 생성)");
        }

        if (input.GetKeyDown(KeyCode.Alpha3))
        {
            host.Voice.Play(null);
            Log($"클립 없이 재생 요청. IsSpeaking = {host.Voice.IsSpeaking} (무음 폴백)");
        }

        if (input.GetKeyDown(KeyCode.S))
        {
            host.Voice.Stop();
            Log("정지");
        }

        if (audioManager == null) return;
        var mixer = audioManager.Mixer;

        if (input.GetKeyDown(KeyCode.LeftBracket))
        {
            mixer.DialogueVolume -= volumeStep;
            Log($"대사 볼륨 {mixer.DialogueVolume:0.00}");
        }

        if (input.GetKeyDown(KeyCode.RightBracket))
        {
            mixer.DialogueVolume += volumeStep;
            Log($"대사 볼륨 {mixer.DialogueVolume:0.00}");
        }

        if (input.GetKeyDown(KeyCode.M))
        {
            mixer.DialogueMuted = !mixer.DialogueMuted;
            Log($"대사 뮤트 {(mixer.DialogueMuted ? "켜짐" : "꺼짐")}");
        }

        if (input.GetKeyDown(KeyCode.Comma))
        {
            mixer.MasterVolume -= volumeStep;
            Log($"마스터 볼륨 {mixer.MasterVolume:0.00}");
        }

        if (input.GetKeyDown(KeyCode.Period))
        {
            mixer.MasterVolume += volumeStep;
            Log($"마스터 볼륨 {mixer.MasterVolume:0.00}");
        }

        if (input.GetKeyDown(KeyCode.N))
        {
            mixer.MasterMuted = !mixer.MasterMuted;
            Log($"마스터 뮤트 {(mixer.MasterMuted ? "켜짐" : "꺼짐")}");
        }

        if (input.GetKeyDown(KeyCode.V))
            Log($"마스터 {mixer.MasterVolume:0.00} / 대사 {mixer.DialogueVolume:0.00} / " +
                $"BGM {mixer.BgmVolume:0.00} / SFX {mixer.SfxVolume:0.00} → " +
                $"대사 최종 {mixer.Calculate(Core.Audio.AudioBus.Dialogue, 1f):0.00}");
    }

    /// <summary>TTS가 돌려주는 것과 같은 성격의 런타임 클립을 만든다.</summary>
    AudioClip CreateTone()
    {
        const int sampleRate = 44100;
        var sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * toneSeconds));
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            // 시작과 끝에 페이드를 넣지 않으면 클릭 노이즈가 나서 볼륨 변화를 듣기 어렵다.
            var t = i / (float)sampleRate;
            var envelope = Mathf.Min(1f, Mathf.Min(i, sampleCount - i) / (sampleRate * 0.02f));
            samples[i] = Mathf.Sin(2f * Mathf.PI * toneHertz * t) * 0.5f * envelope;
        }

        var clip = AudioClip.Create("DialogueTone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void OnFinished() => Log("재생 종료");

    void Log(string message) => Debug.Log($"[CoreAudio Test] {message}");
}
