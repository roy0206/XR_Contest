using UnityEngine;

public class ChiselTool : MonoBehaviour, IWoodTool
{
    public ToolType GetToolType() => ToolType.Chisel;
    public bool IsActive() => true;

    [Header("Chisel Settings")]
    [Tooltip("망치 타격 속도를 깊이로 변환하는 배율")]
    public float hitDepthMultiplier = 0.5f;

    [Header("시각적 가이드 (사영)")]
    [Tooltip("끌 끝단 앞쪽으로 쏠 Raycast 기준점")]
    public Transform raycastOrigin;
    [Tooltip("표면에 그려질 가이드 그래픽 부모")]
    public GameObject projectionVisual;
    [Tooltip("판정용 좌측 포인트")]
    public Transform checkPointLeft;
    [Tooltip("판정용 우측 포인트")]
    public Transform checkPointRight;
    
    [Tooltip("가이드가 나타나는 최대 거리 (m)")]
    public float maxProjectionDistance = 0.15f;

    [Header("피드백")]
    public AudioSource audioSource;
    public AudioClip validHitClip;
    public AudioClip invalidHitClip;
    public ParticleSystem woodChipsParticles;
    
    private Grabbable customGrabbable;
    private ChiselZone currentZone;

    private void Start()
    {
        customGrabbable = GetComponent<Grabbable>();
        if (projectionVisual != null) projectionVisual.SetActive(false);
    }

    private void Update()
    {
        UpdateProjectionVisual();
    }

    private void UpdateProjectionVisual()
    {
        if (raycastOrigin == null || projectionVisual == null) return;

        Ray ray = new Ray(raycastOrigin.position, raycastOrigin.forward);
        
        if (Physics.Raycast(ray, out RaycastHit hit, maxProjectionDistance))
        {
            ChiselZone zone = hit.collider.GetComponent<ChiselZone>();
            if (zone != null)
            {
                currentZone = zone;
                projectionVisual.SetActive(true);
                
                // 표면에 밀착
                projectionVisual.transform.position = hit.point + hit.normal * 0.001f;
                // 표면 노멀에 맞게 회전
                projectionVisual.transform.rotation = Quaternion.LookRotation(-hit.normal, raycastOrigin.up);
                return;
            }
        }
        
        projectionVisual.SetActive(false);
        currentZone = null;
    }

    public void OnHammerHit(float hitVelocity)
    {
        // 1. 유효 타격 여부 판정 (2-Point)
        if (currentZone != null && projectionVisual != null && projectionVisual.activeSelf)
        {
            BoxCollider zoneCol = currentZone.GetCollider();
            if (zoneCol != null && checkPointLeft != null && checkPointRight != null)
            {
                // BoxCollider의 Local Space로 변환해서 Bounds 체크
                Vector3 localLeft = zoneCol.transform.InverseTransformPoint(checkPointLeft.position);
                Vector3 localRight = zoneCol.transform.InverseTransformPoint(checkPointRight.position);
                
                Vector3 halfSize = zoneCol.size * 0.5f;
                Vector3 center = zoneCol.center;
                
                bool isLeftInside = IsPointInLocalBounds(localLeft, center, halfSize);
                bool isRightInside = IsPointInLocalBounds(localRight, center, halfSize);

                if (isLeftInside && isRightInside)
                {
                    // 유효 타격
                    float depth = hitVelocity * hitDepthMultiplier;
                    currentZone.AddHitProgress(depth);
                    PlayHitFeedback(true);
                    return;
                }
            }
        }

        // 무효 타격 (존 밖이거나 허공)
        PlayHitFeedback(false);
    }

    private bool IsPointInLocalBounds(Vector3 localPoint, Vector3 center, Vector3 halfSize)
    {
        return Mathf.Abs(localPoint.x - center.x) <= halfSize.x &&
               Mathf.Abs(localPoint.y - center.y) <= halfSize.y &&
               Mathf.Abs(localPoint.z - center.z) <= halfSize.z;
    }

    private void PlayHitFeedback(bool isValid)
    {
        if (audioSource != null)
        {
            AudioClip clipToPlay = isValid ? validHitClip : invalidHitClip;
            if (clipToPlay != null)
            {
                audioSource.PlayOneShot(clipToPlay);
            }
        }
        
        if (isValid && woodChipsParticles != null)
        {
            woodChipsParticles.Play();
        }

        if (customGrabbable != null && customGrabbable.IsHeld)
        {
            UserInput.Instance.SendHapticImpulse(customGrabbable.Holder.Hand, isValid ? 0.8f : 0.2f, 0.1f);
        }
    }
}
