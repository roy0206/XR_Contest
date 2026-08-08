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

        // Resistance + auto-snap while the string is being pulled
        if (currentState == InkState.Pulled && customCenterGrabbable != null && customCenterGrabbable.IsHeld)
            ApplyPullResistance();
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

    private void ProcessToolActivation(bool isCustom)
    {
        if (currentState == InkState.Idle)
        {
            Vector3 rawPos = pinStartTransform != null ? pinStartTransform.position : transform.position;
            InkLineZone targetZone = FindZoneAtPosition(rawPos);
            if (targetZone == null) return;

            Vector3 surfacePos = targetZone.GetSurfacePoint(rawPos);
            PinStart(surfacePos, targetZone);

            if (isCustom && customGrabbable != null && customGrabbable.IsHeld)
                UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, 0.5f, 0.1f);
        }
        else if (currentState == InkState.PinnedStart)
        {
            Vector3 rawPos = toolEndTransform != null ? toolEndTransform.position : transform.position;
            InkLineZone targetZone = FindZoneAtPosition(rawPos);
            if (targetZone == null) return;

            Vector3 surfacePos = targetZone.GetSurfacePoint(rawPos);
            PinEnd(surfacePos);

            if (isCustom && customGrabbable != null && customGrabbable.IsHeld)
                UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, 0.5f, 0.1f);
        }
    }

    // ── Zone lookup ────────────────────────────────────────────────────────
    private InkLineZone FindZoneAtPosition(Vector3 pos)
    {
        Collider[] colliders = Physics.OverlapSphere(pos, 0.1f);
        foreach (var col in colliders)
        {
            var zone = col.GetComponent<InkLineZone>();
            if (zone != null) return zone;
        }
        return null;
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

        if (currentState == InkState.PinnedEnd || currentState == InkState.Pulled)
            SnapString();
    }

    // ── Snap string ────────────────────────────────────────────────────────
    /// <summary>Snaps the taut string to leave an ink mark on the wood.</summary>
    private void SnapString()
    {
        if (currentZone != null)
        {
            Debug.Log("[InkLineTool] SnapString called - forwarding to currentZone");
            currentZone.OnInkLineSnapped(pinnedStartPos, pinnedEndPos);
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
