# Core.Audio 이식본

Playground(`D:\Unity\Playground`)의 범용 오디오 모듈을 IUM에서 검증하기 위해 가져온 것이다.
ISSUE-002(대사 전용 믹서 채널 없음)의 조치 후보를 시험하는 것이 목적이며, 아직 기존
`Assets/@Scripts/Cores/AudioManager.cs`를 대체하지 않는다.

## 구성

```
Core/          Playground Core.Audio + Core.Foundation.Singleton (namespace 격리)
Integration/   IUM 연동. 전역 네임스페이스
Editor/        테스트 씬 생성 메뉴
```

| 파일 | 원본 | 변경 |
|---|---|---|
| `Core/Singleton.cs` | `Core.Foundation` | `namespace Core.Foundation` 부여 |
| `Core/AudioBus.cs` | `Core.Audio` | `Dialogue = 2` 추가 |
| `Core/AudioData.cs` | `Core.Audio` | `dialogueVolume`, `dialogueMuted`, `enableSpatialization` 추가 |
| `Core/AudioVolumeMixer.cs` | `Core.Audio` | 삼항 분기 → switch, Dialogue 프로퍼티 |
| `Core/AudioManager.cs` | `Core.Audio` | Dialogue 위임 프로퍼티, `spatialize` 이식 |
| `Core/AudioHandle.cs` | `Core.Audio` | namespace만 |
| `Core/IAudioClipProvider.cs` | `Core.Audio` | namespace만 |

가져오지 않은 것은 둘이다.

- `AudioSceneBridge` — Playground `ISceneTransitionListener`를 구현한다. IUM은
  `ISceneEventListener`라 맞지 않아 `Integration/CoreAudioBridge.cs`로 다시 썼다.
- `ResourcesAudioClipProvider` — IUM은 Addressables를 쓴다. 대사 검증에는 클립 사전이
  필요 없어 Provider 없이 돌린다.

## 네임스페이스 함정

IUM과 Playground 양쪽에 전역 `Singleton<T>`, `AudioManager`, `Module`, `MonoThing`이 있다.
이름 충돌은 namespace로 막았지만 **`Singleton<T>`만은 완전 수식이 필요하다.**

```csharp
// 위험. 전역 네임스페이스 멤버가 using 지시문보다 우선하므로 IUM 쪽이 선택된다.
using Core.Foundation;
namespace Core.Audio { class AudioManager : Singleton<AudioManager> { } }

// 올바름
namespace Core.Audio { class AudioManager : Core.Foundation.Singleton<AudioManager> { } }
```

IUM 쪽 제약이 `where T : MonoBehaviour`라 잘못 붙어도 컴파일이 통과한다. 지연 생성 동작까지
따라오므로 런타임에야 드러난다.

## 대사 재생 경로

`Core.Audio.AudioManager`는 문자열 ID로 **사전 로드된** 클립만 재생한다. 대사 음성은
Addressables 녹음이 실패하면 TTS로 합성된 런타임 클립이라 ID로 등록할 대상이 없다.

그래서 대사는 매니저에 태우지 않고 `DialogueAudioModule`이 자기 `AudioSource`로 재생하되,
볼륨만 `Mixer.Calculate(AudioBus.Dialogue, ...)`로 받아 간다. 범용 매니저에는 대사 전용 코드가
한 줄도 들어가지 않는다.

클립 소유권은 모듈이 갖지 않는다. Addressables 핸들 해제와 합성 클립 `Destroy`는
`DialogueVoiceLibrary`가 추적한다.

## 모듈 호스트

`Module`은 `MonoThing`에 붙는데 `Core.Audio.AudioManager`는
`Core.Foundation.Singleton<AudioManager>`를 상속한다. C# 단일 상속이라 둘을 겸할 수 없으므로
`DialogueAudioHost : MonoThing`을 따로 두고 모듈이 매니저를 생성자로 받는다.

IUM의 `Module`을 쓴다(Playground `Core.Modules`는 가져오지 않았다). `LocomotionModule` 등
기존 모듈과 같은 계약이며, `AddModule` 뒤 **`Init()` 호출이 필수**다. `Module.TickUpdate`가
`IsInitialized`일 때만 `OnUpdate`를 부르기 때문이다.

## 테스트

`IUM > Dev > Create Core Audio Test Scene`으로 씬을 만든 뒤 실행한다.

| 키 | 동작 |
|---|---|
| `1` | 인스펙터에 배치한 클립 재생 (녹음 음성 상당) |
| `2` | 런타임 생성 클립 재생 (TTS 합성 상당) |
| `3` | 클립 없이 재생 (무음 폴백) |
| `S` | 정지 |
| `[` `]` | 대사 볼륨 |
| `M` | 대사 뮤트 |
| `,` `.` | 마스터 볼륨 |
| `N` | 마스터 뮤트 |
| `V` | 현재 값 출력 |

`2`를 재생한 채 `[` `]`로 대사 볼륨만 움직여 DIALOGUE 버스가 실제로 걸리는지 확인하는 것이
이번 검증의 핵심이다.

## 남은 제약

이 모듈은 Unity `AudioMixer`를 쓰지 않는다. 버스 단위 감쇠(더킹)는 되지만 **대사에만 거는
필터나 DSP는 불가능하다.** ISSUE-002가 지적한 두 가지 중 코드 중복은 해소되고 이펙트는
해소되지 않는다. 대사에 DSP가 필요한지 기획 확인이 선행되어야 이슈를 닫을 수 있다.

`CoreAudioBridge.Update`가 매 프레임 `DataManager` 설정을 따라간다. `VolumeOptionsPanel`이
`DataManager.ApplyAudioSettings`를 부르고 그 메서드가 아직 구 `AudioManager`만 갱신하기
때문이다. 정식 채택 시 `ApplyAudioSettings`가 이 버스를 직접 쓰게 하고 그 `Update`를 지운다.
