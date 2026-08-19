using UnityEngine;

public class HandPlaneTool : MonoBehaviour, IWoodTool
{
    public ToolType GetToolType() => ToolType.HandPlane;
    public bool IsActive() => true;

    [Header("Planing Settings")]
    [Tooltip("Minimum speed required to register a valid cut (m/s).")]
    public float minCutSpeed = 0.1f;
    [Tooltip("Local direction along which the plane cuts.")]
    public Vector3 cuttingDirection = Vector3.forward;
    [Tooltip("Maximum angle deviation between movement and cutting direction (degrees).")]
    public float maxAngleTolerance = 30f;

    [Header("Feedback")]
    public ParticleSystem woodShavingParticles;
    [Range(0f, 1f)] public float hapticIntensity = 0.3f;
    public float hapticDuration = 0.05f;

    // ── Private state ──────────────────────────────────────────────────────
    private PlaneZone currentZone;
    private Vector3   lastPosition;
    private Grabbable customGrabbable;
    private bool  isCutting             = false;
    private float currentStrokeDistance = 0f;
    private float cutGraceTimer         = 0f;

    // ── Unity lifecycle ────────────────────────────────────────────────────
    private void Start()
    {
        customGrabbable = GetComponent<Grabbable>();
    }

    private void OnTriggerEnter(Collider other)
    {
        var zone = other.GetComponent<PlaneZone>();
        if (zone != null)
        {
            Debug.Log($"[HandPlaneTool] Entered PlaneZone: {zone.gameObject.name}");
            currentZone  = zone;
            lastPosition = transform.position;
        }
    }

    private void Update()
    {
        if (currentZone == null) return;

        Vector3 currentPos = transform.position;
        Vector3 moveDelta  = currentPos - lastPosition;
        float   distance   = moveDelta.magnitude;
        bool    validCut   = false;

        if (Time.deltaTime > 0f && distance > 0.0001f)
        {
            float speed = distance / Time.deltaTime;
            if (speed >= minCutSpeed)
            {
                Vector3 moveDir      = moveDelta.normalized;
                Vector3 localCutDir  = transform.TransformDirection(cuttingDirection);
                Vector3 zoneRequired = currentZone.transform.TransformDirection(currentZone.planeDirection);

                float angleToTool = Vector3.Angle(moveDir, localCutDir);
                float angleToZone = Vector3.Angle(moveDir, zoneRequired);

                if (angleToTool <= maxAngleTolerance && angleToZone <= maxAngleTolerance)
                {
                    validCut = true;
                    PlayFeedback();
                }
            }
        }

        if (validCut)
        {
            isCutting             = true;
            currentStrokeDistance += distance;
            cutGraceTimer         = 0.2f;
        }
        else if (isCutting)
        {
            cutGraceTimer -= Time.deltaTime;
            if (cutGraceTimer <= 0f)
            {
                if (currentZone.IsStrokeSuccessful(currentStrokeDistance))
                    currentZone.AddStrokeProgress();
                else
                    Debug.Log($"[HandPlaneTool] Stroke failed - distance too short: {currentStrokeDistance:F2}m");

                isCutting             = false;
                currentStrokeDistance = 0f;
                StopFeedback();
            }
        }

        lastPosition = currentPos;
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentZone == null || currentZone.gameObject != other.gameObject) return;

        if (isCutting)
        {
            if (currentZone.IsStrokeSuccessful(currentStrokeDistance))
                currentZone.AddStrokeProgress();
            else
                Debug.Log($"[HandPlaneTool] Stroke failed on exit - distance: {currentStrokeDistance:F2}m");

            isCutting             = false;
            currentStrokeDistance = 0f;
        }

        Debug.Log($"[HandPlaneTool] Exited PlaneZone: {currentZone.gameObject.name}");
        currentZone = null;
        StopFeedback();
    }

    // ── Feedback ───────────────────────────────────────────────────────────
    private void PlayFeedback()
    {
        if (woodShavingParticles != null && !woodShavingParticles.isPlaying)
            woodShavingParticles.Play();

        if (customGrabbable != null && customGrabbable.IsHeld)
            UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, hapticIntensity, hapticDuration);
    }

    private void StopFeedback()
    {
        if (woodShavingParticles != null && woodShavingParticles.isPlaying)
            woodShavingParticles.Stop();
    }
}
