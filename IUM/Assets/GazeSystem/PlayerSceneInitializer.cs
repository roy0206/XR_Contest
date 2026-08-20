using System;
using System.Collections;
using System.Collections.Generic; // List<> 관련 CS0246 오류 해결을 위해 반드시 추가
using System.Reflection;
using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 씬 진입 시 동적으로 플레이어 리그(PlayerRig)와 가상 카메라 제어를 초기화 조립하고,
    /// 씬 내의 모든 NPC들을 탐색하여 Gaze System 필수 컴포넌트(GazeTracker, NPCGazeController, 
    /// SequenceGazeSelectionPolicy, ElderlyAnimationTrigger 등)를 동적 조립하고
    /// 씬의 경로 및 타겟 물체를 자동 감지하여 바인딩해 주는 통합 이니셜라이저입니다.
    /// </summary>
    [DefaultExecutionOrder(-100)] // 다른 일반 스크립트보다 먼저 세팅 완료되도록 보장
    public class PlayerSceneInitializer : MonoBehaviour
    {
        [Header("Gaze Target Auto Setup Config")]
        [SerializeField]
        [Tooltip("오른손 가상 타겟의 전방 거리 오프셋입니다.")]
        private float handForwardDistance = 0.4f;

        [SerializeField]
        [Tooltip("오른손 가상 타겟의 우측 거리 오프셋입니다.")]
        private float rightHandHorizontalX = 0.3f;

        [SerializeField]
        [Tooltip("왼손 가상 타겟의 좌측 거리 오프셋입니다.")]
        private float leftHandHorizontalX = -0.3f;

        [SerializeField]
        [Tooltip("플레이어 가상 손 타겟의 지면 대비 상대적 높이 오프셋입니다.")]
        private float relativeHandHeight = 0.6f;

        private void Awake()
        {
            // 1. 플레이어 카메라 리그 및 WASD 이동 자동 조립
            SetupPlayerRig();

            // 2. 씬 내 모든 NPC 자동 탐색 및 시선/이동 자동 조립 바인딩
            SetupNPCs();
        }

        private void SetupPlayerRig()
        {
            // 메인 카메라 찾기
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                mainCam = FindObjectOfType<Camera>();
            }

            if (mainCam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                mainCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
                Debug.LogWarning("[PlayerSceneInitializer] 씬 내 카메라가 없어 메인 카메라를 신규 생성했습니다.");
            }

            // 카메라 태그 메인카메라로 확정
            mainCam.tag = "MainCamera";

            // PlayerRig 루트 오브젝트 구성
            GameObject playerRig = GameObject.Find("PlayerRig");
            if (playerRig == null)
            {
                playerRig = new GameObject("PlayerRig");
                playerRig.transform.position = Vector3.zero;
                playerRig.transform.rotation = Quaternion.identity;
            }

            // 카메라를 PlayerRig 하위로 편입하여 VR/PC 중심축 정렬
            mainCam.transform.SetParent(playerRig.transform);

            // PC 조작용 WASD/마우스 시선 제어 스크립트(PlayerMovement) 자동 탑재
            PlayerMovement movement = playerRig.GetComponent<PlayerMovement>();
            if (movement == null)
            {
                movement = playerRig.AddComponent<PlayerMovement>();
            }

            // VR/PC 공용 가상 손/얼굴 GazeTarget 자동 앵커 조립기 부착
            PlayerGazeTargetAutoSetup targetSetup = playerRig.GetComponent<PlayerGazeTargetAutoSetup>();
            if (targetSetup == null)
            {
                targetSetup = playerRig.AddComponent<PlayerGazeTargetAutoSetup>();
            }

            // 인스펙터 설정값들 리플렉션으로 타겟 셋업에 전달하여 지형 묻힘 보정
            var fwdField = typeof(PlayerGazeTargetAutoSetup).GetField("handForwardDistance", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fwdField != null) fwdField.SetValue(targetSetup, handForwardDistance);

            var rxField = typeof(PlayerGazeTargetAutoSetup).GetField("rightHandHorizontalX", BindingFlags.NonPublic | BindingFlags.Instance);
            if (rxField != null) rxField.SetValue(targetSetup, rightHandHorizontalX);

            var lxField = typeof(PlayerGazeTargetAutoSetup).GetField("leftHandHorizontalX", BindingFlags.NonPublic | BindingFlags.Instance);
            if (lxField != null) lxField.SetValue(targetSetup, leftHandHorizontalX);

            var heightField = typeof(PlayerGazeTargetAutoSetup).GetField("relativeHandHeight", BindingFlags.NonPublic | BindingFlags.Instance);
            if (heightField != null) heightField.SetValue(targetSetup, relativeHandHeight);

            Debug.Log("[PlayerSceneInitializer] 플레이어 씬 리그 구성 및 VR/PC 타겟 영점 보정 조립 완료!");
        }

        private void SetupNPCs()
        {
            // 씬 내의 모든 Animator를 탑재한 NPC 캐릭터 객체 수집
            Animator[] animators = FindObjectsOfType<Animator>();

            foreach (var anim in animators)
            {
                GameObject npcObj = anim.gameObject;

                // PlayerRig나 카메라 등 플레이어 객체는 NPC 조립 대상에서 스킵
                if (npcObj.name.Contains("Player") || npcObj.name.Contains("Camera") || npcObj.transform.parent != null && npcObj.transform.parent.name.Contains("Player"))
                {
                    continue;
                }

                // GazeTracker 본 연동을 위한 머리 뼈(Head) 자동 탐색
                Transform headBone = FindHeadBone(npcObj.transform);
                if (headBone == null)
                {
                    // 머리 뼈를 찾지 못했다면 자기 자신을 대용으로 지정하여 조립을 계속 진행 (부착 스킵 차단)
                    headBone = npcObj.transform;
                    Debug.LogWarning($"[PlayerSceneInitializer] '{npcObj.name}' 캐릭터에서 Head Bone을 찾지 못해 본인을 머리축으로 폴백 지정하여 조립을 강제 수용합니다.");
                }

                // GazeTracker 컴포넌트가 없을 시 조립
                GazeTracker tracker = npcObj.GetComponent<GazeTracker>();
                if (tracker == null)
                {
                    tracker = npcObj.AddComponent<GazeTracker>();
                }

                // 시선 자동 스케줄러 컴포넌트들 조립
                SequenceGazeSelectionPolicy policy = npcObj.GetComponent<SequenceGazeSelectionPolicy>();
                if (policy == null)
                {
                    policy = npcObj.AddComponent<SequenceGazeSelectionPolicy>();
                }

                NPCGazeController controller = npcObj.GetComponent<NPCGazeController>();
                if (controller == null)
                {
                    controller = npcObj.AddComponent<NPCGazeController>();
                }

                NPCGazeDirector director = npcObj.GetComponent<NPCGazeDirector>();
                if (director == null)
                {
                    director = npcObj.AddComponent<NPCGazeDirector>();
                }

                // 캐릭터 이름에 eeum(이음) 또는 iumi(이움/이음)가 들어간 경우 EeumAnimationTrigger를 조립하고, 그렇지 않으면 ElderlyAnimationTrigger를 조립합니다.
                bool isEeum = npcObj.name.ToLower().Contains("eeum") || npcObj.name.ToLower().Contains("iumi");

                // 비활성화된 오브젝트까지 탐색하여 자동 작업물 타겟 감색
                GameObject workTargetObj = FindObjectIncludingInactive("ObjectTarget");
                if (workTargetObj == null)
                {
                    workTargetObj = FindObjectIncludingInactive("WorkTarget");
                }

                if (isEeum)
                {
                    EeumAnimationTrigger eeumTrigger = npcObj.GetComponent<EeumAnimationTrigger>();
                    if (eeumTrigger == null)
                    {
                        eeumTrigger = npcObj.AddComponent<EeumAnimationTrigger>();
                    }

                    // EeumAnimationTrigger의 patrolRoutes 자동 바인딩
                    var routesField = typeof(EeumAnimationTrigger).GetField("patrolRoutes", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (routesField != null)
                    {
                        var currentRoutes = routesField.GetValue(eeumTrigger) as System.Collections.IList;
                        if (currentRoutes == null || currentRoutes.Count == 0)
                        {
                            var allWaypoints = GetSceneWaypoints();
                            if (allWaypoints.Count > 0)
                            {
                                var customRoutesList = new List<WaypointAction>();
                                foreach (var wp in allWaypoints)
                                {
                                    customRoutesList.Add(new WaypointAction { waypoint = wp });
                                }
                                routesField.SetValue(eeumTrigger, customRoutesList);
                                Debug.Log($"[PlayerSceneInitializer] 이음 인스펙터 경로가 비어 있어, 씬 내 {allWaypoints.Count}개의 웨이포인트를 자동 주입했습니다.");
                            }
                        }
                    }

                    if (workTargetObj != null)
                    {
                        var objectTargetField = typeof(EeumAnimationTrigger).GetField("cachedObjectTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (objectTargetField != null)
                        {
                            objectTargetField.SetValue(eeumTrigger, workTargetObj.transform);
                            Debug.Log($"[PlayerSceneInitializer] 이음의 cachedObjectTarget에 '{workTargetObj.name}'를 자동 바인딩 완료!");
                        }
                    }
                }
                else
                {
                    // 노인 특유의 웅크림과 고개짓 시퀀스를 처리하는 ElderlyAnimationTrigger 자동 조립
                    ElderlyAnimationTrigger elderlyTrigger = npcObj.GetComponent<ElderlyAnimationTrigger>();
                    if (elderlyTrigger == null)
                    {
                        elderlyTrigger = npcObj.AddComponent<ElderlyAnimationTrigger>();
                    }

                    // GazeTracker의 headTransform에 탐색한 머리뼈 리플렉션 주입
                    var routesField = typeof(ElderlyAnimationTrigger).GetField("patrolRoutes", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (routesField != null)
                    {
                        var currentRoutes = routesField.GetValue(elderlyTrigger) as System.Collections.IList;
                        if (currentRoutes == null || currentRoutes.Count == 0)
                        {
                            var allWaypoints = GetSceneWaypoints();
                            if (allWaypoints.Count > 0)
                            {
                                var customRoutesList = new List<WaypointAction>();
                                foreach (var wp in allWaypoints)
                                {
                                    customRoutesList.Add(new WaypointAction { waypoint = wp });
                                }
                                routesField.SetValue(elderlyTrigger, customRoutesList);
                                Debug.Log($"[PlayerSceneInitializer] 노장 인스펙터 경로가 비어 있어, 씬 내 {allWaypoints.Count}개의 웨이포인트를 자동 주입했습니다.");
                            }
                        }
                    }

                    if (workTargetObj != null)
                    {
                        var objectTargetField = typeof(ElderlyAnimationTrigger).GetField("cachedObjectTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (objectTargetField != null)
                        {
                            objectTargetField.SetValue(elderlyTrigger, workTargetObj.transform);
                            Debug.Log($"[PlayerSceneInitializer] 노장의 cachedObjectTarget에 '{workTargetObj.name}'를 자동 바인딩 완료!");
                        }
                    }
                }
                
                // GazeTracker의 headTransform에 탐색한 머리뼈 리플렉션 주입
                var headTransField = typeof(GazeTracker).GetField("headTransform", BindingFlags.NonPublic | BindingFlags.Instance);
                if (headTransField != null)
                {
                    headTransField.SetValue(tracker, headBone);
                }

                // 믹사모 캐릭터의 경우 일반적으로 정면을 바라보게 하기 위해 X축 90도 또는 -90도 오프셋이 필요함
                if (headBone.name.ToLower().Contains("mixamo"))
                {
                    var axisField = typeof(GazeTracker).GetField("headRotationOffset", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (axisField != null)
                    {
                        axisField.SetValue(tracker, new Vector3(-90f, 0f, 0f));
                    }
                }

                Debug.Log($"[PlayerSceneInitializer] NPC '{npcObj.name}' 시선 추적 및 리액션 자동 조립 조율 완료!");
            }
        }

        private List<Transform> GetSceneWaypoints()
        {
            var allWaypoints = new List<Transform>();
            
            // 기본 웨이포인트 감지
            GameObject firstWpObj = FindObjectIncludingInactive("Waypoint");
            if (firstWpObj == null)
            {
                firstWpObj = FindObjectIncludingInactive("PatrolWaypoint");
            }
            if (firstWpObj != null)
            {
                allWaypoints.Add(firstWpObj.transform);
            }

            // 복제 시 자동으로 붙는 유니티 넘버링 (Waypoint (1), Waypoint (2)...) 감지
            int wpIndex = 1;
            while (true)
            {
                GameObject numberedWp = FindObjectIncludingInactive($"Waypoint ({wpIndex})");
                if (numberedWp == null)
                {
                    numberedWp = FindObjectIncludingInactive($"PatrolWaypoint ({wpIndex})");
                }

                if (numberedWp != null)
                {
                    allWaypoints.Add(numberedWp.transform);
                    wpIndex++;
                }
                else
                {
                    break;
                }
            }
            return allWaypoints;
        }

        private static Transform FindHeadBone(Transform current)
        {
            string nameLower = current.name.ToLower();
            if (nameLower.Contains("head"))
            {
                return current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                Transform found = FindHeadBone(current.GetChild(i));
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// 씬에 존재하는 비활성화된 오브젝트까지 찾기 위한 Resources.FindObjectsOfTypeAll 기반 헬퍼 함수입니다.
        /// </summary>
        private static GameObject FindObjectIncludingInactive(string name)
        {
            Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform t in allTransforms)
            {
                // 프리팹이나 프로젝트 에셋이 아닌 씬 내에 있는 활성/비활성 오브젝트인지 필터링
                if (t.gameObject.scene.name != null && t.name == name)
                {
                    return t.gameObject;
                }
            }
            return null;
        }
    }
}
