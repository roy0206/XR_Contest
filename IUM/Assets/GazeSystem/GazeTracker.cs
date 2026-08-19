using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 대상(IGazeTarget)을 향해 지정된 Transform(머리 뼈)을 부드럽게 회전시키는 엔진 컴포넌트입니다.
    /// 조화 진동(Harmonic Oscillation) 및 감쇠 필터링 연산을 거쳐 C# 스크립트만으로
    /// 자연스러운 고개 끄덕임(Nod)과 절레절레(Shake) 모션을 물리 시뮬레이션합니다.
    /// </summary>
    [AddComponentMenu("Gaze System/Gaze Tracker")]
    public class GazeTracker : MonoBehaviour
    {
        [Header("Gaze Settings")]
        [SerializeField]
        [Tooltip("회전시킬 머리 또는 눈의 Transform입니다.")]
        private Transform headTransform;

        [SerializeField]
        [Tooltip("시선이 따라가는 속도입니다.")]
        private float gazeSpeed = 5f;

        [SerializeField]
        [Tooltip("정면 방향 대비 최대로 회전할 수 있는 각도(도)입니다.")]
        [Range(0f, 180f)]
        private float maxRotationAngle = 75f;

        [SerializeField]
        [Tooltip("시선 대상을 잃었을 때 정면을 돌아보는 속도입니다.")]
        private float returnSpeed = 3f;

        [Header("Bone Axis Calibration")]
        [SerializeField]
        [Tooltip("3D 캐릭터 모델의 뼈대 축 정렬이 어긋나 엉뚱한 곳을 볼 때 보정해주는 로컬 회전 오프셋 각도(Euler)입니다.")]
        private Vector3 headRotationOffset = Vector3.zero;

        [Header("Debug")]
        [SerializeField]
        private bool showDebugGizmos = true;

        private IGazeTarget currentTarget;
        private Vector3 targetGazePosition;
        private Vector3 currentGazePoint;
        private Quaternion initialLocalRotation;
        private bool hasInitialRotation = false;

        // 현재 바라보고 있는 상태 비율 (0: 정면, 1: 타겟 완전히 주시)
        private float gazeWeight = 0f;

        // 끄덕임(Nod) 물리 연산 변수
        private float nodTimeRemaining;
        private float nodTotalDuration;
        private float nodSpeed;
        private float nodIntensity;
        private bool nodActive;

        // 절레절레(Shake) 물리 연산 변수
        private float shakeTimeRemaining;
        private float shakeTotalDuration;
        private float shakeSpeed;
        private float shakeIntensity;
        private bool shakeActive;

        private void Start()
        {
            if (headTransform == null)
            {
                headTransform = transform;
                Debug.LogWarning($"[{gameObject.name}] Head Transform이 지정되지 않아 자신({gameObject.name})을 머리로 설정합니다.");
            }

            initialLocalRotation = headTransform.localRotation;
            hasInitialRotation = true;

            currentGazePoint = headTransform.position + headTransform.forward * 3f;
            targetGazePosition = currentGazePoint;
        }

        public void SetTarget(IGazeTarget target)
        {
            currentTarget = target;
        }

        public void ClearTarget()
        {
            currentTarget = null;
        }

        #region Motion Trigger API

        /// <summary>
        /// 조화 진동 및 감쇠 기법을 적용한 고개 끄덕임(Yes) 모션을 트리거합니다.
        /// </summary>
        /// <param name="duration">모션이 지속될 시간(초)입니다.</param>
        /// <param name="speed">흔들림의 진동 주파수 속도입니다.</param>
        /// <param name="intensity">위아래 끄덕임 각도의 최대 진폭 크기입니다.</param>
        public void TriggerNod(float duration = 1.5f, float speed = 13f, float intensity = 16f)
        {
            nodSpeed = speed;
            nodIntensity = intensity;
            nodTotalDuration = duration;
            nodTimeRemaining = duration;
            nodActive = true;
        }

        /// <summary>
        /// 고개 끄덕임 모션을 즉시 중단합니다.
        /// </summary>
        public void StopNod()
        {
            nodActive = false;
        }

        /// <summary>
        /// 조화 진동 및 감쇠 기법을 적용한 고개 절레절레(No) 모션을 트리거합니다.
        /// </summary>
        /// <param name="duration">모션이 지속될 시간(초)입니다.</param>
        /// <param name="speed">흔들림의 진동 주파수 속도입니다.</param>
        /// <param name="intensity">좌우 흔들림 각도의 최대 진폭 크기입니다.</param>
        public void TriggerShake(float duration = 1.5f, float speed = 12f, float intensity = 14f)
        {
            shakeSpeed = speed;
            shakeIntensity = intensity;
            shakeTotalDuration = duration;
            shakeTimeRemaining = duration;
            shakeActive = true;
        }

        /// <summary>
        /// 고개 절레절레 모션을 즉시 중단합니다.
        /// </summary>
        public void StopShake()
        {
            shakeActive = false;
        }

        /// <summary>
        /// 모든 물리 모션(끄덕임, 절레절레)을 즉시 종료합니다.
        /// </summary>
        public void StopAllMotions()
        {
            StopNod();
            StopShake();
        }

        #endregion

        private void Update()
        {
            // 1. 모션 타이머 실시간 차감
            if (nodActive)
            {
                nodTimeRemaining -= Time.deltaTime;
                if (nodTimeRemaining <= 0f)
                {
                    nodActive = false;
                }
            }

            if (shakeActive)
            {
                shakeTimeRemaining -= Time.deltaTime;
                if (shakeTimeRemaining <= 0f)
                {
                    shakeActive = false;
                }
            }
        }

        private void LateUpdate()
        {
            if (!hasInitialRotation || headTransform == null) return;

            UpdateGazePoint();
            ApplyGazeRotation();
        }

        private void UpdateGazePoint()
        {
            if (currentTarget != null && currentTarget.TargetTransform != null)
            {
                targetGazePosition = currentTarget.TargetTransform.position;
                gazeWeight = Mathf.MoveTowards(gazeWeight, 1f, Time.deltaTime * gazeSpeed);
            }
            else
            {
                targetGazePosition = headTransform.position + transform.forward * 3f;
                gazeWeight = Mathf.MoveTowards(gazeWeight, 0f, Time.deltaTime * returnSpeed);
            }

            currentGazePoint = Vector3.Lerp(currentGazePoint, targetGazePosition, Time.deltaTime * gazeSpeed);
        }

        private void ApplyGazeRotation()
        {
            Vector3 directionToGaze = currentGazePoint - headTransform.position;
            if (directionToGaze.sqrMagnitude < 0.001f) return;

            // LateUpdate() 시점의 애니메이터 고유 로컬 회전 백업
            Quaternion animatedRotation = headTransform.localRotation;

            // 1. 기본 월드 응시 회전 및 정렬 오프셋 적용
            Quaternion targetWorldRotation = Quaternion.LookRotation(directionToGaze, transform.up);
            targetWorldRotation = targetWorldRotation * Quaternion.Euler(headRotationOffset);

            // 2. 부모 대비 로컬 회전으로 변환
            Quaternion targetLocalRotation = Quaternion.Inverse(headTransform.parent != null ? headTransform.parent.rotation : Quaternion.identity) * targetWorldRotation;

            // 3. [핵심 수정]: 고정된 initialLocalRotation 대신, 매 프레임 애니메이션에 의해 숙여지거나 뒤틀린 animatedRotation을 기준축으로 회전 한계를 계산합니다!
            // 이렇게 해야 웅크리거나 비트는 애니메이션이 돌 때 목이 기괴하게 뒤로 꺾이지 않고, 애니메이션 자세 기준선 안에서 안전하게 눈만 굴립니다.
            float angleDiff = Quaternion.Angle(animatedRotation, targetLocalRotation);
            if (angleDiff > maxRotationAngle)
            {
                float t = maxRotationAngle / angleDiff;
                targetLocalRotation = Quaternion.Slerp(animatedRotation, targetLocalRotation, t);
            }

            // 4. 모션 오프셋 계산 (조화 진동 및 감쇠 페이드아웃 적용)
            Quaternion motionOffset = Quaternion.identity;

            if (nodActive && nodTotalDuration > 0f)
            {
                float elapsedTime = nodTotalDuration - nodTimeRemaining;
                // 남은 시간 비율이 전체의 20% 미만으로 접어들면 서서히 감쇠(fade -> 0)
                float fade = Mathf.Clamp01(nodTimeRemaining / (nodTotalDuration * 0.2f)); 
                // 조화 진동(Mathf.Sin) 기반 회전각 산출
                float angleX = Mathf.Sin(elapsedTime * nodSpeed) * nodIntensity * fade;
                motionOffset *= Quaternion.Euler(angleX, 0f, 0f);
            }

            if (shakeActive && shakeTotalDuration > 0f)
            {
                float elapsedTime = shakeTotalDuration - shakeTimeRemaining;
                // 남은 시간 비율이 전체의 20% 미만으로 접어들면 서서히 감쇠(fade -> 0)
                float fade = Mathf.Clamp01(shakeTimeRemaining / (shakeTotalDuration * 0.2f));
                // 조화 진동(Mathf.Sin) 기반 회전각 산출
                float angleY = Mathf.Sin(elapsedTime * shakeSpeed) * shakeIntensity * fade;
                motionOffset *= Quaternion.Euler(0f, angleY, 0f);
            }

            // 5. Pivot Rotation Multiplication
            // 응시하고 있는 회전축(targetLocalRotation)에 흔들림 오프셋(motionOffset)을 후행 누적시켜
            // 상대방을 똑바로 바라본 상태를 축으로 고개가 진동하도록 구현
            targetLocalRotation = targetLocalRotation * motionOffset;

            // 6. 원래 회전과 타겟 회전을 시선 가중에 맞춰 최종 보간
            headTransform.localRotation = Quaternion.Slerp(animatedRotation, targetLocalRotation, gazeWeight);
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || headTransform == null) return;

            Matrix4x4 prevMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(headTransform.position, currentGazePoint);
            Gizmos.DrawWireSphere(currentGazePoint, 0.15f);

            if (currentTarget != null && currentTarget.TargetTransform != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(currentTarget.TargetTransform.position, 0.1f);
            }

            Gizmos.matrix = prevMatrix;
        }
    }
}
