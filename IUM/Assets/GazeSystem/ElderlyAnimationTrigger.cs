using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GazeSystem
{
    /// <summary>
    /// 이동 목적지별 설정 및 도착 시 실행할 사용자 정의 이벤트를 묶은 구조체입니다.
    /// </summary>
    [System.Serializable]
    public class WaypointAction
    {
        [Tooltip("이동할 목적지 트랜스폼입니다.")]
        public Transform waypoint;

        [Tooltip("도착해서 바라볼 작업 대상 오브젝트입니다. 지정되지 않으면 전역 ObjectTarget 오브젝트 혹은 플레이어를 바라봅니다.")]
        public Transform lookTarget;

        [Tooltip("해당 목적지에 도착한 직후 실행할 유니티 이벤트입니다. 노장의 PlayCrouchAndLookAround, PlayNodReaction, PlayShakeReaction, PlayGazeLock 혹은 PlayWatchAnim 함수를 연결하여 행동을 순차적으로 설계할 수 있습니다.")]
        public UnityEvent onArrived;
    }

    public enum ActionType { Crouch, Nod, Shake, WatchAnim, GazeLock, FaceGaze, AutomaticGaze }

    /// <summary>
    /// 유니티 이벤트의 병렬 동시 호출을 완벽하게 수용하여 순차적으로 실행하기 위한 행동 태스크 클래스입니다.
    /// </summary>
    public class ActionTask
    {
        public ActionType type;
        public float duration;
    }

    /// <summary>
    /// 노인 캐릭터 특유의 느릿하고 묵직한 느낌을 위해 애니메이터 속도를 제어하고,
    /// 다중 목적지 경로(인스펙터의 + / - 리스트)를 돌며 물체를 관찰하고 이벤트를 실행하며,
    /// 원위치로 복귀하여 플레이어와 마주 보며 고개짓 대화하는 순찰형 인터랙션을 구동합니다.
    /// 플레이어 얼굴-손 번갈아보기 등의 대면 행동 시에는 최초 1회 부드럽게 몸을 플레이어로 돌린 뒤에 시선을 구동합니다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Gaze System/NPC/Elderly Animation Trigger")]
    public class ElderlyAnimationTrigger : MonoBehaviour
    {
        [Header("Elderly Timing Settings")]
        [Range(0.3f, 1.2f)]
        [SerializeField]
        [Tooltip("애니메이션 전체 재생 속도입니다. 0.6~0.75배속이 노인(노장)의 묵직한 움직임을 연출하기에 적당합니다.")]
        private float elderlyAnimationSpeed = 0.7f;

        [Header("Movement & Path Settings")]
        [SerializeField]
        [Tooltip("순찰할 경로와 도착 시 실행할 이벤트들입니다. 인스펙터의 + / - 버튼으로 자유롭게 편집하세요.")]
        private List<WaypointAction> patrolRoutes = new List<WaypointAction>();

        [SerializeField]
        [Tooltip("이동 속도(m/s)입니다.")]
        private float moveSpeed = 1.5f;

        [SerializeField]
        [Tooltip("회전 시 방향 전환 속도입니다.")]
        private float rotationSpeed = 5f;

        private Animator animator;
        private GazeTracker gazeTracker;
        private NPCGazeController gazeController;
        private Coroutine sequenceCoroutine;
        private Coroutine activeActionCoroutine;
        private Coroutine gazeLockTimerCoroutine;

        // FIFO 방식의 이동 경로 액션 큐
        private Queue<WaypointAction> routeQueue = new Queue<WaypointAction>();

        // 유니티 이벤트 슬롯 일괄 호출을 순차로 연쇄 재생하기 위한 내부 행동 큐
        private Queue<ActionTask> actionQueue = new Queue<ActionTask>();

        // 원래 스폰 상태 기억용 변수
        private Vector3 startPosition;
        private Quaternion startRotation;

        // 현재 사용자 정의 액션이 진행 중인지 여부 (이벤트 대기 제어용)
        private bool isPerformingAction = false;
        private ActionType currentRunningTaskType = ActionType.Crouch; // 현재 돌고 있는 태스크 타입 기록용

        // 물리적 시선 강제 고정 플래그
        private bool isGazeLocked = false;
        private bool lastGazeLockState = false;

        // 애니메이터 watch 변수 상태 제어 및 실시간 감시용 필드
        private bool watch = false;
        private bool lastWatchState = false;

        // 씬 내의 전역 작업 물체 캐싱용 변수
        private Transform cachedObjectTarget;

        // 현재 목적지 도달 시 활성화된 시선 조준 작업 대상
        private Transform currentActiveLookTarget;

        // 순찰 중인지 복귀 완료하여 플레이어와 대화 중인지 구분하기 위한 상태 플래그
        private bool isAtHomeBase = false;

        #region ================= [1. 물리적 시선 락 제어 API (Gaze Lock)] =================

        /// <summary>
        /// 물리적으로 대상을 강제 응시하게 만드는 시선락 상태를 켜고 끎니다. (애니메이터 변수 관여 없음)
        /// </summary>
        public void SetGazeLock(bool enabled)
        {
            isGazeLocked = enabled;
            Debug.Log($"[{gameObject.name}] 물리 시선 고정(GazeLock) 상태: {isGazeLocked}");
        }

        /// <summary>
        /// 현재 물리 시선락 활성화 상태를 반환합니다.
        /// </summary>
        public bool GetGazeLock()
        {
            return isGazeLocked;
        }

        #endregion

        #region ================= [2. 애니메이터 watch 파라미터 제어 API (Watch Anim)] =================

        /// <summary>
        /// 애니메이터 상의 'watch' (Bool) 파라미터를 수동으로 직접 켜고 끎니다. (물리 시선 조준 관여 없음)
        /// </summary>
        public void SetWatchAnim(bool enabled)
        {
            watch = enabled;
            if (animator != null && HasParameter(animator, "watch"))
            {
                animator.SetBool("watch", enabled);
                Debug.Log($"[{gameObject.name}] 애니메이터 'watch' Bool 값 설정: {enabled}");
            }
        }

        /// <summary>
        /// 현재 애니메이터의 'watch' (Bool) 파라미터 상태를 반환합니다.
        /// </summary>
        public bool GetWatchAnim()
        {
            return watch;
        }

        #endregion

        private void Start()
        {
            animator = GetComponent<Animator>();
            gazeTracker = GetComponent<GazeTracker>();
            gazeController = GetComponent<NPCGazeController>();

            // 애니메이션 루트 모션 강제 비활성화하여 지형 바닥 뚫림(파묻힘) 버그 원천 봉쇄
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            // 1. 노장의 호흡/움직임 묘사를 위한 재생 속도 튜닝 적용
            ApplyElderlySpeed();

            // 2. 초기 순찰 경로 리스트를 큐에 차례대로 Enqueue
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

            // 3. 씬 내의 기본 전역 ObjectTarget 찾아두기
            FindDefaultObjectTarget();

            // 4. 3단계 대화 인터랙션 및 순찰 루프 코루틴 실행
            if (gazeTracker != null && gazeController != null)
            {
                sequenceCoroutine = StartCoroutine(CoDialogueSequenceLoop());
            }
            else
            {
                Debug.LogError($"[{gameObject.name}] GazeTracker 혹은 NPCGazeController 컴포넌트를 찾을 수 없어 시퀀스를 구동하지 못했습니다.");
            }

            lastGazeLockState = isGazeLocked;
            lastWatchState = watch;
        }

        private void OnValidate()
        {
            if (Application.isPlaying && animator != null)
            {
                ApplyElderlySpeed();
            }
        }

        private void Update()
        {
            // 애니메이터 상의 'watch' bool 파라미터 변화가 있다면 스크립트 상태와 양방향 실시간 동기화
            if (animator != null && HasParameter(animator, "watch"))
            {
                bool animWatch = animator.GetBool("watch");
                if (animWatch != watch)
                {
                    watch = animWatch;
                }
            }

            // [물리 시선 오버라이드 조준 조건]:
            // 1. GazeLock(물리 시선 고정)이 활성화되어 있을 때
            // 2. 애니메이터의 watch 파라미터가 true일 때
            // 3. 순찰 도중 도착 이벤트 연출 동작이 수행 중이되, 플레이어 대면형 시선(FaceGaze, AutomaticGaze)을 수행하고 있지 않을 때
            bool isPerformingWorkGaze = isPerformingAction && 
                                        currentRunningTaskType != ActionType.FaceGaze && 
                                        currentRunningTaskType != ActionType.AutomaticGaze;

            bool shouldGazeAtTarget = isGazeLocked || watch || (!isAtHomeBase && isPerformingWorkGaze);

            if (shouldGazeAtTarget)
            {
                // 현재 목적지 경로에서 할당된 목표(lookTarget) 혹은 전역 물체(cachedObjectTarget)를 바라봅니다.
                Transform targetTrans = currentActiveLookTarget != null ? currentActiveLookTarget : cachedObjectTarget;
                IGazeTarget target = GetGazeTargetFromTransform(targetTrans);
                if (target == null)
                {
                    target = FindPlayerGazeTarget(); // 폴백
                }

                if (target != null && gazeTracker != null)
                {
                    gazeTracker.SetTarget(target);
                }
            }
            else
            {
                // 조준 상태가 완전 해제되는 즉시 원래 시선 상태로 복원 (플레이어 대면형 가동 중일 땐 리셋 차단)
                if (lastGazeLockState || lastWatchState || 
                    (!isPerformingAction && isAtHomeBase == false && 
                     currentRunningTaskType != ActionType.FaceGaze && 
                     currentRunningTaskType != ActionType.AutomaticGaze))
                {
                    RestoreSequenceGazeTarget();
                }
            }

            lastGazeLockState = isGazeLocked;
            lastWatchState = watch;
        }

        /// <summary>
        /// 애니메이터의 속도를 설정된 배속으로 조절하여 노인의 중량감을 묘사합니다.
        /// </summary>
        public void ApplyElderlySpeed()
        {
            if (animator != null)
            {
                animator.speed = elderlyAnimationSpeed;
                Debug.Log($"[{gameObject.name}] 노장 애니메이션 연출 속도가 {elderlyAnimationSpeed}배속으로 갱신되었습니다.");
            }
        }

        /// <summary>
        /// 런타임에 외부에서 동적으로 새로운 이동/작업 웨이포인트 액션을 삽입할 수 있는 퍼블릭 함수입니다.
        /// </summary>
        public void EnqueueWaypoint(Transform newWaypoint, Transform customLookTarget = null, UnityAction customEvent = null)
        {
            if (newWaypoint != null)
            {
                WaypointAction route = new WaypointAction
                {
                    waypoint = newWaypoint,
                    lookTarget = customLookTarget
                };
                if (customEvent != null)
                {
                    route.onArrived.AddListener(customEvent);
                }
                routeQueue.Enqueue(route);
                Debug.Log($"[{gameObject.name}] 동적 목적지 '{newWaypoint.name}'가 순찰 큐에 등록되었습니다.");
            }
        }

        #region ================= [인스펙터 이벤트 바인딩용 퍼블릭 행동 함수군] =================

        /// <summary>
        /// [행동 1-A: 수동 시간 입력 시선 고정]
        /// 지정된 시간(duration초) 동안 물리적으로 고개를 돌려 작업 물체를 똑바로 주시합니다. (애니메이터 동작 무관)
        /// </summary>
        public void PlayGazeLock(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.GazeLock, duration = duration });
            Debug.Log($"[{gameObject.name}] 행동 등록: 물리 시선 고정 ({duration}초 지정)");
        }

        /// <summary>
        /// [행동 1-B: 자동 시간 상속 시선 고정]
        /// 이벤트 목록 상 바로 다음(아래)에 등록된 자식 행동의 지속시간을 자동으로 상속받아 그 시간 동안 물리 시선락을 가동합니다.
        /// </summary>
        public void PlayGazeLock()
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.GazeLock, duration = -1f });
            Debug.Log($"[{gameObject.name}] 행동 등록: 물리 시선 고정 (아들 행동 시간 상속 모드)");
        }

        private IEnumerator CoPlayGazeLockInternal(float duration)
        {
            float targetDuration = duration;

            // 상속 모드(-1)일 경우 다음 행동 태스크를 엿보아 지속시간 획득
            if (targetDuration < 0f)
            {
                targetDuration = 3.0f; // 폴백용 기본 시간

                if (actionQueue.Count > 0)
                {
                    ActionTask nextTask = actionQueue.Peek();
                    if (nextTask != null && nextTask.duration > 0f)
                    {
                        targetDuration = nextTask.duration;
                        Debug.Log($"[{gameObject.name}] GazeLock 상속 성공: 아래 등록된 '{nextTask.type}' 행동의 지속시간 {targetDuration}초를 자동 차용합니다.");
                    }
                }
            }

            SetGazeLock(true);

            yield return new WaitForSeconds(targetDuration);

            SetGazeLock(false);

            // 순찰 중일 때에는 시선락이 끝나도 플레이어를 보지 않고 중립 정면 상태를 유지합니다. (원위치 복귀 대화 중에만 허용)
            if (gazeController != null && isAtHomeBase)
            {
                gazeController.ResumeAutomaticGaze();
            }
            else if (gazeController != null && !isAtHomeBase)
            {
                gazeController.DisableGaze(); // 플레이어 응시 방지
            }
        }

        /// <summary>
        /// [행동 2 - 애니메이터 watch 켜기]
        /// 지정된 시간(duration초) 동안 애니메이터의 'watch' (Bool) 파라미터를 켭니다. (물리 시선 고정 무관)
        /// </summary>
        public void PlayWatchAnim(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.WatchAnim, duration = duration });
            Debug.Log($"[{gameObject.name}] 행동 등록: 애니메이터 watch 가동 ({duration}초)");
        }

        private IEnumerator CoPlayWatchAnimInternal(float duration)
        {
            SetWatchAnim(true);

            yield return new WaitForSeconds(duration);

            SetWatchAnim(false);

            yield return new WaitForSeconds(2.0f);
        }

        /// <summary>
        /// [행동 3 - 웅크려 살펴보기] 
        /// 웅크려서 대상 물체를 두리번두리번 훑어보는 웅크림 관찰 행동을 순차 대기열에 추가합니다.
        /// </summary>
        public void PlayCrouchAndLookAround(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.Crouch, duration = duration });
            Debug.Log($"[{gameObject.name}] 행동 등록: 웅크려 살펴보기 ({duration}초)");
        }

        private IEnumerator CoPlayCrouchAndLookAroundInternal(float duration)
        {
            if (animator != null)
            {
                animator.SetBool("cross", false);
                animator.SetBool("back", false);
                animator.SetTrigger("crouch");
                animator.SetBool("crouching", true);
            }

            Transform activeLookTarget = currentActiveLookTarget != null ? currentActiveLookTarget : cachedObjectTarget;
            IGazeTarget target = GetGazeTargetFromTransform(activeLookTarget);
            if (target == null) target = FindPlayerGazeTarget();

            if (target != null && gazeTracker != null)
            {
                gazeTracker.SetTarget(target);
                gazeTracker.TriggerShake(duration: duration, speed: 3.0f, intensity: 10f); // 찬찬히 살펴보기
            }

            yield return new WaitForSeconds(duration);

            if (animator != null)
            {
                animator.SetBool("crouching", false);
                animator.ResetTrigger("crouch");
                animator.CrossFade("Idle", 0.25f); // 강제 기립 복귀
            }

            if (gazeController != null)
            {
                gazeController.DisableGaze(); // 시선 해제
            }

            Debug.Log($"[{gameObject.name}] 웅크려 살펴보기 종료 후 3초간 정지 대기");
            yield return new WaitForSeconds(3.0f);
        }

        /// <summary>
        /// [행동 4 - 고개 끄덕끄덕]
        /// 서서 타겟을 똑바로 바라보며 고개를 끄덕끄덕 긍정하는 행동을 순차 대기열에 추가합니다.
        /// </summary>
        public void PlayNodReaction(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.Nod, duration = duration });
            Debug.Log($"[{gameObject.name}] 행동 등록: 고개 끄덕끄덕 ({duration}초)");
        }

        private IEnumerator CoPlayNodReactionInternal(float duration)
        {
            if (animator != null)
            {
                animator.SetBool("back", true);
            }

            // 고개 끄덕임 진동 물리 모션 적용
            if (gazeTracker != null)
            {
                gazeTracker.TriggerNod(duration, 10f, 13f);
            }

            yield return new WaitForSeconds(duration);

            if (animator != null)
            {
                animator.SetBool("back", false);
            }

            // 복귀 홈 베이스 대화 모드일 때만 플레이어 얼굴/손 자동 번갈아 보기로 돌아갑니다.
            if (gazeController != null && isAtHomeBase)
            {
                gazeController.ResumeAutomaticGaze();
            }
            else if (gazeController != null && !isAtHomeBase)
            {
                gazeController.DisableGaze(); // 순찰 작업 중 플레이어 쳐다보기 방지
            }

            yield return new WaitForSeconds(2.0f);
        }

        /// <summary>
        /// [행동 5 - 고개 절레절레]
        /// 서서 타겟을 똑바로 바라보며 고개를 절레절레 부정하는 행동을 순차 대기열에 추가합니다.
        /// </summary>
        public void PlayShakeReaction(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.Shake, duration = duration });
            Debug.Log($"[{gameObject.name}] 행동 등록: 고개 절레절레 ({duration}초)");
        }

        private IEnumerator CoPlayShakeReactionInternal(float duration)
        {
            if (animator != null)
            {
                animator.SetBool("cross", true);
            }

            // 고개 절레절레 진동 물리 모션 적용
            if (gazeTracker != null)
            {
                gazeTracker.TriggerShake(duration, 10f, 12f);
            }

            yield return new WaitForSeconds(duration);

            if (animator != null)
            {
                animator.SetBool("cross", false);
            }

            // 복귀 홈 베이스 대화 모드일 때만 플레이어 얼굴/손 자동 번갈아 보기로 돌아갑니다.
            if (gazeController != null && isAtHomeBase)
            {
                gazeController.ResumeAutomaticGaze();
            }
            else if (gazeController != null && !isAtHomeBase)
            {
                gazeController.DisableGaze(); // 순찰 작업 중 플레이어 쳐다보기 방지
            }

            yield return new WaitForSeconds(2.0f);
        }

        /// <summary>
        /// [행동 6 - 플레이어 얼굴 그윽히 계속 주시]
        /// 지정된 시간(duration초) 동안 한눈팔지 않고 플레이어의 얼굴(Face)만 계속 집중하여 응시합니다. (시작 전 몸체 정렬 포함)
        /// </summary>
        public void PlayFaceGaze(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.FaceGaze, duration = duration });
            Debug.Log($"[{gameObject.name}] 행동 등록: 플레이어 얼굴 그윽하게 주시 ({duration}초)");
        }

        private IEnumerator CoPlayFaceGazeInternal(float duration)
        {
            // 최초 1회 몸 방향을 플레이어 방향으로 스르륵 돌림 정렬
            Transform playerTrans = FindPlayerTransform();
            if (playerTrans != null)
            {
                Vector3 toPlayer = playerTrans.position - transform.position;
                toPlayer.y = 0;
                if (toPlayer.magnitude > 0.1f)
                {
                    Quaternion facePlayerRot = Quaternion.LookRotation(toPlayer);
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

            if (gazeController != null && isAtHomeBase)
            {
                gazeController.ResumeAutomaticGaze();
            }
            else if (gazeController != null && !isAtHomeBase)
            {
                gazeController.DisableGaze();
            }

            yield return new WaitForSeconds(2.0f);
        }

        /// <summary>
        /// [행동 7 - 플레이어 얼굴/손 번갈아 보기 자동 모션]
        /// 지정된 시간(duration초) 동안 플레이어의 얼굴과 오른손을 자동으로 번갈아 바라봅니다. (시작 전 몸체 정렬 포함)
        /// </summary>
        public void PlayAutomaticGaze(float duration)
        {
            actionQueue.Enqueue(new ActionTask { type = ActionType.AutomaticGaze, duration = duration });
            Debug.Log($"[{gameObject.name}] 행동 등록: 플레이어 얼굴-손 자동 번갈아 보기 ({duration}초)");
        }

        private IEnumerator CoPlayAutomaticGazeInternal(float duration)
        {
            // [최초 1회 플레이어 방향으로 스르륵 몸 회전 정렬]
            Transform playerTrans = FindPlayerTransform();
            if (playerTrans != null)
            {
                Vector3 toPlayer = playerTrans.position - transform.position;
                toPlayer.y = 0;
                if (toPlayer.magnitude > 0.1f)
                {
                    Quaternion facePlayerRot = Quaternion.LookRotation(toPlayer);
                    float faceAlignTime = 0.8f;
                    float elapsedRot = 0f;
                    while (elapsedRot < faceAlignTime)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, facePlayerRot, Time.deltaTime * rotationSpeed);
                        elapsedRot += Time.deltaTime;
                        yield return null;
                    }
                    transform.rotation = facePlayerRot; // 정면 고정 완료
                }
            }

            if (gazeController != null)
            {
                gazeController.ResumeAutomaticGaze();
            }

            yield return new WaitForSeconds(duration);

            if (gazeController != null && !isAtHomeBase)
            {
                gazeController.DisableGaze(); // 순찰 중 연출 완료 시 시선 복구 해제
            }

            yield return new WaitForSeconds(2.0f);
        }

        private void ResetAllAnimatorActions()
        {
            if (animator != null)
            {
                animator.SetBool("crouching", false);
                animator.SetBool("back", false);
                animator.SetBool("cross", false);
                animator.ResetTrigger("crouch");
            }
            SetGazeLock(false);
            SetWatchAnim(false);
        }

        #endregion

        /// <summary>
        /// 이동과 상호작용 행동을 차례대로 수행하며 무한 순환하는 핵심 코루틴 루프입니다.
        /// </summary>
        private IEnumerator CoDialogueSequenceLoop()
        {
            // 원래 스폰 상태 저장
            startPosition = transform.position;
            startRotation = transform.rotation;

            // 씬 시작 시 초기화 안정화를 위해 1.5초 대기
            yield return new WaitForSeconds(1.5f);

            while (true)
            {
                if (routeQueue.Count > 0)
                {
                    isAtHomeBase = false; // 순찰 상태로 진입

                    // 큐에서 다음 목적지 루트 액션을 꺼냄
                    WaypointAction currentRoute = routeQueue.Dequeue();

                    if (currentRoute != null && currentRoute.waypoint != null)
                    {
                        // 현재 도착해서 바라봐야 할 경로의 셋업 타겟 수집
                        currentActiveLookTarget = currentRoute.lookTarget != null ? currentRoute.lookTarget : cachedObjectTarget;

                        Debug.Log($"[{gameObject.name}] 큐 목적지 '{currentRoute.waypoint.name}'로 이동 시작");
                        yield return StartCoroutine(CoWalkToPosition(currentRoute.waypoint.position, isAtWaypoint: true, customLookTarget: currentRoute.lookTarget));

                        // 도착 직후에 인스펙터에 등록된 UnityEvent를 트리거합니다.
                        if (currentRoute.onArrived != null && currentRoute.onArrived.GetPersistentEventCount() > 0)
                        {
                            actionQueue.Clear(); // 대기열 초기화
                            isPerformingAction = true;

                            // 이벤트 슬롯들을 동시 호출하여 actionQueue에 차례대로 작업을 쌓습니다.
                            Debug.Log($"[{gameObject.name}] '{currentRoute.waypoint.name}' 도착 이벤트 리스트 동시 호출");
                            currentRoute.onArrived.Invoke();

                            // 쌓인 actionQueue의 작업들을 차례대로 "순차적"으로 가동합니다.
                            yield return StartCoroutine(CoExecuteActionQueue());
                        }
                        else
                        {
                            // 만약 인스펙터 이벤트 창에 아무 함수도 등록하지 않았다면 기본 작업(웅크려 두리번 5초 ➔ 3초 대기)을 수행합니다.
                            yield return StartCoroutine(CoPerformCrouchInvestigation(currentRoute.lookTarget));
                        }

                        // 초기 정적 경로에 등록해둔 목적지는 무한 순찰을 위해 다시 큐의 끝에 재삽입
                        if (patrolRoutes.Contains(currentRoute))
                        {
                            routeQueue.Enqueue(currentRoute);
                        }
                    }
                }
                else
                {
                    // 큐가 비어 있고, 원래 위치에 있지 않다면 스폰 지점으로 복귀
                    float distanceToStart = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(startPosition.x, 0, startPosition.z));
                    if (distanceToStart > 0.5f)
                    {
                        Debug.Log($"[{gameObject.name}] 모든 작업 목적지 순찰 완료. 원래 스폰 위치로 복귀 시작");
                        yield return StartCoroutine(CoWalkToPosition(startPosition, isAtWaypoint: false));
                    }

                    isAtHomeBase = true; // 복귀 완료 대화 상태로 진입

                    // 원래 위치에서 플레이어를 향해 끄덕끄덕, 절레절레 대화 루틴 시작 (이동 전까지 목만 회전)
                    yield return StartCoroutine(CoPerformDialogueReactions());

                    // 큐가 비어 있는 동안 프레임 과부하를 막기 위한 대기
                    if (routeQueue.Count == 0)
                    {
                        yield return new WaitForSeconds(1.0f);
                    }
                }
            }
        }

        /// <summary>
        /// 이벤트 일괄 등록으로 누적된 actionQueue의 테스크를 순차적으로 이행하는 메인 러너 코루틴입니다.
        /// GazeLock(시선 고정)은 멈춤 대기(yield return) 없이 백그라운드로 즉시 가동하고 다음 줄(아들 행동)을 즉각 병렬 실행시킵니다.
        /// </summary>
        private IEnumerator CoExecuteActionQueue()
        {
            while (actionQueue.Count > 0)
            {
                ActionTask task = actionQueue.Dequeue();
                
                if (task.type == ActionType.GazeLock)
                {
                    // GazeLock은 큐의 흐름을 멈추고 기다리지 않고 즉각 비동기로 실행시켜 둔 채 다음 행동으로 즉시 건너뜁니다!
                    if (gazeLockTimerCoroutine != null) StopCoroutine(gazeLockTimerCoroutine);
                    gazeLockTimerCoroutine = StartCoroutine(CoPlayGazeLockInternal(task.duration));
                    continue; // 큐를 멈추지 않고 즉각 다음 태스크 Dequeue로 순차 이동 (병렬 실행)
                }

                Debug.Log($"[{gameObject.name}] 순차 액션 실행 시작: {task.type} ({task.duration}초)");
                currentRunningTaskType = task.type; // 현재 시작한 태스크의 종류 기록
                
                // 새로운 연출 행동을 시작할 때, 이전 애니메이션 상태들만 초기화 (GazeLock은 계속 켜져 있도록 보호)
                if (animator != null)
                {
                    animator.SetBool("crouching", false);
                    animator.SetBool("back", false);
                    animator.SetBool("cross", false);
                    animator.ResetTrigger("crouch");
                }

                switch (task.type)
                {
                    case ActionType.Crouch:
                        yield return StartCoroutine(CoPlayCrouchAndLookAroundInternal(task.duration));
                        break;
                    case ActionType.Nod:
                        yield return StartCoroutine(CoPlayNodReactionInternal(task.duration));
                        break;
                    case ActionType.Shake:
                        yield return StartCoroutine(CoPlayShakeReactionInternal(task.duration));
                        break;
                    case ActionType.WatchAnim:
                        yield return StartCoroutine(CoPlayWatchAnimInternal(task.duration));
                        break;
                    case ActionType.FaceGaze:
                        yield return StartCoroutine(CoPlayFaceGazeInternal(task.duration));
                        break;
                    case ActionType.AutomaticGaze:
                        yield return StartCoroutine(CoPlayAutomaticGazeInternal(task.duration));
                        break;
                }
            }

            ResetAllAnimatorActions();
            isPerformingAction = false;
            currentRunningTaskType = ActionType.Crouch; // 디폴트 상태 초기화
            currentActiveLookTarget = null; // 타겟 작업 완료 해제
            Debug.Log($"[{gameObject.name}] 등록된 모든 순차 액션 완료!");
        }

        /// <summary>
        /// 목적지까지 몸 방향을 돌리면서 걷고, 도착 후 플레이어 혹은 오브젝트 타겟 방향으로 다시 몸을 돌리는 이동 코루틴입니다.
        /// </summary>
        private IEnumerator CoWalkToPosition(Vector3 targetPos, bool isAtWaypoint, Transform customLookTarget = null)
        {
            // walk 애니메이션 파라미터 켜기 (bool)
            if (animator != null)
            {
                animator.SetBool("walk", true);
                animator.SetBool("crouching", false); // 걷기 전 강제로 웅크림 해제
                animator.ResetTrigger("crouch");
                animator.CrossFade("Idle", 0.1f);      // 강제 기립 상태 전이
            }

            // 걸어가는 동안에는 시선 연산을 중단하여 앞을 보고 걷게 함
            if (gazeController != null)
            {
                gazeController.DisableGaze();
            }

            float threshold = 0.25f; // 약간의 수평 물리적 오차를 메우기 위한 보정 반경
            float timeout = 8.0f;    // 목적지 이동 타임아웃 8초 (오브젝트 끼임 현상 자동 복구)
            float elapsed = 0f;

            // XZ 수평 축만으로 목적지 도달 여부 검사
            while (Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(targetPos.x, 0, targetPos.z)) > threshold)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeout)
                {
                    Debug.LogWarning($"[{gameObject.name}] 이동 타임아웃 초과! 다음 단계로 강제 전이합니다.");
                    break;
                }

                Vector3 targetDir = targetPos - transform.position;
                targetDir.y = 0; // 수평 방향 벡터로 고정

                if (targetDir.magnitude > 0.01f)
                {
                    // 진행 방향으로 부드럽게 몸 돌리기
                    Quaternion targetRot = Quaternion.LookRotation(targetDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);

                    // 수평 위치 이동
                    transform.position = Vector3.MoveTowards(transform.position, new Vector3(targetPos.x, transform.position.y, targetPos.z), moveSpeed * Time.deltaTime);
                }

                yield return null;
            }

            // walk 애니메이션 끄기
            if (animator != null)
            {
                animator.SetBool("walk", false);
            }

            // [도착 시점 1회 몸체 방향 정렬]
            if (isAtWaypoint)
            {
                // [순찰 목적지 도달 시]: 몸의 방향을 지정된 customLookTarget 혹은 objectTarget 방향으로 부드럽게 회전 정렬
                Transform lookTrans = customLookTarget != null ? customLookTarget : cachedObjectTarget;
                if (lookTrans != null)
                {
                    Vector3 toObject = lookTrans.position - transform.position;
                    toObject.y = 0;
                    if (toObject.magnitude > 0.1f)
                    {
                        Quaternion faceObjRot = Quaternion.LookRotation(toObject);
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
                // [복귀 완료 시]: 플레이어 내 얼굴 방향으로 몸을 딱 1회 정렬 (나중 이동 시까지 몸체는 고정)
                Transform playerTrans = FindPlayerTransform();
                if (playerTrans != null)
                {
                    Vector3 toPlayer = playerTrans.position - transform.position;
                    toPlayer.y = 0;
                    if (toPlayer.magnitude > 0.1f)
                    {
                        Quaternion facePlayerRot = Quaternion.LookRotation(toPlayer);
                        float faceAlignTime = 0.8f;
                        float elapsedRot = 0f;
                        while (elapsedRot < faceAlignTime)
                        {
                            transform.rotation = Quaternion.Slerp(transform.rotation, facePlayerRot, Time.deltaTime * rotationSpeed);
                            elapsedRot += Time.deltaTime;
                            yield return null;
                        }
                        transform.rotation = facePlayerRot; // 정확히 마주 보도록 최종 고정
                    }
                }
            }
        }

        /// <summary>
        /// 목적지에서 웅크리기(crouch)를 켜고 지정된 물체를 5초간 두리번두리번 관찰한 뒤, 다 끄고 3초를 쉬는 코루틴입니다.
        /// </summary>
        private IEnumerator CoPerformCrouchInvestigation(Transform customLookTarget = null)
        {
            Transform lookTrans = customLookTarget != null ? customLookTarget : cachedObjectTarget;
            IGazeTarget target = GetGazeTargetFromTransform(lookTrans);
            
            if (target == null)
            {
                target = FindPlayerGazeTarget();
            }

            Debug.Log($"[{gameObject.name}] [목적지] 웅크리기 + 물체 두리번두리번 관찰 시작 (5초간 진행)");
            
            // 1. crouching 애니메이터 파라미터 / Trigger 활성화
            if (animator != null)
            {
                animator.SetBool("cross", false);
                animator.SetBool("back", false);
                animator.SetTrigger("crouch");
                animator.SetBool("crouching", true);
            }

            // 2. target을 바라보며 찬찬히 고개 두리번두리번 살펴보기 (속도 3f로 부드럽게)
            if (target != null && gazeTracker != null)
            {
                gazeTracker.SetTarget(target);
                gazeTracker.TriggerShake(duration: 5.0f, speed: 3.0f, intensity: 10f); // 5초간 두리번두리번
            }

            yield return new WaitForSeconds(5.0f);

            // 3. 작업 종료 시 crouching 해제 및 Trigger 리셋 + 강제 기립 상태 전이(CrossFade)
            if (animator != null)
            {
                animator.SetBool("crouching", false);
                animator.ResetTrigger("crouch");
                animator.CrossFade("Idle", 0.25f); // 화살표 꼬임 대비 강제 일어서기 전이
            }

            if (gazeController != null)
            {
                gazeController.DisableGaze();
            }

            Debug.Log($"[{gameObject.name}] 웅크리기 및 관찰 종료 후 3초 정지 대기");
            yield return new WaitForSeconds(3.0f);
        }

        /// <summary>
        /// 원래 위치에서 플레이어 얼굴을 바라보고 끄덕끄덕(3초) ➔ 2초대기 ➔ 절레절레(3초) ➔ 2초대기 순으로 대화하는 코루틴입니다.
        /// </summary>
        private IEnumerator CoPerformDialogueReactions()
        {
            if (gazeController != null)
            {
                gazeController.ForceGazeToType(GazeTargetType.Face);
            }

            Debug.Log($"[{gameObject.name}] [원위치] 끄덕끄덕 시작 (3초 유지)");
            if (animator != null) animator.SetBool("back", true);
            if (gazeTracker != null) gazeTracker.TriggerNod(3.0f, 13f, 16f);
            yield return new WaitForSeconds(3.0f);

            if (animator != null) animator.SetBool("back", false);

            if (gazeController != null)
            {
                gazeController.ResumeAutomaticGaze();
            }

            yield return new WaitForSeconds(1.0f);

            if (gazeController != null)
            {
                gazeController.ForceGazeToType(GazeTargetType.Face);
            }

            Debug.Log($"[{gameObject.name}] [원위치] 절레절레 시작 (3초 유지)");
            if (animator != null) animator.SetBool("cross", true);
            if (gazeTracker != null) gazeTracker.TriggerShake(3.0f, 12f, 14f);
            yield return new WaitForSeconds(3.0f);

            if (animator != null) animator.SetBool("cross", false);

            if (gazeController != null)
            {
                gazeController.ResumeAutomaticGaze();
            }

            yield return new WaitForSeconds(1.0f);
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

        private void RestoreSequenceGazeTarget()
        {
            if (gazeController != null)
            {
                float distanceToStart = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(startPosition.x, 0, startPosition.z));
                if (routeQueue.Count == 0 && distanceToStart < 0.5f)
                {
                    gazeController.ForceGazeToType(GazeTargetType.Face);
                }
            }
        }

        private IGazeTarget GetGazeTargetFromTransform(Transform targetTrans)
        {
            if (targetTrans == null) return null;

            GazeTarget target = targetTrans.GetComponent<GazeTarget>();
            if (target == null)
            {
                target = targetTrans.gameObject.AddComponent<GazeTarget>();
                var typeField = typeof(GazeTarget).GetField("targetType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (typeField != null)
                {
                    typeField.SetValue(target, GazeTargetType.Other);
                }
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
            return pm != null ? pm.transform : null;
        }

        private bool HasParameter(Animator anim, string paramName)
        {
            foreach (AnimatorControllerParameter param in anim.parameters)
            {
                if (param.name == paramName) return true;
            }
            return false;
        }

        private void OnDisable()
        {
            if (sequenceCoroutine != null)
            {
                StopCoroutine(sequenceCoroutine);
            }
        }
    }
}
