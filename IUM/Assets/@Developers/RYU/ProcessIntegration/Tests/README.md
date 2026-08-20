# 전체 코어 루프 자동 검증

## 목적

물리 공정 구현을 직접 실행하거나 수정하지 않고, 게임 전체 루프를 잇는 계약이 깨졌는지 빠르게 검출한다.

검증 범위:

- `flow.json`의 `프롤로그 → 튜토리얼 → 먹매김 → 톱질/대패 → 끌질 → 도리 → 공포 → 엔딩` 경로
- 흐름이 참조하는 씬의 Build Settings 등록 상태
- 흐름이 참조하는 컷씬, 영상, 다음 씬의 존재
- 각 공정 정의와 단계, 신호 키, 신호 요구량
- 공정이 참조하는 대사 ID의 존재
- `quest.json` 튜토리얼의 진입점, 단일 진행 경로, 완료 노드, 대사·상호작용 대상 계약
- `UserProgressData`의 실제 완료 및 초기화 전이
- `MainPlayProcessBridge`와 `process.json` 사이의 신호 계약
- 현재 공정이 아닌 도리·공포 부재의 접근 차단 정책
- 메인 플레이 씬의 러너, 브리지, 대사, 일시정지 컴포넌트 배선
- 시작 메뉴의 필수 UI 요소, FixedUI 아이콘, 시작 배경 모델과 StartScene 배선

검증하지 않는 범위:

- 먹선, 대패, 끌질, 조립 판정 알고리즘의 정확성
- XR 입력, 햅틱, 손 위치와 실제 조작감
- 모델, 충돌체, 머티리얼의 시각적 품질
- 네트워크 TTS와 AI 공급자의 실제 응답

## 실행 방법

Unity 메뉴에서 다음 항목을 실행한다.

`Tools > IUM > Verify Full Core Loop`

결과는 Console과 다음 파일에 기록된다.

`Logs/CoreLoopVerification.latest.log`

Test Runner에서는 EditMode 테스트의 `IUM.CoreLoop` 카테고리를 실행해도 된다.

CI 또는 별도 Unity 프로세스에서는 다음 형식을 사용한다.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe' `
  -batchmode -nographics `
  -projectPath 'D:\Unity\XR_Contest\IUM' `
  -runTests -testPlatform EditMode `
  -testFilter 'IUM.CoreLoopVerification.Tests.CoreLoopContractTests' `
  -testResults 'Logs\CoreLoopVerification.xml' `
  -logFile 'Logs\CoreLoopVerification.Unity.log'
```

Unity 프로젝트가 이미 다른 Editor에서 열려 있으면 동일 경로에 두 번째 프로세스를 실행하지 않는다.

## 프로필 재사용

예상 경로와 공정별 신호는 `CoreLoopVerificationProfile.json`에 있다. 경로가 바뀌면 테스트 코드를 복사하지 말고 프로필을 갱신한다.

다른 프로젝트 변형을 검증하려면 같은 스키마의 프로필을 준비하고 테스트의 `ProfilePath`만 교체하면 된다. 물리 공정 구현은 프로필과 검증기에 의존하지 않는다.

## 실패 해석

- `FullRoute_*`: 흐름, 컷씬, 영상 또는 Build Settings 계약이 깨졌다.
- `ProcessDefinitions_*`: 제작 공정 JSON, 대사 ID 또는 브리지 신호 키가 어긋났다.
- `TutorialQuestGraph_*`: 튜토리얼 퀘스트 그래프의 경로, 노드 또는 대사 참조가 어긋났다.
- `ProgressModel_*`: `ProcessId` 순서나 저장 진행 전이가 바뀌었다.
- `MainPlaySignals_*`: 공정 신호가 누적·초기화되지 않거나 요구량이 잘못됐다.
- `MainPlayPolicy_*`: 도리나 공포 부재가 잘못된 공정에서 열릴 수 있다.
- `MainPlayScene_*`: 메인 씬 배선 또는 재시작 대상 씬 등록이 빠졌다.
- `StartScene_*`: 시작 메뉴 UI 계약, FixedUI 아이콘 또는 시작 배경 모델 배선이 빠졌다.
