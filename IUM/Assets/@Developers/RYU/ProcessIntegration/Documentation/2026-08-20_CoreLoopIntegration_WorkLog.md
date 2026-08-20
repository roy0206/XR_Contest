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
