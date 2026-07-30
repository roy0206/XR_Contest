using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class HandPlaneTool : MonoBehaviour, IWoodTool
{
    public ToolType GetToolType() => ToolType.HandPlane;
    public bool IsActive() => true;

    [Header("대패질 판정 설정")]
    [Tooltip("이동 속도가 이 값 이상일 때만 대패질로 인정 (m/s)")]
    public float minCutSpeed = 0.1f;
    [Tooltip("대패가 나무를 깎는 정방향(로컬 축)")]
    public Vector3 cuttingDirection = Vector3.forward;
    [Tooltip("나무 방향과 대패 방향의 최대 허용 오차(각도)")]
    public float maxAngleTolerance = 30f;

    [Header("피드백 요소")]
    public ParticleSystem woodShavingParticles;
    [Range(0f, 1f)] public float hapticIntensity = 0.3f;
    public float hapticDuration = 0.05f;

    private PlaneZone currentZone;
    private Vector3 lastPosition;
    private XRGrabInteractable interactable;
    private bool isCutting = false;
    private float currentStrokeDistance = 0f;

    private void Start()
    {
        interactable = GetComponent<XRGrabInteractable>();
    }

    private void OnTriggerEnter(Collider other)
    {
        PlaneZone zone = other.GetComponent<PlaneZone>();
        if (zone != null)
        {
            Debug.Log($"[HandPlaneTool] 🎯 PlaneZone 진입 감지! (Zone: {zone.gameObject.name})");
            currentZone = zone;
            lastPosition = transform.position;
        }
    }

    private void Update()
    {
        if (currentZone != null)
        {
            Vector3 currentPos = transform.position;
            Vector3 moveDelta = currentPos - lastPosition;
            float distance = moveDelta.magnitude;
            
            bool validCutThisFrame = false;

            // 너무 작은 프레임간 이동이나 deltaTime 0 방지
            if (Time.deltaTime > 0f && distance > 0.0001f)
            {
                float speed = distance / Time.deltaTime;

                if (speed >= minCutSpeed)
                {
                    // 이동 방향이 대패의 정방향과 일치하는지 판정
                    Vector3 moveDir = moveDelta.normalized;
                    Vector3 localCutDir = transform.TransformDirection(cuttingDirection);
                    Vector3 zoneRequiredDir = currentZone.transform.TransformDirection(currentZone.planeDirection);

                    float angleToTool = Vector3.Angle(moveDir, localCutDir);
                    float angleToZone = Vector3.Angle(moveDir, zoneRequiredDir);

                    if (angleToTool <= maxAngleTolerance && angleToZone <= maxAngleTolerance)
                    {
                        // 유효한 대패질
                        validCutThisFrame = true;
                        PlayFeedback();
                    }
                    else
                    {
                        // 각도가 틀어졌을 때 뜨는 로그 (프레임마다 떠서 스팸이 될 수 있으므로 주석 처리)
                        // Debug.Log($"[HandPlaneTool] 각도 안맞음! Tool각도: {angleToTool:F1}, Zone각도: {angleToZone:F1}");
                    }
                }
                else
                {
                    // 속도가 너무 느릴 때는 로그가 너무 많이 뜨므로 생략하거나 필요시 주석 해제
                    // Debug.Log($"[HandPlaneTool] 속도 부족! 현재속도: {speed:F2}");
                }
            }

            // 이번 프레임에 깎지 않았다면 (멈췄거나 각도가 틀어졌다면)
            if (validCutThisFrame)
            {
                isCutting = true;
                currentStrokeDistance += distance; // 스트로크 거리 누적
            }
            else
            {
                if (isCutting)
                {
                    // 깎다가 멈췄으므로 한 번의 스트로크가 끝남!
                    if (currentZone.IsStrokeSuccessful(currentStrokeDistance))
                    {
                        currentZone.AddStrokeProgress();
                    }
                    else
                    {
                        // 너무 짧게 끊어 친 경우 무시
                        Debug.Log($"[HandPlaneTool] 스트로크 실패 (이동 거리 부족: {currentStrokeDistance:F2}m)");
                    }
                    
                    isCutting = false;
                    currentStrokeDistance = 0f;
                }
                StopFeedback();
            }

            // 매 프레임 위치 업데이트
            lastPosition = currentPos;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentZone != null && currentZone.gameObject == other.gameObject)
        {
            if (isCutting)
            {
                // 존을 빠져나가면서 스트로크가 끝난 경우
                if (currentZone.IsStrokeSuccessful(currentStrokeDistance))
                {
                    currentZone.AddStrokeProgress();
                }
                else
                {
                    Debug.Log($"[HandPlaneTool] 스트로크 실패 (이동 거리 부족: {currentStrokeDistance:F2}m)");
                }
                
                isCutting = false;
                currentStrokeDistance = 0f;
            }
            Debug.Log($"[HandPlaneTool] 💨 PlaneZone 이탈! (Zone: {currentZone.gameObject.name})");
            currentZone = null;
            StopFeedback();
        }
    }

    private void PlayFeedback()
    {
        if (woodShavingParticles != null && !woodShavingParticles.isPlaying)
        {
            woodShavingParticles.Play();
        }

        if (interactable != null && interactable.isSelected)
        {
            var interactor = interactable.firstInteractorSelecting as XRBaseInputInteractor;
            if (interactor != null)
            {
                interactor.SendHapticImpulse(hapticIntensity, hapticDuration);
            }
        }
    }

    private void StopFeedback()
    {
        if (woodShavingParticles != null && woodShavingParticles.isPlaying)
        {
            woodShavingParticles.Stop();
        }
    }
}
