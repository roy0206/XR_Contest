using UnityEngine;

public class SawTool : MonoBehaviour, IWoodTool
{
    public ToolType GetToolType() => ToolType.Saw;
    public bool IsActive() => true;

    [Header("Saw Settings")]
    [Tooltip("Minimum speed required to register a valid cut (m/s).")]
    public float minCutSpeed = 0.05f;
    [Tooltip("Local direction along which the saw cuts.")]
    public Vector3 cuttingDirection = Vector3.forward;
    [Tooltip("Maximum angle deviation between movement and cutting direction (degrees).")]
    public float maxAngleTolerance = 30f;

    [Header("Feedback")]
    public ParticleSystem sawdustParticles;
    [Range(0f, 1f)] public float hapticIntensity = 0.4f;
    public float hapticDuration = 0.05f;

    // ── Private state ──────────────────────────────────────────────────────
    private SawZone currentZone;
    private Vector3 lastPosition;
    private Grabbable customGrabbable;
    private bool isCutting = false;
    private float currentStrokeDistance = 0f;
    private float cutGraceTimer = 0f;
    
    // 평균 품질 계산용
    private float strokeSpeedAccum = 0f;
    private float strokeAngleAccum = 0f;
    private int frameCount = 0;

    private void Start()
    {
        customGrabbable = GetComponent<Grabbable>();
    }

    private void OnTriggerEnter(Collider other)
    {
        var zone = other.GetComponent<SawZone>();
        if (zone != null)
        {
            Debug.Log($"[SawTool] Entered SawZone: {zone.gameObject.name}");
            currentZone = zone;
            lastPosition = transform.position;
        }
    }

    private void Update()
    {
        if (currentZone == null) return;

        Vector3 currentPos = transform.position;
        Vector3 moveDelta = currentPos - lastPosition;
        float distance = moveDelta.magnitude;
        bool validCut = false;
        
        float currentSpeed = 0f;
        float minAngleToZone = 0f;

        if (Time.deltaTime > 0f && distance > 0.0001f)
        {
            currentSpeed = distance / Time.deltaTime;
            if (currentSpeed >= minCutSpeed)
            {
                Vector3 moveDir = moveDelta.normalized;
                Vector3 localCutDir = transform.TransformDirection(cuttingDirection);
                Vector3 zoneRequired = currentZone.transform.TransformDirection(currentZone.sawDirection);

                float angleToTool = Vector3.Angle(moveDir, localCutDir);
                
                // 톱질은 앞/뒤 양방향 스트로크 모두 인정 (180도 차이)
                float angleToToolOpposite = 180f - angleToTool;
                float minAngleToTool = Mathf.Min(angleToTool, angleToToolOpposite);

                float angleToZone = Vector3.Angle(moveDir, zoneRequired);
                float angleToZoneOpposite = 180f - angleToZone;
                minAngleToZone = Mathf.Min(angleToZone, angleToZoneOpposite);

                if (minAngleToTool <= maxAngleTolerance && minAngleToZone <= maxAngleTolerance)
                {
                    validCut = true;
                    PlayFeedback();
                }
            }
        }

        if (validCut)
        {
            isCutting = true;
            currentStrokeDistance += distance;
            cutGraceTimer = 0.2f;
            
            strokeSpeedAccum += currentSpeed;
            strokeAngleAccum += minAngleToZone;
            frameCount++;
        }
        else if (isCutting)
        {
            cutGraceTimer -= Time.deltaTime;
            if (cutGraceTimer <= 0f)
            {
                FinishStroke();
            }
            StopFeedback();
        }

        lastPosition = currentPos;
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentZone == null || currentZone.gameObject != other.gameObject) return;

        if (isCutting)
        {
            FinishStroke();
        }

        Debug.Log($"[SawTool] Exited SawZone: {currentZone.gameObject.name}");
        currentZone = null;
        StopFeedback();
    }

    private void FinishStroke()
    {
        if (currentZone != null)
        {
            if (currentZone.IsStrokeSuccessful(currentStrokeDistance))
            {
                float avgSpeed = frameCount > 0 ? strokeSpeedAccum / frameCount : 0f;
                float avgAngle = frameCount > 0 ? strokeAngleAccum / frameCount : 0f;
                currentZone.AddStrokeProgress(avgSpeed, avgAngle);
            }
            else
            {
                Debug.Log($"[SawTool] Stroke failed - distance too short: {currentStrokeDistance:F2}m");
            }
        }
        
        isCutting = false;
        currentStrokeDistance = 0f;
        strokeSpeedAccum = 0f;
        strokeAngleAccum = 0f;
        frameCount = 0;
    }

    // ── Feedback ───────────────────────────────────────────────────────────
    private void PlayFeedback()
    {
        if (sawdustParticles != null && !sawdustParticles.isPlaying)
            sawdustParticles.Play();

        if (customGrabbable != null && customGrabbable.IsHeld)
        {
            UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, hapticIntensity, hapticDuration);
        }
    }

    private void StopFeedback()
    {
        if (sawdustParticles != null && sawdustParticles.isPlaying)
            sawdustParticles.Stop();
    }
}
