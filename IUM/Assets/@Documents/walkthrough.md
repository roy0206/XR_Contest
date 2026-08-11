# Woodworking Tools Walkthrough

모든 목재 가공 도구(Saw, InkLine, Hammer, Chisel) 관련 스크립트 작성이 완료되었습니다! 🎉

## 📝 구현된 사항 (Changes Made)

> [!NOTE]
> 유니티 에디터에서 필요한 컴포넌트 어태치 및 에셋 세팅을 직접 진행하시면 정상적으로 동작합니다.

### 1. 톱 (SawTool & SawZone)
- **`SawZone.cs`**: `WorkZone`을 상속받아 톱질 횟수를 체크하고, 완료 시 `VisualWoodModifier.HideWaste()`를 통해 불필요한 목재(Waste)를 비활성화합니다.
- **`SawTool.cs`**: 톱의 이동 속도(minCutSpeed)와 방향(앞/뒤 양방향 180도 허용)을 체크하여 유효한 스트로크를 판정합니다. 완료 시 속도와 방향 정확도를 기반으로 `WorkResult`를 채점합니다. 톱밥 파티클(VFX) 및 햅틱 진동 연동이 포함되었습니다.

### 2. 먹매김 (InkLineZone & InkLineTool)
- **`InkLineZone.cs`**: `targetStart`, `targetEnd` Transform을 기준으로 실제 찍힌 선과 오차를 계산(거리 오차)하여 점수를 매기도록 확장되었습니다.
- **`InkLineTool.cs`**: 먹줄을 당겼다 놓을 때, 최대로 당긴 장력(Tension)을 계산하여 `InkLineZone`에 넘겨주며, 이 장력 수치가 점수(QualityScore)에 30% 반영됩니다.

### 3. 끌과 망치 (ChiselTool, ChiselZone & HammerTool)
- **`HammerTool.cs`**: 충돌 검사(Continuous Dynamic)를 통해 타격 속도(Velocity)를 `ChiselTool`로 전달합니다. 타격 시 잡고 있는 손에 강한 진동이 옵니다.
- **`ChiselZone.cs`**: 망치질 한 번당 깊이(Depth)가 누적되며, 목표치 도달 전까지 `VisualWoodModifier`를 통해 "Chiseled" BlendShape를 점진적으로 갱신합니다.
- **`ChiselTool.cs`**: Raycast를 쏘아 목재 표면에 사영 그래픽(ProjectionVisual)을 표시합니다. 망치로 타격 시, 끌 끝단 양쪽의 2-Point(`checkPointLeft`, `checkPointRight`)가 `ChiselZone`의 BoxCollider 영역 내부에 온전히 들어와 있는지 검사하여 유효 타격을 판별합니다. 성공/실패 시 각각 다른 사운드(SFX)와 진동이 재생됩니다.

---

## 🛠️ 다음 단계 (Verification Plan)

> [!IMPORTANT]
> 코드 레벨의 구현은 완료되었으나, 실제로 테스트하려면 유니티 에디터 상에서 다음 작업이 필요합니다:
> 
> 1. **프리팹 컴포넌트 부착 및 연결**
>    - SawZone, SawTool, ChiselZone, ChiselTool, HammerTool 스크립트를 각각의 오브젝트에 부착
>    - `SawTool`, `ChiselTool`, `HammerTool`에 알맞은 파티클 효과, 사운드 소스, Haptic 설정을 인스펙터에서 연결
> 2. **ChiselTool의 시각적 가이드 설정**
>    - `ChiselTool` 하위에 `projectionVisual`용 자식 오브젝트 생성 (Decal 혹은 Quad Mesh)
>    - 판정용 `checkPointLeft`, `checkPointRight` 빈 게임 오브젝트를 끌 날의 양 끝에 배치 후 스크립트에 할당
> 3. **목재(원목) 에셋 세팅**
>    - 원본 메쉬와 분리되어 잘려나갈 메쉬(Waste)를 분리하고 `VisualWoodModifier.wasteObject`에 연결
>    - `InkLineZone`에 `targetStart`, `targetEnd` 빈 오브젝트를 채점 위치에 배치

에디터에서 직접 세팅 후 씬을 플레이하시면서 의도한 대로 동작하는지 테스트해주시면 됩니다! 혹시 특정 도구가 동작하지 않거나 스크립트상 수정이 필요한 부분이 있다면 편하게 말씀해 주세요.
