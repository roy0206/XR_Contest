# 2026-08-20 코어 루프 연결 안정화 작업 로그

## 작업 요청

R 승인 아래 다음 네 항목을 무인 작업 가능한 범위로 진행했다.

1. 현재 공정 이전·이후의 오브젝트를 조작해 진행이 막히는 문제 방지
2. 일시정지 메뉴의 공정 다시 시작 연결
3. 첫 대사 초기화 문제 확인 및 보강
4. 재사용 가능한 전체 코어 루프 자동 검증 구성

공정 자체는 타 개발자의 소관이므로 해당 구현 파일을 수정하지 않는다.

## 브랜치와 시작 상태

- 작업 브랜치: `codex/core-loop-integration-verification`
- 분기 기준: `main`의 `9af56e0` (`메인 플레이 제작 루프 및 통합 시스템 구현`)
- 시작 전 미커밋 변경: `Assets/warehouseFin/Scenes/SampleScene.unity.meta`
- 위 `.meta`는 이번 작업과 무관한 기존 변경으로 보존했으며 스테이징·커밋하지 않는다.

## 소유권 확인

수정 전 프로젝트 규칙과 다음 파일의 Git 이력을 확인했다.

- `ProcessRunner.cs`: 최근 작성자 `roy0206`, 커밋 `9af56e0`
- `MainPlayProcessBridge.cs`: 최근 작성자 `roy0206`, 커밋 `9af56e0`
- `PauseController.cs`: 작성자 `roy0206`, 커밋 `b128436` — 이벤트를 구독할 뿐 파일은 수정하지 않음
- `InGameDialogue.cs`: 최근 작성자 `roy0206`, 커밋 `9af56e0` — 공개 초기화 API만 사용
- 먹선·대패·공포 조립 스크립트: 다른 팀원 작성 파일로 확인해 읽기 전용 유지

읽은 규칙:

- `AGENT.md`
- `.claude/rules/개발환경_입력.md`
- `.claude/rules/협업_소유권.md`
- `.claude/rules/Unity_커밋.md`
- `Assets/@Documents/이슈_로그/00_기록_규칙.md`

## 구현 상세

### 1. 공정 생명주기와 선행 조작 보호

`ProcessRunner`에 `ProcessChanged(ProcessId)` 이벤트를 추가했다. 최초 공정과 같은 씬에서 이어지는 다음 공정 모두 `BeginProcess` 초기화 직후 동일한 이벤트를 발생시킨다.

`MainPlayProcessBridge`는 이 이벤트를 받아 다음을 수행한다.

- 해당 공정의 `ProcessSignalBus` 값만 0으로 초기화
- 공정별 내부 카운터 초기화
- 먹선 공정에서만 먹선 도구 허용
- Sawing 공정에서만 현재 연결된 대패 도구 허용
- 도리 공정에서 `1floor` 부재만 허용
- 공포 공정에서 `1floor` 이외의 미조립 부재만 허용
- 공정 완료 대사 및 저장 처리 중에는 위 도구와 부재를 모두 잠금
- 조립 완료 이벤트는 부재 종류에 맞는 현재 공정에서만 소비
- 공정 진입 시 이미 점유된 조립 타겟을 재조회해 이벤트 순서 차이로 인한 교착 방지

접근 제어는 타 개발자의 스크립트를 변경하지 않고 공개된 `Grabbable.GrabEnabled`, 작업 완료 이벤트, 조립 완료 이벤트만 사용한다. 원래 비활성화된 부재는 공정이 맞더라도 임의로 활성화하지 않는다.

### 2. 공정 다시 시작

`MainPlayProcessBridge`가 `PauseController.ProcessRestartRequested`를 구독한다.

재시작 순서:

1. `PauseController`가 메뉴와 일시정지를 해제
2. 진행 중 대사와 대사 큐 중지
3. 메인 플레이 공정 신호 초기화
4. `SceneController`로 현재 메인 플레이 씬을 Single 모드 재로딩
5. 저장된 `NextProcess`에 따라 현재 미완료 공정을 처음부터 다시 시작

공정별 Reset API가 없어도 씬의 작업 오브젝트가 원본 상태로 복원되며, 타 개발자의 공정 코드에 Reset 인터페이스를 추가하지 않는다. 플레이어 위치는 기존 `PlayerPoseTracker` 정책을 유지한다.

### 3. 첫 대사 초기화

기존 ISSUE-021의 TTS 사전 준비 수정은 이미 적용돼 있었다. 중복 수정하지 않았다.

별도로 `ProcessRunner`와 `InGameDialogue`가 둘 다 `DataManager`를 비동기로 기다릴 때 continuation 순서에 따라 첫 공정 대사가 준비 전에 호출될 수 있는 경로를 확인했다. 씬에 `InGameDialogue`가 있으면 `InitializeAsync()` 완료를 기다린 뒤 첫 `BeginProcess`를 호출하도록 보강했다.

대사 초기화가 예외로 끝나더라도 공정 자체는 시작하며 경고를 남긴다.

### 4. 전체 코어 루프 자동 검증

검증기는 공정별 물리 동작을 가짜 성공 처리하지 않는다. 상위 루프가 의존하는 계약을 데이터와 실제 런타임 모델을 함께 사용해 검사한다.

프로필:

- `Tests/CoreLoopVerificationProfile.json`
- 예상 경로, 목적지 종류, 목적지 ID, 공정 정의 필요 여부, 신호 키를 데이터로 관리

검증 코드:

- `Tests/Editor/CoreLoopContractTests.cs`
- `Tests/Editor/CoreLoopVerificationLauncher.cs`
- `Tests/Editor/IUM.CoreLoopVerification.Tests.asmdef`

검증 항목 6개:

1. 전체 경로의 flow 목적지, 컷씬/영상, Build Settings 등록
2. 공정 정의, 단계, 대사 ID, 브리지 신호 일치
3. 실제 `UserProgressData.Complete/Reset`을 이용한 프롤로그부터 엔딩까지의 상태 전이
4. 모든 제작 Signal 단계의 요구량을 실제 `ProcessSignalBus`에 주입·조회·초기화
5. 도리·공포 부재의 공정별 접근 정책
6. 메인 플레이 씬의 러너·브리지·대사·일시정지 배선과 재시작 대상 씬 등록

Unity 메뉴 `Tools > IUM > Verify Full Core Loop`, Test Runner, batchmode 세 경로에서 같은 테스트를 실행할 수 있다. 열린 Editor에서 무인 실행할 때는 `Temp/IUMCoreLoopVerification.request` 요청 파일을 감지하며, 결과는 `Logs/CoreLoopVerification.latest.log`에 남긴다. `Temp`와 `Logs` 결과물은 커밋하지 않는다.

## 변경하지 않은 항목

- `Assets/@Scenes/GongpoScene.unity`
- 메인 플레이 씬 및 모든 프리팹
- `InkLineZone`, `InkLineTool`
- `PlaneZone`, `HandPlaneTool`, `WorkZone`
- `AssemblyTarget`, `AssemblySnapModule`, `AssemblyPart`, `MaleSnapPoint`
- `Grabbable`, XR 입력 및 목공 판정 코드
- `flow.json`, `process.json`, `dialogue.json`, `cutscene.json`
- 기존 `warehouseFin` 미커밋 `.meta`

## 검증 결과

### Unity 컴파일

- 수정 런타임 어셈블리: 컴파일 오류 0
- 신규 `IUM.CoreLoopVerification.Tests` 어셈블리: 컴파일 오류 0

### .NET 보조 컴파일

- 명령: `dotnet build Assembly-CSharp.csproj --no-restore --nologo`
- 결과: 오류 0, 경고 10
- 경고는 기존 Unity 참조의 `System.Net.Http` 및 `System.Security.Cryptography.Algorithms` 버전 충돌이다. 이번 변경 코드의 컴파일 경고가 아니다.

### 전체 루프 자동 검증

- 실행 시각(UTC): `2026-08-20T00:32:01.9604132Z`
- 결과: `Passed`
- 통과: 6
- 실패: 0
- 건너뜀: 0
- 미결정: 0
- 실행 시간: 0.071초

## 남은 수동 검증

다음은 이번 자동 검증의 의도적인 범위 밖이다.

- 실제 먹선, 대패, 끌질 조작이 각 공정 이벤트를 올바르게 발생시키는지
- 실제 조립 위치와 순서, 허용 오차가 적절한지
- Meta Quest에서의 입력, 햅틱, 손 위치와 조작감
- 재시작 버튼 클릭 후 화면 페이드와 체감 시간
- 네트워크 TTS 및 AI 응답

이 항목에서 문제가 발견되더라도 타 개발자의 공정 코드는 이번 브랜치에서 수정하지 않는다.

---

## FixedUI.zip 원본 시작 화면 통합

### 적용 원칙

- `FixedUI.zip`을 별도 Unity 프로젝트로 풀어 원본 `SampleScene`의 계층, 씬 의존성, 재질, 라이트맵, 볼륨, 버튼 영구 이벤트를 조사했다.
- 기존 시작 화면을 비슷하게 재디자인하지 않고, 원본 씬 내용을 `Assets/@Scenes/StartScene.unity`의 본문으로 사용했다. 기존 StartScene의 `.meta` GUID는 유지했다.
- 원본의 월드 스페이스 Canvas와 3D 스튜디오, FBX, 재질, 텍스처, 스카이박스, 베이크 라이트맵, 반사 프로브를 `Assets/@Developers/RYU/Start/FixedUI` 아래에 이식했다.
- 기존 HUD 관련 커밋(`ed4bf0f`, `5825f2e`)은 유지했고, 이번 변경은 시작 화면 통합에 한정했다.

### 호환 연결

- 원본 `PauseMenuController`의 씬 GUID를 `FixedUIStartMenuAdapter`가 이어받아 버튼의 기존 persistent onClick 연결을 보존했다.
- 원본 버튼 의미를 시작 화면에 맞게 `이어하기`, `옵션`, `게임 시작`, `나가기`로 연결했다.
- 실제 저장 확인, 새 게임, 이어하기, 옵션, 종료 처리는 기존 `StartMenuController` 공개 진입점으로 위임했다.
- UI Toolkit의 기존 메뉴 카드만 숨기고 옵션 패널과 확인 패널은 그대로 재사용했다.
- 월드 스페이스 Canvas의 이벤트 카메라를 Main Camera로 런타임 연결했다.
- 원본 PC URP 에셋을 StartScene 동안만 적용하고 씬 파괴 시 이전 파이프라인으로 복구한다.
- 원본 SDF에 없는 한글 글리프 문제는 기존 프로젝트의 `Giants-Bold.ttf`로 런타임 TMP 폰트를 만들어 해결했다.

### 원본 대조 기록

- 원본과 이식본의 씬 렌더 상태 311줄을 비교했다. 경로와 품질 레벨 이름을 제외한 렌더러, 재질, 셰이더, 색상, 텍스처, 라이트, 라이트맵 인덱스가 일치했다.
- 최초 원본 캡처에서 보였던 보라색은 ZIP의 직렬화된 에셋 값이 아니라 이전 임포트 캐시에 남은 결과였다. 원본 프로젝트를 전체 재임포트한 뒤에는 원본도 검정·회색으로 렌더링됐다.
- 원본에 없는 추가 글로벌 볼륨과 진단용 URP 전역 설정은 최종본에서 제거했다. ProjectSettings는 수정하지 않았다.

### 재사용 검증

- Unity 메뉴: `Tools > IUM > Validation > Run FixedUI Start Screen Probe`
- 결과: `Temp/FixedUIValidation/RuntimeReport.txt`
- 캡처: `Temp/FixedUIValidation/RuntimeGame.png`
- 검사 항목: 어댑터/기존 시작 컨트롤러/카메라, 누락 스크립트, 버튼 4개, 이벤트 카메라, 원본 URP, 베이크 라이트맵, 옵션 persistent listener, 옵션 패널 열림.
- 최종 실행 결과: 13개 항목 모두 PASS.

### 보호한 변경

- 타 개발자의 목공 공정 코드와 공포 씬은 수정하지 않았다.
- 기존 미커밋 파일 `Assets/warehouseFin/Scenes/SampleScene.unity.meta`는 스테이징 및 커밋에서 제외한다.

---

## StartScene 최소기능 플레이어

### 적용 내용

- 기존 `Assets/Prefabs/Player.prefab`을 StartScene 루트에 프리팹 인스턴스로 직접 배치했다. 런타임 생성 부트스트랩은 사용하지 않는다.
- 원본 FixedUI 카메라의 위치와 회전은 씬에 배치된 플레이어에 반영하고, 렌더 설정만 `StartScenePlayerInteraction`이 이어받는다. 원본 카메라는 비활성화해 MainCamera와 AudioListener 중복을 막는다.
- 기존 공용 플레이어 입력을 그대로 사용하므로 데스크톱에서 WASD 이동, 우클릭 시점, Q/E 스냅 회전, F/좌클릭 잡기를 사용할 수 있다.
- 기존 플레이어의 R 상호작용 명령을 화면 중앙의 World Space uGUI 버튼으로 전달해 시작 메뉴를 키보드로도 선택할 수 있게 했다.
- FixedUI 자동 프로브에 씬 직접 배치 플레이어, CharacterController, 데스크톱 입력 대체 경로, 플레이어 MainCamera, R 버튼 실행 검사를 추가했다.
- 최종 런타임 프로브 결과는 기존 FixedUI 검사를 포함해 21개 항목 모두 PASS였고, 씬 직접 배치 플레이어의 접지 상태도 확인했다. 누락 스크립트와 런타임 예외는 0개였다.

### 소유권 보호

- 타 개발자 소유인 `Player.prefab`과 `Assets/@Scripts/Player` 코드는 수정하지 않고 참조만 사용했다.
- StartScene과 RYU 전용 어댑터/검증 코드만 변경했다.
