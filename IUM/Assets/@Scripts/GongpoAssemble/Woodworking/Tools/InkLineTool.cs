using UnityEngine;

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

    [Header("State")]
    public InkState currentState = InkState.Idle;

    [Header("Pin and Tool Transforms")]
    [Tooltip("Pin start object. Attaches to wood surface when pinned.")]
    public Transform pinStartTransform;
    [Tooltip("Position where the string exits the ink tool body.")]
    public Transform toolEndTransform;
    [Tooltip("Virtual grab object at the center of the string for pulling.")]
    public Transform stringCenterGrabTransform;

    [Header("String Rendering")]
    public LineRenderer stringRenderer;

    [Header("Pull Resistance Settings")]
    [Tooltip("Maximum distance (meters) the string can be pulled before auto-snap.")]
    public float maxPullDistance = 1.0f;

    [Header("시각적 가이드 (사영)")]
    [Tooltip("표면에 그려질 가이드 그래픽 (점이 찍힐 위치 표시)")]
    public GameObject projectionVisual;

    // ── Private state ──────────────────────────────────────────────────────
    private InkLineZone currentZone;
    private Vector3 pinnedStartPos;
    private Vector3 pinnedEndPos;

    private Grabbable customGrabbable;        // Grabbable on the ink tool body
    private Grabbable customCenterGrabbable;  // Grabbable on the center pull point

    // ── Unity lifecycle ────────────────────────────────────────────────────
    private void Start()
    {
        if (stringRenderer != null)
            stringRenderer.useWorldSpace = true;

        if (projectionVisual != null)
            projectionVisual.SetActive(false);

        // Ink tool body grab
        customGrabbable = GetComponent<Grabbable>();
        if (customGrabbable != null)
            customGrabbable.Activated += OnToolActivatedCustom;

        // Center pull-point grab
        if (stringCenterGrabTransform != null)
        {
            customCenterGrabbable = stringCenterGrabTransform.GetComponent<Grabbable>();
            if (customCenterGrabbable != null)
            {
                customCenterGrabbable.Released += OnStringReleasedCustom;
                customCenterGrabbable.gameObject.SetActive(false); // Hidden until both pins are placed
            }
        }
    }

    private void Update()
    {
        UpdateStringVisuals();
        UpdateProjectionVisual();

        // Resistance + auto-snap while the string is being pulled
        if (currentState == InkState.Pulled && customCenterGrabbable != null && customCenterGrabbable.IsHeld)
            ApplyPullResistance();
    }

    private void UpdateProjectionVisual()
    {
        if (currentState != InkState.Idle && currentState != InkState.PinnedStart) 
        {
            if (projectionVisual != null) projectionVisual.SetActive(false);
            return;
        }

        if (projectionVisual == null) return;

        Vector3 checkPos = toolEndTransform != null ? toolEndTransform.position : transform.position;
        InkLineZone zone = FindZoneAtPosition(checkPos);
        
        if (zone != null)
        {
            Vector3 surfacePos = zone.GetSurfacePoint(checkPos);
            projectionVisual.SetActive(true);
            
            // 데칼을 표면에 조금 더 띄우기 위해 중심에서 바깥쪽으로 향하는 벡터를 씁니다.
            Vector3 dirFromCenter = (surfacePos - zone.transform.position).normalized;
            projectionVisual.transform.position = surfacePos + dirFromCenter * 0.001f;

            if (dirFromCenter != Vector3.zero)
            {
                projectionVisual.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.up, dirFromCenter), dirFromCenter);
            }
        }
        else
        {
            projectionVisual.SetActive(false);
        }
    }

    // ── Pull resistance ────────────────────────────────────────────────────
    private void ApplyPullResistance()
    {
        // 1. Find closest point on the original line (start -> end)
        Vector3 lineDir = (pinnedEndPos - pinnedStartPos).normalized;
        float lineLen   = (pinnedEndPos - pinnedStartPos).magnitude;

        Vector3 centerPos    = customCenterGrabbable.transform.position;
        float   t            = Vector3.Dot(centerPos - pinnedStartPos, lineDir);
        Vector3 closestPoint = pinnedStartPos + lineDir * Mathf.Clamp(t, 0f, lineLen);

        // 2. Perpendicular pull distance
        float pullDistance = Vector3.Distance(centerPos, closestPoint);

        // 3. Haptic vibration proportional to pull distance (> 5 cm threshold)
        if (pullDistance > 0.05f)
        {
            float intensity = Mathf.Clamp01((pullDistance - 0.05f) / (maxPullDistance - 0.05f));
            UserInput.Instance.SendHapticImpulse(customCenterGrabbable.Holder.Hand, intensity * 0.7f, Time.deltaTime);
        }

        // 4. Auto-release if pulled beyond max distance
        if (pullDistance > maxPullDistance)
        {
            customCenterGrabbable.Holder.Release();
        }
    }

    // ── Tool activation (trigger press while holding ink tool) ────────────
    private void OnToolActivatedCustom(Grabbable g)
    {
        ProcessToolActivation(true);
    }

    // ── Zone lookup ────────────────────────────────────────────────────────
    private InkLineZone FindZoneAtPosition(Vector3 pos)
    {
        Collider[] colliders = Physics.OverlapSphere(pos, 0.01f);
        foreach (var col in colliders)
        {
            var zone = col.GetComponent<InkLineZone>();
            if (zone != null) return zone;
        }
        return null;
    }

    private void ProcessToolActivation(bool isCustom)
    {
        // 먹통 끝부분(ToolEnd)이 유효한 InkLineZone 안에 있어야만 동작
        Vector3 checkPos = toolEndTransform != null ? toolEndTransform.position : transform.position;
        InkLineZone targetZone = FindZoneAtPosition(checkPos);
        
        if (targetZone == null) return;

        // 점이 찍힐 최종 위치: ToolEnd 위치에 해당하는 표면점 사용
        Vector3 finalPinPos = targetZone.GetSurfacePoint(checkPos);

        if (currentState == InkState.Idle)
        {
            PinStart(finalPinPos, targetZone);

            if (isCustom && customGrabbable != null && customGrabbable.IsHeld)
                UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, 0.5f, 0.1f);
        }
        else if (currentState == InkState.PinnedStart)
        {
            PinEnd(finalPinPos);

            if (isCustom && customGrabbable != null && customGrabbable.IsHeld)
                UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, 0.5f, 0.1f);
        }
    }

    // ── Pin state machine ──────────────────────────────────────────────────
    /// <summary>Pins the first endpoint to an InkLineZone.</summary>
    public void PinStart(Vector3 position, InkLineZone zone)
    {
        pinnedStartPos = position;
        currentZone    = zone;
        currentState   = InkState.PinnedStart;

        if (pinStartTransform != null)
        {
            pinStartTransform.position = position;
            pinStartTransform.SetParent(zone.transform);
        }
    }

    /// <summary>Pins the second endpoint and activates the center pull point.</summary>
    public void PinEnd(Vector3 position)
    {
        pinnedEndPos = position;
        currentState = InkState.PinnedEnd;

        // Place center grab point at the midpoint
        if (stringCenterGrabTransform != null)
        {
            stringCenterGrabTransform.SetParent(null);
            stringCenterGrabTransform.position = (pinnedStartPos + pinnedEndPos) / 2f;
        }

        if (customCenterGrabbable != null)
        {
            customCenterGrabbable.gameObject.SetActive(true);
            customCenterGrabbable.transform.SetParent(null);
            customCenterGrabbable.transform.position = (pinnedStartPos + pinnedEndPos) / 2f;
        }
    }

    // ── String release ─────────────────────────────────────────────────────
    private void OnStringReleasedCustom(Grabbable grabbable, GrabHandModule hand)
    {
        // Strong haptic on the hand that released
        if (hand != null)
            UserInput.Instance.SendHapticImpulse(hand.Hand, 1.0f, 0.15f);

        // Impact haptic on the hand holding the ink tool body
        if (customGrabbable != null && customGrabbable.IsHeld)
            UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, 0.8f, 0.15f);

        // Calculate tension before snapping
        float tension = 0f;
        if (currentState == InkState.Pulled && customCenterGrabbable != null)
        {
            Vector3 lineDir = (pinnedEndPos - pinnedStartPos).normalized;
            float lineLen   = (pinnedEndPos - pinnedStartPos).magnitude;
            Vector3 centerPos    = customCenterGrabbable.transform.position;
            float   t            = Vector3.Dot(centerPos - pinnedStartPos, lineDir);
            Vector3 closestPoint = pinnedStartPos + lineDir * Mathf.Clamp(t, 0f, lineLen);
            float pullDistance = Vector3.Distance(centerPos, closestPoint);
            tension = Mathf.Clamp01(pullDistance / maxPullDistance);
        }

        if (currentState == InkState.PinnedEnd || currentState == InkState.Pulled)
            SnapString(tension);
    }

    // ── Snap string ────────────────────────────────────────────────────────
    /// <summary>Snaps the taut string to leave an ink mark on the wood.</summary>
    private void SnapString(float tension = 1f)
    {
        if (currentZone != null && currentZone.woodModifier != null)
        {
            Debug.Log($"[InkLineTool] SnapString called with tension: {tension:F2}");
            
            // 한 목재에 여러 개의 InkLineZone이 있을 수 있으므로, 그려진 선과 가장 가까운(오차가 적은) 미완료 Zone을 찾습니다.
            InkLineZone[] allZones = currentZone.woodModifier.GetComponentsInChildren<InkLineZone>();
            InkLineZone bestZone = null;
            float minDistanceError = float.MaxValue;
            
            foreach (var zone in allZones)
            {
                if (zone.IsCompleted) continue; // 이미 완료된 선은 제외

                float error = zone.CalculateDistanceError(pinnedStartPos, pinnedEndPos);
                if (error < minDistanceError)
                {
                    minDistanceError = error;
                    bestZone = zone;
                }
            }
            
            if (bestZone != null)
            {
                bestZone.OnInkLineSnapped(pinnedStartPos, pinnedEndPos, tension);
            }
            else
            {
                // 모든 선이 완료되었거나 찾지 못한 경우 그냥 기본 currentZone에 전달 (피드백용)
                currentZone.OnInkLineSnapped(pinnedStartPos, pinnedEndPos, tension);
            }
        }
        else if (currentZone != null)
        {
            currentZone.OnInkLineSnapped(pinnedStartPos, pinnedEndPos, tension);
        }
        else
        {
            Debug.LogWarning("[InkLineTool] currentZone is null - cannot snap string!");
        }
        ResetTool();
    }

    // ── Reset ──────────────────────────────────────────────────────────────
    public void ResetTool()
    {
        currentState = InkState.Idle;
        currentZone  = null;

        if (pinStartTransform != null)
        {
            pinStartTransform.SetParent(transform);
            pinStartTransform.localPosition = toolEndTransform.localPosition;
        }

        if (stringCenterGrabTransform != null)
        {
            stringCenterGrabTransform.SetParent(transform);
            stringCenterGrabTransform.localPosition = Vector3.zero;
        }

        if (customCenterGrabbable != null)
        {
            customCenterGrabbable.transform.SetParent(transform);
            customCenterGrabbable.transform.localPosition = Vector3.zero;
            customCenterGrabbable.gameObject.SetActive(false);
        }
    }

    // ── String visuals ─────────────────────────────────────────────────────
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
                bool isPulled = customCenterGrabbable != null && customCenterGrabbable.IsHeld;

                if (isPulled)
                {
                    currentState = InkState.Pulled;
                    stringRenderer.positionCount = 3;
                    stringRenderer.SetPosition(0, pinnedStartPos);
                    stringRenderer.SetPosition(1, customCenterGrabbable.transform.position);
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
