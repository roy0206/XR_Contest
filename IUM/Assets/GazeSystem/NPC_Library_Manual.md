# 📘 NPC 시선 & 리액션 제어 통합 라이브러리 사용 설명서 (Manual)

본 라이브러리는 **비행형 호위 펫(이음이)** 및 **지상형 일반 NPC(노장 / legendOldman)** 등 게임 내 다양한 NPC들의 시선 트래킹과 애니메이션 리액션 동작을 단일 컴포넌트 수준에서 자립적이고 유연하게 제어하기 위해 특수 제작되었습니다.

---

## 1. ⚙️ 핵심 설정: NPC 타입 스위치 (isFlyingNPC)

인스펙터의 `NPC Type Mode`에 위치한 **`isFlyingNPC`** 토글 스위치 하나로 물리 거동 방식을 즉시 전환할 수 있습니다.

### 🕊️ A. 비행형 NPC 모드 (`isFlyingNPC = True` - 이음이 전용)
* **작동 특성**: 
  * 플레이어가 WASD로 진짜 움직일 때만 마지막 이동 방향의 **왼쪽 6m 지점**으로 비행 추종합니다.
  * 플레이어가 다가올 경우, 6m의 **절대 거리** 간격을 지키기 위해 이음이가 반대 방향으로 스르륵 밀려나 도망칩니다.
  * 대기 시 공중 둥실둥실(Bobbing) 및 물리 목 쏠림 관성이 작동합니다.

### 🕴️ B. 지상형 NPC 모드 (`isFlyingNPC = False` - 노장 등)
* **작동 특성**:
  * 공중 부유, 비행 이동, 가변 가속도, 6m 간격 추종 연산이 **100% 비활성화(Skip)**됩니다.
  * 제자리에 자연스럽게 서서 플레이어를 마주 보도록 회전하며, 시선 처리(Gaze) 및 애니메이터 리액션(끄덕임, 절레절레, 인사 등)만 조용히 구동됩니다.

---

## 👴 2. 노장 (legendOldman) 캐릭터 적용 실무 단계

노장(할아버지) NPC 오브젝트에 이 시스템을 입히는 구체적인 방법입니다.

1. **컴포넌트 추가**:
   * 하이러키 창에서 노장 프리팹 혹은 오브젝트를 선택하고, **`EeumAnimationTrigger`** 스크립트를 추가(Add Component)합니다.
   * `GazeTracker` 및 `NPCGazeController` 등 필수 시선 부속 컴포넌트들은 실행 시 자동으로 부착되므로 별도로 추가하지 않으셔도 됩니다.
2. **인스펙터 속성 설정**:
   * **`Is Flying NPC`**: ⬜ **체크를 해제(False)** 합니다. (서 있는 NPC 전용 세팅)
   * **`Rotation Speed`**: 플레이어를 향해 몸을 돌릴 때의 회전 속도입니다. (기본 `8.0` 권장)
3. **Animator Controller 파라미터 등록**:
   * 노장의 Animator Controller에 아래 매개변수들을 타입에 맞춰 등록해 줍니다:
     * `greet` : **Bool** 타입 (인사)
     * `wonder` : **Bool** 타입 (기웃/갸웃거리기)
     * `happy` : **Trigger** 타입 (신남/만족 제스처)
     * `watch` : **Bool** 타입 (주시하기)
4. **대기 시 순차 릴레이 리액션 활용**:
   * 플레이어가 근처에 가만히 서 있을 때, 노장 또한 대화 릴레이 루프를 타며 인사 ➔ 고개 끄덕 ➔ 고개 절레 ➔ 만족 제스처(Happy) ➔ 갸웃거리기를 차례대로 무한 반복합니다.

---

## 🕶️ 3. VR 환경 및 플레이어 스크립트 미지원 대응 (Failsafe)

이 라이브러리는 타 프로젝트로 임포트하거나 **VR (HMD) 환경**에 장착할 때 발생할 수 있는 특수한 작동 상황에 대해 **강력한 자동 감지(Failsafe) 장치**를 내장하고 있습니다.

### 🔍 A. 플레이어 조종 스크립트(PlayerMovement)가 없을 때
* **해결**: 본 컴포넌트는 `PlayerMovement` 스크립트를 탐색하되, 만약 찾지 못하면 아래 순서대로 플레이어를 **100% 자동 추적**합니다:
  1. 씬 내에 **`Player` 태그**가 지정된 GameObject를 플레이어로 간주하고 따라갑니다.
  2. 만약 태그된 오브젝트도 없다면, 현재 활성화된 **`MainCamera`** (VR 카메라 헤드셋 위치)를 플레이어로 인식하여 머리맡 왼쪽 6m로 날아가 작동합니다.
  * 즉, 어떠한 조종 스크립트 의존성 없이도 프로젝트에 갖다 붙이기만 하면 플레이어를 즉시 알아채고 곁을 지킵니다!

### ⚡ B. VR 텔레포트(순간이동) 시 대처
* VR 환경에서는 플레이어가 텔레포트 기능으로 한순간에 수 미터에서 수십 미터씩 좌표 이동을 행할 수 있습니다.
* **해결**: 플레이어와 펫의 간격이 **12.0m 이상** 벌어질 경우, 동적 가변 가속도에 의해 펫이 화면 바깥으로 미친 듯이 초고속 로켓 비행을 일으키는 버그를 예방하기 위해, **즉각 6m 비행 스팟 지점으로 소리 없이 순간이동(Teleport) 동기화**하여 재배치됩니다. 플레이어의 VR 멀미를 유발하지 않고 쾌적하게 따라붙습니다.

---

## 📢 4. 캐릭터별 상세 이벤트 호출 & 연동 방법

기획자/디자이너를 위한 **무코딩(No-Code) 인스펙터 연동** 및 개발자를 위한 **C# 스크립트 제어**법을 캐릭터 유형별로 상세히 안내합니다.

---

### 🕊️ [유형 A] 이음이 (비행형 호위 펫) 전용 이벤트 호출 방식

이음이는 **경로 이동(Patrol / Waypoint)**을 돌며 특정 경유지에 도착할 때 이벤트를 호출하는 기획이 주로 사용됩니다.

#### 1) 무코딩 유니티 인스펙터 연동 (Unity Event)
* **경로 및 도착 이벤트 설정 (`Force Path Settings`)**:
  * 이음이 인스펙터에 위치한 `Patrol Routes` (Force Path Settings) 목록에 요소를 추가합니다.
  * **`Waypoint`**: 이동시킬 씬 안의 빈 오브젝트(Transform)를 할당합니다.
  * **`Look Target`**: 이동하면서 쳐다볼 시선 타겟을 지정합니다. (비워두면 기본 플레이어를 봅니다.)
  * **`On Arrived` (도착 시 이벤트)**: 
    * 여기에 `+` 버튼을 누르고 이벤트를 실행할 오브젝트를 끌어다 놓은 뒤, 호출할 퍼블릭 함수를 선택합니다.
    * 예: 씬 안의 상자 오브젝트를 넣고 `Box.Open()` 을 연결하면, 이음이가 웨이포인트에 안착하는 순간 자동으로 상자가 열립니다!

#### 2) C# 스크립트 연동
```csharp
using UnityEngine;
using GazeSystem;

public class EeumCommandCenter : MonoBehaviour
{
    [SerializeField] private EeumAnimationTrigger eeum; 
    [SerializeField] private Transform specialAnchor; // 특별히 앉혀둘 가구 위치

    // C# 코드로 이음이를 특정 앵커 가구에 즉시 앉히고 5초 동안 끄덕이게 명령!
    public void CommandEeumToSitAndNod()
    {
        if (eeum != null)
        {
            // 1. 강제로 앵커 자리에 가 있도록 순간 셋업
            // (homeAnchor를 실시간 할당해주면 그리로 날아가 고정됩니다.)
            // EeumAnimationTrigger 인스펙터 필드인 homeAnchor 값을 덮어씁니다.
            typeof(EeumAnimationTrigger)
                .GetField("homeAnchor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(eeum, specialAnchor);

            // 2. 앵커 안착 후 고개 끄덕 반응 실행
            eeum.PlayNodReaction(3.0f);
        }
    }
}
```

---

### 👴 [유형 B] 노장 (지상형 대화 NPC) 전용 이벤트 호출 방식

노장은 날아다니지 않으므로, **대화 텍스트가 출력되는 타이밍**이나 **시나리오 컷씬**에 맞춰 특정 애니메이션 제스처와 시선 락온을 유기적으로 호출하는 것이 핵심입니다.

#### 1) 무코딩 유니티 인스펙터 연동 (Unity Event)
* **시나리오 트리거 영역/대화 시스템 연동**:
  * 씬 내에 콜라이더(Trigger)를 두고, 트리거 감지 컴포넌트의 `OnTriggerEnter` 유니티 이벤트 창에 노장을 드래그합니다.
  * 우측 함수 드롭다운에서 `EeumAnimationTrigger`를 선택하고, 원하는 반응 함수를 선택합니다:
    * `PlayGreetAnim (float)`: 호출 창에 `2.0`을 적으면 대화 진입 시 노장이 나를 향해 2초 동안 정중하게 인사합니다!
    * `PlayNodReaction (float)`: 호출 창에 `1.5`를 적으면 대화 동의의 의미로 1.5초 동안 고개를 끄덕입니다!
    * `PlayShakeReaction (float)`: 부정/의심 상황에서 호출 창에 `2.0`을 적으면 고개를 절레절레 흔듭니다!

#### 2) C# 스크립트 연동 (대화 매니저 연동 예시)
```csharp
using UnityEngine;
using GazeSystem;

public class DialogueManager : MonoBehaviour
{
    [SerializeField] private EeumAnimationTrigger nojangNPC; // 노장 컴포넌트

    // 대화 진행 중 특정 타이밍에 호출되는 함수들
    public void OnDialogueLineChanged(int lineIndex)
    {
        if (nojangNPC == null) return;

        switch (lineIndex)
        {
            case 1: // "오, 자네 왔는가?" (인사)
                nojangNPC.PlayGreetAnim(2.0f);
                break;

            case 3: // "음.. 그건 숭례문 복원에 아주 중요한 자료지." (끄덕임)
                nojangNPC.PlayNodReaction(2.5f);
                break;

            case 5: // "하지만 그렇게 복원해서는 안 되네!" (절레절레 + 시선 레이저 고정)
                nojangNPC.PlayShakeReaction(2.0f);
                nojangNPC.SetGazeLock(true); // 시선을 무조건 플레이어에게 고정!
                break;

            case 7: // "다시 잘 생각해보게." (시선 고정 해제하여 자유 시선으로 원복)
                nojangNPC.SetGazeLock(false);
                break;
        }
    }
}
```
