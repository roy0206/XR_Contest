using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 플레이어 오브젝트에 부착하여 얼굴, 왼손, 오른손의 시선 추적용 타겟(GazeTarget)을 
    /// 자동으로 씬에 생성하거나 VR 컨트롤러에 연결해 주는 자동 설정 헬퍼 컴포넌트입니다.
    /// PC 모드 시, 사용자의 요청에 따라 가상 손의 월드 Y 높이를 무조건 0.5f로 강제 고정하여 추적합니다.
    /// </summary>
    [AddComponentMenu("Gaze System/Player/Player Gaze Target Auto Setup")]
    public class PlayerGazeTargetAutoSetup : MonoBehaviour
    {
        [Header("VR Auto Search Settings")]
        [SerializeField]
        [Tooltip("VR 환경(XR Origin/OVRCameraRig 등)에서 머리/양손 오브젝트를 자동으로 검색하여 부착할지 여부입니다.")]
        private bool autoSearchVRBones = true;

        [Header("Manual Reference (Optional)")]
        [SerializeField]
        [Tooltip("수동으로 머리(HMD) 오브젝트를 지정합니다. 지정하지 않으면 메인 카메라를 기준으로 자동 탐색/스폰합니다.")]
        private Transform headAnchor;

        [SerializeField]
        [Tooltip("수동으로 왼손 컨트롤러 오브젝트를 지정합니다.")]
        private Transform leftHandAnchor;

        [SerializeField]
        [Tooltip("수동으로 오른손 컨트롤러 오브젝트를 지정합니다.")]
        private Transform rightHandAnchor;

        [Header("PC Mode Hand Offsets (Horizontal Only)")]
        [SerializeField]
        [Tooltip("플레이어 중심 기준으로 왼손이 위치할 좌우(X), 전방(Z) 거리입니다.")]
        private float leftHandHorizontalX = -0.3f;

        [SerializeField]
        [Tooltip("플레이어 중심 기준으로 오른손이 위치할 좌우(X), 전방(Z) 거리입니다.")]
        private float rightHandHorizontalX = 0.3f;

        [SerializeField]
        [Tooltip("플레이어 중심 기준으로 손이 전방으로 내밀어지는 거리입니다.")]
        private float handForwardDistance = 0.4f;

        [Header("Force Gaze Height (PC Mode)")]
        [SerializeField]
        [Tooltip("PC 모드 작동 시, 가상 손 타겟의 플레이어 발밑(루트) 기준 Y 높이를 설정할 값입니다.")]
        private float forceHandWorldHeight = 0.6f;

        [SerializeField]
        [Tooltip("PC 환경 등에서 자동으로 생성될 얼굴 타겟의 로컬 위치 오프셋입니다. (카메라가 없을 시 적용)")]
        private Vector3 faceOffset = new Vector3(0f, 1.0f, 0f);

        // 생성된 가상 타겟 트랜스폼 레퍼런스
        private Transform spawnedLeftHand;
        private Transform spawnedRightHand;
        private Transform spawnedFace;

        private void Start()
        {
            SetupTargets();

            // 만약 씬에 NPCGazeController가 있다면 타겟이 새로 추가되었음을 갱신하도록 신호 전달
            var npcControllers = FindObjectsOfType<NPCGazeController>();
            foreach (var npc in npcControllers)
            {
                npc.InitializeTargets();
            }
        }

        private void Update()
        {
            UpdatePCModeTargetPositions();
        }

        /// <summary>
        /// 설정에 따라 적절한 뼈대나 카메라를 찾아 GazeTarget을 생성 및 부착합니다.
        /// </summary>
        public void SetupTargets()
        {
            // 1. 머리/얼굴 타겟 설정
            Transform targetHead = headAnchor;
            if (targetHead == null)
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    targetHead = mainCam.transform;
                }
            }

            if (targetHead != null)
            {
                CreateOrAttachGazeTarget(targetHead.gameObject, GazeTargetType.Face, "Auto_FaceTarget");
                spawnedFace = targetHead;
            }
            else
            {
                GameObject faceObj = new GameObject("Auto_FaceTarget");
                faceObj.transform.SetParent(transform);
                faceObj.transform.localPosition = faceOffset;
                faceObj.transform.localRotation = Quaternion.identity;
                faceObj.transform.localScale = Vector3.one;
                CreateOrAttachGazeTarget(faceObj, GazeTargetType.Face, "Auto_FaceTarget");
                spawnedFace = faceObj.transform;
            }

            // 2. VR 자동 검색 시도
            if (autoSearchVRBones && (leftHandAnchor == null || rightHandAnchor == null))
            {
                SearchVRAnchorsInHierarchy();
            }

            // 3. 왼손 타겟 설정
            if (leftHandAnchor != null)
            {
                CreateOrAttachGazeTarget(leftHandAnchor.gameObject, GazeTargetType.LeftHand, "Auto_LeftHandTarget");
            }
            else
            {
                // 왼손 앵커가 없으면 월드 좌표 갱신형 빈 오브젝트 생성
                GameObject leftHandObj = new GameObject("Auto_LeftHandTarget");
                leftHandObj.transform.localScale = Vector3.one;
                CreateOrAttachGazeTarget(leftHandObj, GazeTargetType.LeftHand, "Auto_LeftHandTarget");
                spawnedLeftHand = leftHandObj.transform;
            }

            // 4. 오른손 타겟 설정
            if (rightHandAnchor != null)
            {
                CreateOrAttachGazeTarget(rightHandAnchor.gameObject, GazeTargetType.RightHand, "Auto_RightHandTarget");
            }
            else
            {
                // 오른손 앵커가 없으면 월드 좌표 갱신형 빈 오브젝트 생성
                GameObject rightHandObj = new GameObject("Auto_RightHandTarget");
                rightHandObj.transform.localScale = Vector3.one;
                CreateOrAttachGazeTarget(rightHandObj, GazeTargetType.RightHand, "Auto_RightHandTarget");
                spawnedRightHand = rightHandObj.transform;
            }

            // 초기 위치 업데이트 적용
            UpdatePCModeTargetPositions();
        }

        /// <summary>
        /// PC 테스트 모드 시 가상 손의 X, Z 좌표는 카메라의 수평 방향을 쫓아가게 하고, Y(높이)는 무조건 0.5f로 고정합니다.
        /// </summary>
        private void UpdatePCModeTargetPositions()
        {
            Transform refTransform = headAnchor != null ? headAnchor : (Camera.main != null ? Camera.main.transform : transform);
            if (refTransform == null) return;

            // 카메라가 바라보는 평면 상의 정면/우측 벡터 계산 (기울어짐 배제)
            Vector3 forward = refTransform.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 right = refTransform.right;
            right.y = 0;
            right.Normalize();

            // 왼손 위치 계산 및 플레이어 루트 기준 Y축 높이 고정
            if (spawnedLeftHand != null)
            {
                Vector3 targetPos = refTransform.position + (right * leftHandHorizontalX) + (forward * handForwardDistance);
                targetPos.y = transform.position.y + forceHandWorldHeight; // 플레이어 루트 Y 대비 상대 높이 적용
                spawnedLeftHand.position = targetPos;
            }

            // 오른손 위치 계산 및 플레이어 루트 기준 Y축 높이 고정
            if (spawnedRightHand != null)
            {
                Vector3 targetPos = refTransform.position + (right * rightHandHorizontalX) + (forward * handForwardDistance);
                targetPos.y = transform.position.y + forceHandWorldHeight; // 플레이어 루트 Y 대비 상대 높이 적용
                spawnedRightHand.position = targetPos;
            }
        }

        /// <summary>
        /// 특정 게임오브젝트에 GazeTarget 컴포넌트가 없다면 추가하고 설정을 지정합니다.
        /// </summary>
        private void CreateOrAttachGazeTarget(GameObject targetObj, GazeTargetType type, string defaultName)
        {
            if (targetObj == null) return;

            GazeTarget target = targetObj.GetComponent<GazeTarget>();
            if (target == null)
            {
                target = targetObj.AddComponent<GazeTarget>();
            }

            var targetField = typeof(GazeTarget).GetField("targetType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (targetField != null)
            {
                targetField.SetValue(target, type);
            }
        }

        /// <summary>
        /// 하이어라키에서 이름 규칙을 통해 XR 컨트롤러 앵커 오브젝트들을 자동 검색합니다.
        /// </summary>
        private void SearchVRAnchorsInHierarchy()
        {
            Transform[] allChildren = GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
            {
                string nameLower = child.name.ToLower();

                // 왼손 검색
                if (leftHandAnchor == null && (nameLower.Contains("left") || nameLower.Contains("l_")) && 
                    (nameLower.Contains("hand") || nameLower.Contains("controller") || nameLower.Contains("anchor")))
                {
                    leftHandAnchor = child;
                    continue;
                }

                // 오른손 검색
                if (rightHandAnchor == null && (nameLower.Contains("right") || nameLower.Contains("r_")) && 
                    (nameLower.Contains("hand") || nameLower.Contains("controller") || nameLower.Contains("anchor")))
                {
                    rightHandAnchor = child;
                    continue;
                }
            }
        }
    }
}
