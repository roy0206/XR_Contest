using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GazeSystem
{
    /// <summary>
    /// 이음(Eeum) 및 노장(legendOldman) 등 NPC 캐릭터들의 행동, 시선, 이동 리액션을 제어하는 통합 라이브러리 컴포넌트입니다.
    /// [통합 NPC 라이브러리 최종 완결판]:
    /// 1. [VR 연동 & PlayerMovement 의존성 해제]: PlayerMovement 스크립트가 없는 타 프로젝트나 VR 환경에서도, Player 태그 오브젝트 및 Main Camera(VR HMD)를 자동 탐색하여 100% 자립 구동됩니다.
    /// 2. [VR 텔레포트 Failsafe]: VR 순간이동이나 갑작스러운 공간 점프 시 펫이 미친 듯이 초고속 비행을 뿜지 않도록, 12m 이상 거리가 어긋나면 즉시 타겟 스팟으로 부드럽게 순간이동 동기화합니다.
    /// 3. [lumi 컨트롤러 Bool Greet 호환]: greet 파라미터가 Bool 타입일 경우 리액션 종료 시 확실하게 false로 꺾어 인사가 무한 재생되는 버그를 방지합니다.
    /// 4. [6m 절대거리 밀어내기]: 플레이어가 이음이 쪽으로 다가오면 이음이가 그 방향 그대로 뒤/옆으로 밀려나며 6m 절대 거리를 철저하게 사수(Keep)합니다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Gaze System/NPC/NPC Animation Trigger")]
    public class EeumAnimationTrigger : MonoBehaviour
    {
        private const float modelRotationOffset = 0.0f; // 정방향 0도 고정

        [Header("NPC Type Mode (이음이 / 노장 구분)")]
        [SerializeField]
        [Tooltip("비행형 NPC(이음이 등)인 경우 체크합니다. 체크 해제 시 지상 NPC(노장 등)로 작동하여 공중 부유나 비행 이동 없이 제자리 시선 처리와 애니메이터 리액션만 작동합니다.")]
        private bool isFlyingNPC = true;

        [Header("Eeum Follow Settings (비행 추종 설정 - Flying 전용)")]
        [SerializeField]
        [Tooltip("플레이어 이동 방향 기준 왼쪽 오프셋 거리(m)입니다. 철저한 절대거리 간격으로 유지됩니다.")]
        private float followDistance = 6.0f;

        [SerializeField]
        [Tooltip("플레이어 위치 기준 이음이가 떠 있을 공중 높이(m)입니다.")]
        private float hoverHeight = 0.9f;

        [SerializeField]
        [Tooltip("이동 속도(m/s)입니다. 가까이 있을 때의 기준 추종 속도입니다.")]
        private float moveSpeed = 4.0f;

        [SerializeField]
        [Tooltip("회전 시 방향 전환 속도입니다.")]
        private float rotationSpeed = 8f;

        [SerializeField]
        [Tooltip("플레이어를 천천히 부드럽게 쫓아올 때의 기준 감쇠 속도율(값이 클수록 천천히 따라옴)입니다.")]
        private float followSmoothTime = 0.65f;

        [Header("Eeum Anchor Soft Fix (가구 안착 앵커 - 선택사항)")]
        [SerializeField]
        [Tooltip("설정 시, 플레이어가 anchorTriggerRange 내에 있을 때 플레이어 추종 대신 해당 앵커 가구 자리에 고정됩니다.")]
        private Transform homeAnchor;

        [SerializeField]
        [Tooltip("홈 앵커 소프트 픽스가 활성화되는 반경 범위(m)입니다.")]
        private float anchorTriggerRange = 5.0f;

        [Header("Developer Test Mode")]
        [SerializeField]
        [Tooltip("체크 시 NPC가 지정된 자리에 고정된 상태에서 모든 애니메이션/리액션을 순차적 반복 테스트합니다.")]
        private bool testAllAnimations = false;

        [Header("Eeum Flying Settings (공중 부유 설정 - Flying 전용)")]
        [SerializeField]
        [Tooltip("공중에 떠 있을 때 위아래로 둥실둥실 흔들리는 속도입니다.")]
        private float bobbingSpeed = 2.0f;

        [SerializeField]
        [Tooltip("공중에 떠 있을 때 위아래로 흔들리는 진폭(m)입니다.")]
        private float bobbingAmount = 0.12f;

        [Header("Eeum Procedural Inertia Bone Settings (관성 뼈대 제어 - Flying 전용)")]
        [SerializeField]
        [Tooltip("목뼈(Neck) 트랜스폼입니다. 지정되지 않으면 이름으로 자동 탐색합니다.")]
        private Transform neckBone;

        [SerializeField]
        [Tooltip("목뼈가 감속/가속에 반응해 쏠리는 민감도입니다.")]
        private float neckTiltSensitivity = 2.5f;

        [SerializeField]
        [Tooltip("목뼈가 최대로 꺾일 수 있는 한계각(도)입니다.")]
        private float maxNeckAngle = 15.0f;

        [SerializeField]
        [Tooltip("관성 뼈대 회전이 원래대로 돌아오거나 기울 때의 보간 속도(Lerp Speed)입니다.")]
        private float boneLerpSpeed = 5.0f;

        [Header("Force Path Settings")]
        [SerializeField]
        [Tooltip("도착 시 실행할 임시 강제 웨이포인트 액션 목록입니다. 비어 있으면 앵커 주변에 상주합니다.")]
        private List<WaypointAction> patrolRoutes = new List<WaypointAction>();

        private Animator animator;
        private GazeTracker gazeTracker;
        private NPCGazeController gazeController;
        private Coroutine sequenceCoroutine;
        private Coroutine gazeLockTimerCoroutine;

        // 강제 이동/작업 액션 큐
        private Queue<WaypointAction> routeQueue = new Queue<WaypointAction>();

        // 내부 행동 큐
        private Queue<ActionTask> actionQueue = new Queue<ActionTask>();

        // 원래 스폰 상태 기억용 변수
        private Vector3 startPosition;
        private Quaternion startRotation;

        private bool isPerformingAction = false;
        private ActionType currentRunningTaskType = ActionType.Crouch; 

        private bool isGazeLocked = false;
        private bool lastGazeLockState = false;

        private bool watch = false;
        private bool lastWatchState = false;

        private Transform cachedObjectTarget;
        private Transform currentActiveLookTarget;

        private bool isAtHomeBase = true;
        private float bobbingTime = 0f;
        private bool isMoving = false;

        private Vector3 currentMoveTargetPos;
        private Vector3 moveVelocity; 

        private bool isDoingHappyBackflip = false;

        private Vector3 lastPosition;
        private Vector3 currentVelocity;
        private Vector3 lastVelocity;
        private Vector3 currentAcceleration;

        private Quaternion currentNeckOffset = Quaternion.identity;

        // WASD 마지막 실제 이동 진행 방향 벡터 및 순차 리액션 감시 인덱스
        private Vector3 lastPlayerMoveDirection = Vector3.forward;
        private Vector3 lastPlayerPos;
        private Vector3 playerVelocity;
        private int currentIdleReactionIndex = 0;

        #region ================= [1. 물리적 시선 락 제어 API (Gaze Lock)] =================

        public void SetGazeLock(bool enabled)
        {
            isGazeLocked = enabled;
        }

        public bool GetGazeLock()
        {
            return isGazeLocked;
        }

        #endregion

        #region ================= [2. 애니메이터 watch 파라미터 제어 API (Watch Anim)] =================

        public void SetWatchAnim(bool enabled)
        {
            watch = enabled;
            if (animator != null && HasParameter(animator, "watch"))
            {
                animator.SetBool("watch", enabled);
            }
        }

        public bool GetWatchAnim()
        {
            return watch;
        }

        #endregion

        private void Start()
        {
            animator = GetComponent<Animator>();

            gazeTracker = GetComponent<GazeTracker>();
            if (gazeTracker == null) gazeTracker = gameObject.AddComponent<GazeTracker>();

            gazeController = GetComponent<NPCGazeController>();
            if (gazeController == null) gazeController = gameObject.AddComponent<NPCGazeController>();

            if (GetComponent<NPCGazeDirector>() == null) gameObject.AddComponent<NPCGazeDirector>();
            if (GetComponent<SequenceGazeSelectionPolicy>() == null) gameObject.AddComponent<SequenceGazeSelectionPolicy>();

            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            FindBones();

            if (gazeTracker != null)
            {
                var headTransField = typeof(GazeTracker).GetField("headTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (headTransField != null)
                {
                    Transform headBone = headTransField.GetValue(gazeTracker) as Transform;
                    if (headBone == null)
                    {
                        Transform foundHead = FindHeadBoneFallback(transform);
                        headBone = foundHead != null ? foundHead : transform;
                        headTransField.SetValue(gazeTracker, headBone);
                    }

                    if (neckBone == headBone && neckBone != null)
                    {
                        if (neckBone.parent != null && (neckBone.parent.name.ToLower().Contains("neck") || neckBone.parent.name.ToLower().Contains("back") || neckBone.parent.name.ToLower().Contains("spine")))
                        {
                            neckBone = neckBone.parent;
                        }
                    }
                }

                var offsetField = typeof(GazeTracker).GetField("headRotationOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (offsetField != null)
                {
                    Vector3 currentOffset = (Vector3)offsetField.GetValue(gazeTracker);
                    currentOffset.y = modelRotationOffset; 
                    offsetField.SetValue(gazeTracker, currentOffset);
                }
            }

            startPosition = transform.position;
            startRotation = transform.rotation;
            lastPosition = transform.position;
            currentMoveTargetPos = transform.position;

            Transform playerTrans = FindPlayerTransform();
            if (playerTrans != null)
            {
                lastPlayerPos = playerTrans.position;
                lastPlayerMoveDirection = playerTrans.forward;
                lastPlayerMoveDirection.y = 0;
                lastPlayerMoveDirection.Normalize();
            }
            else
            {
                lastPlayerPos = transform.position;
            }

            if (patrolRoutes != null && patrolRoutes.Count > 0)
            {
                foreach (var route in patrolRoutes)
                {
                    if (route != null && route.waypoint != null)
                    {
                        routeQueue.Enqueue(route);
                    }
                }
            }

            FindDefaultObjectTarget();

            sequenceCoroutine = StartCoroutine(CoDialogueSequenceLoop());

            lastGazeLockState = isGazeLocked;
            lastWatchState = watch;
        }

        private void Update()
        {
            Transform playerTrans = FindPlayerTransform();
            if (playerTrans != null && Time.deltaTime > 0f)
            {
                playerVelocity = (playerTrans.position - lastPlayerPos) / Time.deltaTime;
                lastPlayerPos = playerTrans.position;

                // [WASD 실제 이동 방향 캐싱]
                Vector3 horizontalVelocity = playerVelocity;
                horizontalVelocity.y = 0;
                if (horizontalVelocity.magnitude > 0.15f)
                {
                    lastPlayerMoveDirection = horizontalVelocity.normalized;
                }
            }

            if (Time.deltaTime > 0f)
            {
                currentVelocity = (transform.position - lastPosition) / Time.deltaTime;
                currentAcceleration = (currentVelocity - lastVelocity) / Time.deltaTime;
                
                lastPosition = transform.position;
                lastVelocity = currentVelocity;
            }

            if (animator != null && HasParameter(animator, "watch"))
            {
                bool animWatch = animator.GetBool("watch");
                if (animWatch != watch)
                {
                    watch = animWatch;
                }
            }

            // [백덤블링 전용 시선 비활성화 제어]
            if (isDoingHappyBackflip)
            {
                if (gazeTracker != null)
                {
                    gazeTracker.ClearTarget(); 
                }
            }
            else
            {
                bool isPerformingWorkGaze = isPerformingAction && 
                                            currentRunningTaskType != ActionType.FaceGaze && 
                                            currentRunningTaskType != ActionType.AutomaticGaze;

                bool shouldGazeAtTarget = isGazeLocked || watch || (!isAtHomeBase && isPerformingWorkGaze);

                if (shouldGazeAtTarget)
                {
                    Transform targetTrans = currentActiveLookTarget != null ? currentActiveLookTarget : cachedObjectTarget;
                    IGazeTarget target = GetGazeTargetFromTransform(targetTrans);
                    if (target == null)
                    {
                        target = FindPlayerGazeTarget(); 
                    }

                    if (target != null && gazeTracker != null)
                    {
                        gazeTracker.SetTarget(target);
                    }
                }
                else
                {
                    if (lastGazeLockState || lastWatchState || 
                        (!isPerformingAction && isAtHomeBase == false && 
                         currentRunningTaskType != ActionType.FaceGaze && 
                         currentRunningTaskType != ActionType.AutomaticGaze))
                    {
                        RestoreSequenceGazeTarget();
                    }
                }
            }

            // [NPC 위치 제어 스팟]
            if (routeQueue.Count == 0 && !isPerformingAction)
            {
                // A. 비행형 NPC인 경우 -> 플레이어 WASD 6m 절대거리 추종 작동
                if (isFlyingNPC)
                {
                    Vector3 targetFlightPos = startPosition;

                    if (testAllAnimations && playerTrans != null)
                    {
                        isAtHomeBase = true;
                        Vector3 leftDirection = Quaternion.Euler(0, -90f, 0) * lastPlayerMoveDirection;
                        targetFlightPos = playerTrans.position + leftDirection * followDistance + Vector3.up * hoverHeight;
                    }
                    else if (homeAnchor != null && playerTrans != null && Vector3.Distance(playerTrans.position, homeAnchor.position) <= anchorTriggerRange)
                    {
                        isAtHomeBase = true;
                        targetFlightPos = homeAnchor.position;
                    }
                    else if (playerTrans != null)
                    {
                        Vector3 leftDirection = Quaternion.Euler(0, -90f, 0) * lastPlayerMoveDirection;
                        Vector3 defaultTargetPos = playerTrans.position + leftDirection * followDistance + Vector3.up * hoverHeight;

                        Vector3 toEeum2D = transform.position - playerTrans.position;
                        toEeum2D.y = 0;
                        float rawDist = toEeum2D.magnitude;

                        // [절대거리 사수]: 플레이어가 다가와서 6m보다 좁아지거나 거리가 비틀어지면 밀려남
                        if (rawDist > 0.05f)
                        {
                            toEeum2D.Normalize();
                            targetFlightPos = playerTrans.position + toEeum2D * followDistance + Vector3.up * hoverHeight;
                        }
                        else
                        {
                            targetFlightPos = defaultTargetPos;
                        }

                        // 플레이어가 진짜 WASD 이동 중일 때는 기본 타겟(왼쪽 6m 스팟)으로 자연스럽게 블렌딩 수렴 유도
                        Vector3 horizontalVelocity = playerVelocity;
                        horizontalVelocity.y = 0;
                        if (horizontalVelocity.magnitude > 0.15f)
                        {
                            targetFlightPos = Vector3.Lerp(targetFlightPos, defaultTargetPos, Time.deltaTime * 5.0f);
                        }

                        // [VR 텔레포트 Failsafe]:
                        // 텔레포트나 갑작스러운 순간 좌표 변경으로 플레이어와 거리가 급격히 벌어진 경우(12m 이상),
                        // 가변 가속도에 의해 펫이 화면 밖으로 총알같이 날아가는 연출을 스킵하고 사뿐히 6m 지점으로 동기화 이동시킵니다.
                        float distToPlayer = Vector3.Distance(transform.position, playerTrans.position);
                        if (distToPlayer > followDistance * 2.2f)
                        {
                            transform.position = targetFlightPos;
                            currentMoveTargetPos = targetFlightPos;
                            moveVelocity = Vector3.zero;
                        }

                        // 정지 및 안착 완료 여부 감지 (6m 절대 거리 기준)
                        float distToTargetSpace = Vector3.Distance(transform.position, targetFlightPos);
                        if (distToTargetSpace <= 0.35f && playerVelocity.magnitude <= 0.15f)
                        {
                            isAtHomeBase = true;
                        }
                        else
                        {
                            isAtHomeBase = false;
                        }
                    }

                    float distToTarget = Vector3.Distance(transform.position, targetFlightPos);
                    if (distToTarget > 0.05f)
                    {
                        isMoving = true;
                    }
                    else
                    {
                        isMoving = false;
                    }

                    if (!isMoving && !isDoingHappyBackflip)
                    {
                        bobbingTime += Time.deltaTime * bobbingSpeed;
                        targetFlightPos.y += Mathf.Sin(bobbingTime) * bobbingAmount;
                    }
                    else
                    {
                        bobbingTime = 0f;
                    }

                    currentMoveTargetPos = targetFlightPos;

                    float activeSpeed = moveSpeed * (1.0f + distToTarget * 0.45f);
                    float activeSmoothTime = Mathf.Clamp(followSmoothTime / (1.0f + distToTarget * 0.3f), 0.12f, followSmoothTime);

                    transform.position = Vector3.SmoothDamp(transform.position, currentMoveTargetPos, ref moveVelocity, activeSmoothTime, activeSpeed);
                }
                // B. 지상형 NPC(노장 등)인 경우 -> 날지 않고 고정되어 정지 대기 리액션만 무조건 작동
                else
                {
                    isMoving = false;
                    isAtHomeBase = true;
                }

                // [정방향 시선 정렬]: 앵커 자리에 머물며 항상 플레이어를 자연스럽게 지긋이 응시
                if (!isDoingHappyBackflip && playerTrans != null)
                {
                    Vector3 toPlayer = playerTrans.position - transform.position;
                    toPlayer.y = 0;
                    if (toPlayer.magnitude > 0.05f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(toPlayer) * Quaternion.Euler(0, modelRotationOffset, 0);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
                    }
                }
            }

            lastGazeLockState = isGazeLocked;
            lastWatchState = watch;
        }

        private void LateUpdate()
        {
            // [목 관성 연산] (비행형 NPC인 경우에만 가동)
            if (isFlyingNPC && neckBone != null && Time.deltaTime > 0f)
            {
                Vector3 localAccel = transform.InverseTransformDirection(currentAcceleration);

                float targetNeckX = localAccel.z * neckTiltSensitivity;
                targetNeckX = Mathf.Clamp(targetNeckX, -maxNeckAngle, maxNeckAngle);

                Quaternion targetNeckRot = Quaternion.Euler(targetNeckX, 0f, 0f);
                currentNeckOffset = Quaternion.Slerp(currentNeckOffset, targetNeckRot, Time.deltaTime * boneLerpSpeed);

                neckBone.localRotation = neckBone.localRotation * currentNeckOffset;
            }
        }

        #region ================= [인스펙터 이벤트 바인딩 및 외부 C# 스크립트 호출용 퍼블릭 API] =================

        public void PlayGazeLock(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.GazeLock, duration = duration });
        }

        public void PlayGazeLock()
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.GazeLock, duration = -1f });
        }

        private IEnumerator CoPlayGazeLockInternal(float duration)
        {
            float targetDuration = duration;
            if (targetDuration < 0f)
            {
                targetDuration = 3.0f; 
                if (actionQueue.Count > 0)
                {
                    ActionTask nextTask = actionQueue.Peek();
                    if (nextTask != null && nextTask.duration > 0f)
                    {
                        targetDuration = nextTask.duration;
                    }
                }
            }

            SetGazeLock(true);
            yield return new WaitForSeconds(targetDuration);
            SetGazeLock(false);

            if (gazeController != null && isAtHomeBase)
            {
                gazeController.ResumeAutomaticGaze();
            }
            else if (gazeController != null && !isAtHomeBase)
            {
                gazeController.DisableGaze(); 
            }
        }

        public void PlayWatchAnim(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.WatchAnim, duration = duration });
        }

        private IEnumerator CoPlayWatchAnimInternal(float duration)
        {
            SetWatchAnim(true);
            yield return new WaitForSeconds(duration);
            SetWatchAnim(false);
            yield return new WaitForSeconds(2.0f);
        }

        public void PlayNodReaction(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.Nod, duration = duration });
        }

        private IEnumerator CoPlayNodReactionInternal(float duration)
        {
            isPerformingAction = true;
            if (gazeTracker != null) gazeTracker.TriggerNod(duration, 13f, 16f);
            yield return new WaitForSeconds(duration);

            if (gazeController != null && isAtHomeBase) gazeController.ResumeAutomaticGaze();
            else if (gazeController != null && !isAtHomeBase) gazeController.DisableGaze();
            yield return new WaitForSeconds(2.0f);
            isPerformingAction = false;
        }

        public void PlayShakeReaction(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.Shake, duration = duration });
        }

        private IEnumerator CoPlayShakeReactionInternal(float duration)
        {
            isPerformingAction = true;
            if (gazeTracker != null) gazeTracker.TriggerShake(duration, 10f, 12f);
            yield return new WaitForSeconds(duration);

            if (gazeController != null && isAtHomeBase) gazeController.ResumeAutomaticGaze();
            else if (gazeController != null && !isAtHomeBase) gazeController.DisableGaze();
            yield return new WaitForSeconds(2.0f);
            isPerformingAction = false;
        }

        public void PlayFaceGaze(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.FaceGaze, duration = duration });
        }

        private IEnumerator CoPlayFaceGazeInternal(float duration)
        {
            isPerformingAction = true;
            Transform playerTrans = FindPlayerTransform();
            if (playerTrans != null)
            {
                Vector3 toPlayer = playerTrans.position - transform.position;
                toPlayer.y = 0;
                if (toPlayer.magnitude > 0.1f)
                {
                    Quaternion facePlayerRot = Quaternion.LookRotation(toPlayer) * Quaternion.Euler(0, modelRotationOffset, 0);
                    float faceAlignTime = 0.8f;
                    float elapsedRot = 0f;
                    while (elapsedRot < faceAlignTime)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, facePlayerRot, Time.deltaTime * rotationSpeed);
                        elapsedRot += Time.deltaTime;
                        yield return null;
                    }
                    transform.rotation = facePlayerRot;
                }
            }

            if (gazeController != null)
            {
                gazeController.ForceGazeToType(GazeTargetType.Face);
            }

            yield return new WaitForSeconds(duration);

            if (gazeController != null && isAtHomeBase) gazeController.ResumeAutomaticGaze();
            else if (gazeController != null && !isAtHomeBase) gazeController.DisableGaze();
            yield return new WaitForSeconds(2.0f);
            isPerformingAction = false;
        }

        public void PlayAutomaticGaze(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.AutomaticGaze, duration = duration });
        }

        private IEnumerator CoPlayAutomaticGazeInternal(float duration)
        {
            isPerformingAction = true;
            Transform playerTrans = FindPlayerTransform();
            if (playerTrans != null)
            {
                Vector3 toPlayer = playerTrans.position - transform.position;
                toPlayer.y = 0;
                if (toPlayer.magnitude > 0.1f)
                {
                    Quaternion facePlayerRot = Quaternion.LookRotation(toPlayer) * Quaternion.Euler(0, modelRotationOffset, 0);
                    float faceAlignTime = 0.8f;
                    float elapsedRot = 0f;
                    while (elapsedRot < faceAlignTime)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, facePlayerRot, Time.deltaTime * rotationSpeed);
                        elapsedRot += Time.deltaTime;
                        yield return null;
                    }
                    transform.rotation = facePlayerRot;
                }
            }

            if (gazeController != null) gazeController.ResumeAutomaticGaze();
            yield return new WaitForSeconds(duration);
            if (gazeController != null && !isAtHomeBase) gazeController.DisableGaze();
            yield return new WaitForSeconds(2.0f);
            isPerformingAction = false;
        }

        public void PlayHappyReaction(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.Crouch, duration = duration }); 
        }

        private IEnumerator CoPlayHappyReactionInternal(float duration)
        {
            isDoingHappyBackflip = true; 
            isPerformingAction = true;
            
            if (gazeTracker != null) gazeTracker.ClearTarget();
            if (gazeController != null) gazeController.DisableGaze(); 

            if (animator != null && HasParameter(animator, "happy"))
            {
                animator.SetTrigger("happy");
            }

            yield return new WaitForSeconds(duration);

            // 착지 후 추가 2초 동안 시선 고정 중단 유지
            yield return new WaitForSeconds(2.0f);

            isDoingHappyBackflip = false; 
            isPerformingAction = false;

            if (gazeController != null && isAtHomeBase) gazeController.ResumeAutomaticGaze();
        }

        public void PlayWonderAnim(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.WatchAnim, duration = duration }); 
        }

        private IEnumerator CoPlayWonderAnimInternal(float duration)
        {
            isPerformingAction = true;
            if (animator != null && HasParameter(animator, "wonder")) animator.SetBool("wonder", true);
            yield return new WaitForSeconds(duration);
            if (animator != null && HasParameter(animator, "wonder")) animator.SetBool("wonder", false);
            yield return new WaitForSeconds(2.0f);
            isPerformingAction = false;
        }

        public void PlayGreetAnim(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.FaceGaze, duration = duration }); 
        }

        private IEnumerator CoPlayGreetAnimInternal(float duration)
        {
            isPerformingAction = true;
            if (animator != null)
            {
                if (HasParameter(animator, "greet"))
                {
                    AnimatorControllerParameterType pType = GetParameterType(animator, "greet");
                    if (pType == AnimatorControllerParameterType.Bool)
                    {
                        animator.SetBool("greet", true);
                    }
                    else
                    {
                        animator.SetTrigger("greet");
                    }
                }
            }
            yield return new WaitForSeconds(duration);
            if (animator != null && HasParameter(animator, "greet"))
            {
                if (GetParameterType(animator, "greet") == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool("greet", false);
                }
            }
            yield return new WaitForSeconds(2.0f);
            isPerformingAction = false;
        }

        private void ResetAllAnimatorActions()
        {
            SetGazeLock(false);
            SetWatchAnim(false);
            if (animator != null)
            {
                if (HasParameter(animator, "wonder")) animator.SetBool("wonder", false);
                if (HasParameter(animator, "greet") && GetParameterType(animator, "greet") == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool("greet", false);
                }
            }
        }

        #endregion

        private IEnumerator CoDialogueSequenceLoop()
        {
            yield return new WaitForSeconds(1.5f);

            while (true)
            {
                Transform playerTrans = FindPlayerTransform();

                // [테스트 루틴 작동]: 지정된 자리에 머물며 테스트
                if (testAllAnimations && playerTrans != null)
                {
                    isAtHomeBase = true;

                    Debug.Log("<color=lime><b>[이음 테스트 모드]</b></color> 1. 인사하기 (Greet)");
                    yield return StartCoroutine(CoPlayGreetAnimInternal(2.0f));

                    Debug.Log("<color=lime><b>[이음 테스트 모드]</b></color> 2. 고개 끄덕끄덕 (Nod)");
                    yield return StartCoroutine(CoPlayNodReactionInternal(2.0f));

                    Debug.Log("<color=lime><b>[이음 테스트 모드]</b></color> 3. 고개 절레절레 (Shake)");
                    yield return StartCoroutine(CoPlayShakeReactionInternal(2.0f));

                    Debug.Log("<color=lime><b>[이음 테스트 모드]</b></color> 4. 백덤블링 (Happy - 시선 추가 2초 잠금)");
                    yield return StartCoroutine(CoPlayHappyReactionInternal(1.5f)); 

                    Debug.Log("<color=lime><b>[이음 테스트 모드]</b></color> 5. 기웃거리기 (Wonder)");
                    yield return StartCoroutine(CoPlayWonderAnimInternal(2.5f));

                    Debug.Log("<color=lime><b>[이음 테스트 모드]</b></color> 6. 그윽하게 쳐다보기 (FaceGaze)");
                    yield return StartCoroutine(CoPlayFaceGazeInternal(2.0f));

                    Debug.Log("<color=lime><b>[이음 테스트 모드]</b></color> 7. 얼굴-손 번갈아 보기 (AutomaticGaze)");
                    yield return StartCoroutine(CoPlayAutomaticGazeInternal(3.0f));

                    yield return new WaitForSeconds(1.0f);
                    continue; 
                }

                if (routeQueue.Count > 0)
                {
                    isAtHomeBase = false; 
                    WaypointAction currentRoute = routeQueue.Dequeue();

                    if (currentRoute != null && currentRoute.waypoint != null)
                    {
                        currentActiveLookTarget = currentRoute.lookTarget != null ? currentRoute.lookTarget : cachedObjectTarget;
                        yield return StartCoroutine(CoWalkToPosition(currentRoute.waypoint.position, isAtWaypoint: true, customLookTarget: currentRoute.lookTarget));

                        if (currentRoute.onArrived != null && currentRoute.onArrived.GetPersistentEventCount() > 0)
                        {
                            actionQueue.Clear(); 
                            isPerformingAction = true;
                            currentRoute.onArrived.Invoke();
                            yield return StartCoroutine(CoExecuteActionQueue());
                        }
                        else
                        {
                            yield return new WaitForSeconds(3.0f);
                        }

                        if (patrolRoutes.Contains(currentRoute)) routeQueue.Enqueue(currentRoute);
                    }
                }
                else
                {
                    // [정지 대기 릴레이 작동]
                    if (isAtHomeBase)
                    {
                        if (gazeController != null) gazeController.ResumeAutomaticGaze();

                        switch (currentIdleReactionIndex)
                        {
                            case 0:
                                Debug.Log("<color=lime><b>[이음 대기 릴레이]</b></color> 1. 인사하기 (Greet)");
                                yield return StartCoroutine(CoPlayGreetAnimInternal(2.0f));
                                break;
                            case 1:
                                Debug.Log("<color=lime><b>[이음 대기 릴레이]</b></color> 2. 고개 끄덕끄덕 (Nod)");
                                yield return StartCoroutine(CoPlayNodReactionInternal(2.0f));
                                break;
                            case 2:
                                Debug.Log("<color=lime><b>[이음 대기 릴레이]</b></color> 3. 고개 절레절레 (Shake)");
                                yield return StartCoroutine(CoPlayShakeReactionInternal(2.0f));
                                break;
                            case 3:
                                Debug.Log("<color=lime><b>[이음 대기 릴레이]</b></color> 4. 백덤블링 (Happy - 시선 2초 대기)");
                                yield return StartCoroutine(CoPlayHappyReactionInternal(1.5f));
                                break;
                            case 4:
                                Debug.Log("<color=lime><b>[이음 대기 릴레이]</b></color> 5. 기웃거리기 (Wonder)");
                                yield return StartCoroutine(CoPlayWonderAnimInternal(2.5f));
                                break;
                            case 5:
                                Debug.Log("<color=lime><b>[이음 대기 릴레이]</b></color> 6. 얼굴-손 번갈아 보기 (AutomaticGaze)");
                                yield return StartCoroutine(CoPlayAutomaticGazeInternal(3.0f));
                                break;
                        }

                        // 다음 리액션으로 인덱스 순환 증가
                        currentIdleReactionIndex = (currentIdleReactionIndex + 1) % 6;

                        yield return new WaitForSeconds(0.5f);
                    }
                    else
                    {
                        currentIdleReactionIndex = 0;
                        yield return new WaitForSeconds(0.1f);
                    }
                }
            }
        }

        private IEnumerator CoWalkToPosition(Vector3 targetPos, bool isAtWaypoint, Transform customLookTarget = null, float customMoveSpeed = -1f)
        {
            isMoving = true;

            float activeMoveSpeed = customMoveSpeed > 0f ? customMoveSpeed : moveSpeed;

            if (animator != null && HasParameter(animator, "walk")) animator.SetBool("walk", false); 
            if (gazeController != null) gazeController.DisableGaze();

            float threshold = 0.25f; 
            float timeout = 4.0f; 
            float elapsed = 0f;

            Vector3 flightTargetPos = new Vector3(targetPos.x, targetPos.y + hoverHeight, targetPos.z);

            while (Vector3.Distance(transform.position, flightTargetPos) > threshold)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeout) break;

                Vector3 targetDir = flightTargetPos - transform.position;
                targetDir.y = 0; 

                if (targetDir.magnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(targetDir) * Quaternion.Euler(0, modelRotationOffset, 0);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
                    transform.position = Vector3.MoveTowards(transform.position, flightTargetPos, activeMoveSpeed * Time.deltaTime);
                }

                yield return null;
            }

            if (isAtWaypoint)
            {
                Transform lookTrans = customLookTarget != null ? customLookTarget : cachedObjectTarget;
                if (lookTrans != null)
                {
                    Vector3 toObject = lookTrans.position - transform.position;
                    toObject.y = 0;
                    if (toObject.magnitude > 0.1f)
                    {
                        Quaternion faceObjRot = Quaternion.LookRotation(toObject) * Quaternion.Euler(0, modelRotationOffset, 0);
                        float faceAlignTime = 0.8f;
                        float elapsedRot = 0f;
                        while (elapsedRot < faceAlignTime)
                        {
                            transform.rotation = Quaternion.Slerp(transform.rotation, faceObjRot, Time.deltaTime * rotationSpeed);
                            elapsedRot += Time.deltaTime;
                            yield return null;
                        }
                    }
                }
            }
            else
            {
                Transform playerTrans = FindPlayerTransform();
                if (playerTrans != null)
                {
                    Vector3 toPlayer = playerTrans.position - transform.position;
                    toPlayer.y = 0;
                    if (toPlayer.magnitude > 0.1f)
                    {
                        Quaternion facePlayerRot = Quaternion.LookRotation(toPlayer) * Quaternion.Euler(0, modelRotationOffset, 0);
                        float faceAlignTime = 0.8f;
                        float elapsedRot = 0f;
                        while (elapsedRot < faceAlignTime)
                        {
                            transform.rotation = Quaternion.Slerp(transform.rotation, facePlayerRot, Time.deltaTime * rotationSpeed);
                            elapsedRot += Time.deltaTime;
                            yield return null;
                        }
                        transform.rotation = facePlayerRot; 
                    }
                }
            }

            isMoving = false;
        }

        private void FindDefaultObjectTarget()
        {
            GameObject targetObj = GameObject.Find("ObjectTarget");
            if (targetObj == null) targetObj = GameObject.Find("WorkTarget");
            if (targetObj != null)
            {
                cachedObjectTarget = targetObj.transform;
            }
        }

        private IEnumerator CoExecuteActionQueue()
        {
            while (actionQueue.Count > 0)
            {
                ActionTask task = actionQueue.Dequeue();
                if (task.type == ActionType.GazeLock)
                {
                    if (gazeLockTimerCoroutine != null) StopCoroutine(gazeLockTimerCoroutine);
                    gazeLockTimerCoroutine = StartCoroutine(CoPlayGazeLockInternal(task.duration));
                    continue; 
                }

                switch (task.type)
                {
                    case ActionType.Crouch: 
                        yield return StartCoroutine(CoPlayHappyReactionInternal(task.duration));
                        break;
                    case ActionType.Nod:
                        yield return StartCoroutine(CoPlayNodReactionInternal(task.duration));
                        break;
                    case ActionType.Shake:
                        yield return StartCoroutine(CoPlayShakeReactionInternal(task.duration));
                        break;
                    case ActionType.WatchAnim: 
                        yield return StartCoroutine(CoPlayWonderAnimInternal(task.duration));
                        break;
                    case ActionType.FaceGaze: 
                        yield return StartCoroutine(CoPlayGreetAnimInternal(task.duration));
                        break;
                    case ActionType.AutomaticGaze:
                        yield return StartCoroutine(CoPlayAutomaticGazeInternal(task.duration));
                        break;
                }
            }

            ResetAllAnimatorActions();
            isPerformingAction = false;
            currentRunningTaskType = ActionType.Crouch; 
            currentActiveLookTarget = null; 
        }

        private void RestoreSequenceGazeTarget()
        {
            if (gazeController != null) gazeController.ForceGazeToType(GazeTargetType.Face);
        }

        private void FindBones()
        {
            Transform[] children = GetComponentsInChildren<Transform>();
            foreach (var child in children)
            {
                string nameLower = child.name.ToLower();
                if (neckBone == null && (nameLower.Contains("neck") || nameLower.Contains("collar"))) neckBone = child;
            }
        }

        private Transform FindHeadBoneFallback(Transform current)
        {
            string nameLower = current.name.ToLower();
            if (nameLower.Contains("head")) return current;

            for (int i = 0; i < current.childCount; i++)
            {
                Transform found = FindHeadBoneFallback(current.GetChild(i));
                if (found != null) return found;
            }
            return null;
        }

        private IGazeTarget GetGazeTargetFromTransform(Transform targetTrans)
        {
            if (targetTrans == null) return null;

            GazeTarget target = targetTrans.GetComponent<GazeTarget>();
            if (target == null)
            {
                target = targetTrans.gameObject.AddComponent<GazeTarget>();
                var typeField = typeof(GazeTarget).GetField("targetType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (typeField != null) typeField.SetValue(target, GazeTargetType.Other);
            }
            return target;
        }

        private IGazeTarget GetObjectGazeTarget()
        {
            return GetGazeTargetFromTransform(cachedObjectTarget);
        }

        private IGazeTarget FindPlayerGazeTarget()
        {
            var targets = FindObjectsOfType<GazeTarget>();
            foreach (var t in targets)
            {
                if (t.TargetType == GazeTargetType.Face) return t;
            }
            return null;
        }

        private Transform FindPlayerTransform()
        {
            var pm = FindObjectOfType<PlayerMovement>();
            if (pm != null) return pm.transform;

            // Fallback 1: Player 태그가 달린 오브젝트 우선 탐색
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) return playerObj.transform;

            // Fallback 2: VR 헤드셋 트래킹(MainCamera) 자동 탐색
            Camera mainCam = Camera.main;
            if (mainCam != null) return mainCam.transform;

            return null;
        }

        private bool HasParameter(Animator anim, string paramName)
        {
            foreach (AnimatorControllerParameter param in anim.parameters)
            {
                if (param.name == paramName) return true;
            }
            return false;
        }

        private AnimatorControllerParameterType GetParameterType(Animator anim, string paramName)
        {
            foreach (AnimatorControllerParameter param in anim.parameters)
            {
                if (param.name == paramName) return param.type;
            }
            return AnimatorControllerParameterType.Trigger;
        }

        private void OnDisable()
        {
            if (sequenceCoroutine != null) StopCoroutine(sequenceCoroutine);
        }
    }
}
