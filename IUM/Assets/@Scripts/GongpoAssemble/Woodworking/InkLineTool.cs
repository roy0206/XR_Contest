using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class InkLineTool : MonoBehaviour, IWoodTool
{
    public ToolType GetToolType() => ToolType.InkLine;
    public bool IsActive() => true;

    public enum InkState
    {
        Idle,
        PinnedStart,
        PinnedEnd,
        Pulled
    }

    [Header("먹줄 상태")]
    public InkState currentState = InkState.Idle;

    [Header("핀 및 도구 위치 (자식 오브젝트 매핑)")]
    [Tooltip("실의 시작점이 될 핀 객체. 고정 시 나무에 붙습니다.")]
    public Transform pinStartTransform;
    [Tooltip("먹통 본체의 실이 나오는 위치")]
    public Transform toolEndTransform;
    [Tooltip("양손 조작 시 반대 손으로 실을 당기기 위한 가상의 그랩 객체")]
    public Transform stringCenterGrabTransform;

    [Header("실 렌더링")]
    public LineRenderer stringRenderer;

    [Header("실 당기기 저항 설정")]
    [Tooltip("실을 최대로 당길 수 있는 거리(미터)")]
    public float maxPullDistance = 1.0f; 

    private InkLineZone currentZone;
    private Vector3 pinnedStartPos;
    private Vector3 pinnedEndPos;

    private XRGrabInteractable toolGrabInteractable;
    private XRGrabInteractable centerGrabInteractable;

    private void Start()
    {
        if (stringRenderer != null)
        {
            stringRenderer.useWorldSpace = true; // 먹통이 움직일 때 로컬 좌표 꼬임 방지
        }

        // 먹통 자체의 그랩 인터랙터 가져오기
        toolGrabInteractable = GetComponent<XRGrabInteractable>();
        if (toolGrabInteractable != null)
        {
            // 트리거 버튼(Activate)을 눌렀을 때 핀을 고정하는 이벤트 연결
            toolGrabInteractable.activated.AddListener(OnToolActivated);
        }

        if (stringCenterGrabTransform != null)
        {
            centerGrabInteractable = stringCenterGrabTransform.GetComponent<XRGrabInteractable>();
            if (centerGrabInteractable != null)
            {
                centerGrabInteractable.selectExited.AddListener(OnStringReleased);
                centerGrabInteractable.gameObject.SetActive(false); // 처음에는 잡을 수 없음
                
                // 에러 방지: 실 중앙 오브젝트는 놓았을 때 던져질(Throw) 필요가 없으므로 던지기 기능 비활성화
                centerGrabInteractable.throwOnDetach = false;
            }
        }
    }

    private void Update()
    {
        UpdateStringVisuals();
        
        // 당겨지고 있을 때 저항 및 진동 연산
        if (currentState == InkState.Pulled && centerGrabInteractable != null && centerGrabInteractable.isSelected)
        {
            ApplyPullResistance();
        }
    }

    private void ApplyPullResistance()
    {
        // 1. 실의 원래 직선(시작점 -> 끝점) 상에서 현재 손이 있는 위치와 가장 가까운 점(수선의 발) 찾기
        Vector3 lineDir = (pinnedEndPos - pinnedStartPos);
        float lineLen = lineDir.magnitude;
        lineDir.Normalize();
        
        Vector3 v = stringCenterGrabTransform.position - pinnedStartPos;
        float t = Vector3.Dot(v, lineDir);
        Vector3 closestPoint = pinnedStartPos + lineDir * Mathf.Clamp(t, 0, lineLen);
        
        // 2. 당겨진 거리 계산 (직선으로부터 수직으로 벗어난 거리)
        float pullDistance = Vector3.Distance(stringCenterGrabTransform.position, closestPoint);
        
        // 3. 진동(저항감) 주기: 5cm(0.05m) 이상 당겼을 때부터 진동 시작
        if (pullDistance > 0.1f)
        {
            float intensity = Mathf.Clamp01((pullDistance - 0.05f) / (maxPullDistance - 0.05f));
            
            var interactor = centerGrabInteractable.firstInteractorSelecting;
            if (interactor is XRBaseInputInteractor inputInteractor)
            {
                // 거리가 멀어질수록 강하게 진동
                inputInteractor.SendHapticImpulse(intensity * 0.7f, Time.deltaTime);
            }
            
            // 4. 한계치 이상 당기면 강제로 손에서 놓침 (Snap)
            if (pullDistance > maxPullDistance)
            {
                if (centerGrabInteractable.interactionManager != null)
                {
                    centerGrabInteractable.interactionManager.SelectCancel(interactor, centerGrabInteractable);
                }
            }
        }
    }

    // 해당 좌표(핀 끝) 반경 10cm 이내에 존이 있는지 검사
    private InkLineZone FindZoneAtPosition(Vector3 pos)
    {
        Collider[] colliders = Physics.OverlapSphere(pos, 0.01f);
        foreach (var col in colliders)
        {
            InkLineZone zone = col.GetComponent<InkLineZone>();
            if (zone != null)
                return zone;
        }
        return null;
    }

    // 플레이어가 먹통을 쥐고 트리거 버튼을 눌렀을 때
    private void OnToolActivated(ActivateEventArgs arg)
    {
        if (currentState == InkState.Idle)
        {
            Vector3 rawPos = pinStartTransform != null ? pinStartTransform.position : transform.position;
            
            // "핀 끝부분"이 Zone 안에 있는지 직접 검사
            InkLineZone targetZone = FindZoneAtPosition(rawPos);
            if (targetZone == null) return; // 핀 근처에 구역이 없으면 무시

            // 허공을 찍었더라도 실제 목재 표면으로 좌표를 스냅(Snap)해옵니다.
            Vector3 surfacePos = targetZone.GetSurfacePoint(rawPos);
            
            PinStart(surfacePos, targetZone);
            
            // 햅틱 진동 피드백
            if (arg.interactorObject is XRBaseInputInteractor interactor)
            {
                interactor.SendHapticImpulse(0.5f, 0.1f);
            }
        }
        else if (currentState == InkState.PinnedStart)
        {
            Vector3 rawPos = toolEndTransform != null ? toolEndTransform.position : transform.position;
            
            // 두 번째 핀을 꽂을 때도 도구 끝부분 반경을 검사
            InkLineZone targetZone = FindZoneAtPosition(rawPos);
            if (targetZone == null) return;

            Vector3 surfacePos = targetZone.GetSurfacePoint(rawPos);
            
            PinEnd(surfacePos);
            
            if (arg.interactorObject is XRBaseInputInteractor interactor)
            {
                interactor.SendHapticImpulse(0.5f, 0.1f);
            }
        }
    }

    /// <summary>
    /// 첫 번째 핀을 먹줄 허용 구역(InkLineZone)에 고정합니다.
    /// </summary>
    public void PinStart(Vector3 position, InkLineZone zone)
    {
        pinnedStartPos = position;
        currentZone = zone;
        currentState = InkState.PinnedStart;
        
        if (pinStartTransform != null)
        {
            pinStartTransform.position = position;
            pinStartTransform.SetParent(zone.transform);
        }
    }

    /// <summary>
    /// 먹통을 당겨 두 번째 지점을 고정합니다.
    /// </summary>
    public void PinEnd(Vector3 position)
    {
        pinnedEndPos = position;
        currentState = InkState.PinnedEnd;
        
        if (centerGrabInteractable != null)
        {
            centerGrabInteractable.gameObject.SetActive(true);
            
            // 물리 폭발 방지 (생성 되자마자 튕겨나가지 않게)
            Rigidbody rb = centerGrabInteractable.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }

            // 먹통을 움직여도 실의 중앙 위치가 따라가지 않도록 부모 관계 해제
            stringCenterGrabTransform.SetParent(null);
            stringCenterGrabTransform.position = (pinnedStartPos + pinnedEndPos) / 2f;
        }
    }

    private void OnStringReleased(SelectExitEventArgs arg)
    {
        // 1. 실을 놓거나(놓쳤을 때) 해당 손에 강한 진동 '팍!'
        if (arg.interactorObject is XRBaseInputInteractor stringInteractor)
        {
            stringInteractor.SendHapticImpulse(1.0f, 0.15f);
        }

        // 2. 팽팽한 줄이 튕겨졌으므로 먹통을 쥐고 있는 반대쪽 손에도 타격감 전달
        if (toolGrabInteractable != null && toolGrabInteractable.isSelected)
        {
            if (toolGrabInteractable.firstInteractorSelecting is XRBaseInputInteractor toolInteractor)
            {
                toolInteractor.SendHapticImpulse(0.8f, 0.15f);
            }
        }

        if (currentState == InkState.PinnedEnd || currentState == InkState.Pulled)
        {
            SnapString();
        }
    }

    /// <summary>
    /// 실을 튕겨서 나무에 선을 남깁니다.
    /// </summary>
    private void SnapString()
    {
        if (currentZone != null)
        {
            Debug.Log("[InkLineTool] SnapString 호출됨 - currentZone에 이벤트 전달");
            currentZone.OnInkLineSnapped(pinnedStartPos, pinnedEndPos);
        }
        else
        {
            Debug.LogWarning("[InkLineTool] currentZone이 Null이라 선을 튕길 수 없습니다!");
        }
        ResetTool();
    }

    public void ResetTool()
    {
        currentState = InkState.Idle;
        currentZone = null;
        if (pinStartTransform != null)
        {
            pinStartTransform.SetParent(transform);
            pinStartTransform.localPosition = toolEndTransform.localPosition;
        }
        if (centerGrabInteractable != null)
        {
            Rigidbody rb = centerGrabInteractable.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }

            // 실 중앙 오브젝트를 다시 먹통의 자식으로 원상복구
            stringCenterGrabTransform.SetParent(transform);
            stringCenterGrabTransform.localPosition = Vector3.zero;
            centerGrabInteractable.gameObject.SetActive(false);
        }
    }

    private void UpdateStringVisuals()
    {
        if (stringRenderer == null) return;

        switch (currentState)
        {
            case InkState.Idle:
                stringRenderer.enabled = false;
                break;
            case InkState.PinnedStart:
                stringRenderer.enabled = true;
                stringRenderer.positionCount = 2;
                stringRenderer.SetPosition(0, pinStartTransform.position);
                stringRenderer.SetPosition(1, toolEndTransform.position);
                break;
            case InkState.PinnedEnd:
            case InkState.Pulled:
                stringRenderer.enabled = true;
                
                if (centerGrabInteractable != null && centerGrabInteractable.isSelected)
                {
                    currentState = InkState.Pulled;
                    stringRenderer.positionCount = 3;
                    stringRenderer.SetPosition(0, pinnedStartPos);
                    stringRenderer.SetPosition(1, stringCenterGrabTransform.position);
                    stringRenderer.SetPosition(2, pinnedEndPos);
                }
                else
                {
                    stringRenderer.positionCount = 2;
                    stringRenderer.SetPosition(0, pinnedStartPos);
                    stringRenderer.SetPosition(1, pinnedEndPos);
                }
                break;
        }
    }
}
