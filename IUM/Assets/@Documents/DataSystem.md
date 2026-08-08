---
created: "2026-07-27"
updated: "2026-07-27"
tags: ["XR경진대회", "PROJECT이음", "데이터"]
type: "system-note"
project: "PROJECT 이음"
category: "데이터 시스템"
---

# PROJECT 이음 - 데이터 시스템

## 1. 구성

데이터는 두 종류로 나눈다.

| **구분**      | **내용**                          | **위치**                                     |
|---------------|-----------------------------------|----------------------------------------------|
| 정적 데이터   | 빌드에 포함되는 읽기 전용 JSON    | `Assets/@AddressableAssets/Data/Static`      |
| 사용자 데이터 | 진행 상황과 옵션                  | `Application.persistentDataPath/UserData`    |

사용자 데이터는 Addressables에 넣지 않는다.

## 2. 정적 데이터

### 2.1 매니페스트

빌드에 포함하는 모든 정적 JSON은 `Assets/@AddressableAssets/Data/manifest.json`에 등록하고 Addressables 자산으로 추가한다.

```json
{
  "schemaVersion": 1,
  "files": [
    { "key": "app", "address": "ium/data/static/app", "required": true }
  ]
}
```

- `key`: 코드에서 테이블을 꺼낼 때 쓰는 이름
- `address`: Addressables 주소. 파일을 옮겨도 주소는 유지한다
- `required`: `false`이면 로드에 실패해도 경고만 남기고 진행한다

매니페스트 자체의 주소는 `ium/data/manifest`이며, 데이터 자산은 `ium-data` 라벨을 사용한다.

### 2.2 사용

```csharp
var app = DataManager.Instance.Static.Get<AppConfig>("app");
```

정적 데이터는 `IDataTextProvider`를 통해 읽으므로, 나중에 원격 배포로 전환해도 런타임 코드는 바뀌지 않는다.

## 3. 사용자 데이터

### 3.1 저장 파일

`Application.persistentDataPath/UserData/user.json`

```json
{
  "settings": {
    "masterVolume": 1.0,
    "musicVolume": 0.7,
    "dialogueVolume": 1.0,
    "environmentVolume": 0.8
  },
  "progress": {
    "nextProcess": "prologue",
    "makmeokGrade": "none",
    "sawingGrade": "none",
    "chiselingGrade": "none"
  }
}
```

저장 슬롯은 1개이며 로그인과 프로필은 사용하지 않는다.

### 3.2 진행 상태

진행 상태는 `nextProcess` 하나로 표현한다. 공정은 아래 순서로 한 줄로 이어지고 분기가 없으므로, 이전 공정의 완료 여부는 저장하지 않고 비교로 판정한다.

```
prologue → tutorial → makmeok → sawing → chiseling
        → purlinInstall → gongpoPuzzle → ending → completed
```

| **판정**             | **방법**                                  |
|----------------------|-------------------------------------------|
| 특정 공정 완료 여부  | `Progress.IsCompleted(ProcessId.Tutorial)`|
| 저장 데이터 존재 여부 | `Progress.HasSaveData`                    |
| 이어하기 진입 지점   | `Progress.NextProcess`                    |

프롤로그 완료, 튜토리얼 완료, 현재 챕터, 공포 퍼즐 완료, 엔딩 완료는 모두 위 값에서 계산한다. 별도 필드로 저장하지 않는다.

### 3.3 등급

등급은 공정 순서에서 계산할 수 없으므로 먹매김·톱질·끌질 세 개만 따로 저장한다.

```
None < Fail < Assisted < Pass < Excellent
```

낮은 등급부터 나열되어 있어, 재도전 결과가 이전보다 낮으면 갱신되지 않는다. 각 공정의 최고 평가를 유지한다(CR-11).

### 3.4 작업 중 상태

절단면, 홈 형상, 부재 위치 등 공정 진행 중의 세부 상태는 저장하지 않는다. 이어하기는 항상 공정의 시작 지점에서 재개한다.

## 4. 런타임 API

```csharp
await DataManager.Instance.InitializeAsync();

// 공정 통과
DataManager.Instance.Progress.Complete(ProcessId.Sawing, ProcessGrade.Excellent);
await DataManager.Instance.SaveUserAsync();

// 옵션 변경
DataManager.Instance.Settings.MusicVolume = 0.5f;
DataManager.Instance.ApplyAudioSettings();
await DataManager.Instance.SaveUserAsync();

// 새 게임 (옵션은 유지된다)
DataManager.Instance.Progress.Reset();
await DataManager.Instance.SaveUserAsync();
```

### 4.1 저장 시점

프롤로그 완료, 튜토리얼 완료, 각 공정 통과, Chapter 1 완료, 도리 설치 완료, 공포 퍼즐 완료, 엔딩 완료, 옵션 변경 시 저장한다.

### 4.2 볼륨 적용

`ApplyAudioSettings()`가 저장된 볼륨을 믹서에 반영한다.

| **설정 값**       | **믹서 채널** |
|-------------------|---------------|
| masterVolume      | MASTER        |
| musicVolume       | BGM           |
| environmentVolume | SFX           |
| dialogueVolume    | 미연결        |

대사 전용 채널은 아직 없다. 값은 저장되며 옵션 UI 작업 시 채널을 추가해 연결한다.

## 5. 오류 처리

| **상황**              | **동작**                                        |
|-----------------------|-------------------------------------------------|
| 저장 파일 없음        | 기본값으로 시작                                 |
| 저장 파일 손상        | 경고 로그 후 기본값으로 시작                    |
| 저장 실패             | 예외를 던지지 않고 `SaveFailed` 이벤트만 발생   |
| 매니페스트 오류       | `DataLoadException` 발생, 초기화 실패로 처리    |

저장은 임시 파일에 먼저 기록한 뒤 교체하므로, 저장이 중단되어도 기존 저장 파일은 손상되지 않는다. 저장 실패로 게임 진행이 멈추지 않는다.

## 6. 필드 변경

저장 필드는 아직 확정되지 않았으며 구현 진행에 따라 변경한다.

- 필드 추가: `UserSettingsData` 또는 `UserProgressData`에 프로퍼티를 추가한다. 기존 저장 파일에 없는 필드는 기본값으로 채워진다.
- 필드 삭제: 프로퍼티를 지운다. 저장 파일에 남아 있는 값은 무시된다.
- 필드 의미 변경: 기존 저장 파일과 충돌하면 이름을 바꾼다. 별도의 버전 변환 절차는 두지 않는다.
