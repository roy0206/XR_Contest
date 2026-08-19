using System.Collections.Generic;
using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// NPC의 행동을 제어하여 일정 주기마다 플레이어의 얼굴이나 손을 번갈아 쳐다보게 하는 상위 컨트롤러입니다.
    /// 런타임 실행 순서 꼬임으로 인한 타겟 누락을 방어하는 자가 치유(Self-Healing) 탐색 로직이 내장되어 있습니다.
    /// </summary>
    [RequireComponent(typeof(GazeTracker))]
    [AddComponentMenu("Gaze System/NPC/NPC Gaze Controller")]
    public class NPCGazeController : MonoBehaviour
    {
        [Header("Required Components")]
        [SerializeField]
        [Tooltip("시선 연산을 담당하는 GazeTracker입니다. 미지정 시 이 오브젝트에서 찾습니다.")]
        private GazeTracker gazeTracker;

        [Header("Target Configuration")]
        [SerializeField]
        [Tooltip("씬 시작 시 활성화된 모든 GazeTarget을 자동으로 검색할지 여부입니다.")]
        private bool autoFindTargets = true;

        [SerializeField]
        [Tooltip("직접 인스펙터에서 시선 대상을 지정할 수 있습니다. (Auto Find가 꺼져있을 때 유용)")]
        private List<GazeTarget> customTargets = new List<GazeTarget>();

        private List<IGazeTarget> activeTargets = new List<IGazeTarget>();
        private IGazeSelectionPolicy selectionPolicy;
        private IGazeTarget currentTarget;

        private float intervalTimer = 0f;
        private float nextSwitchTime = 2f;

        // 외부에서 시선을 임의로 강제 고정했는지 여부
        private bool isGazeOverridden = false;

        private void Start()
        {
            if (gazeTracker == null)
            {
                gazeTracker = GetComponent<GazeTracker>();
            }

            selectionPolicy = GetComponent<IGazeSelectionPolicy>();
            if (selectionPolicy == null)
            {
                Debug.LogWarning($"[{gameObject.name}] IGazeSelectionPolicy를 구현한 컴포넌트가 없습니다. 기본 IntervalGazeSelectionPolicy를 추가합니다.");
                selectionPolicy = gameObject.AddComponent<IntervalGazeSelectionPolicy>();
            }

            InitializeTargets();
            ResetTimer();
        }

        private void Update()
        {
            // 자가 치유(Self-Healing): 타겟이 비어 있다면 1초 간격으로 계속 재검색하여 실행 순서 꼬임 현상을 복구합니다.
            if (activeTargets.Count == 0)
            {
                intervalTimer += Time.deltaTime;
                if (intervalTimer >= 1.0f)
                {
                    InitializeTargets();
                    intervalTimer = 0f;
                }
                return;
            }

            // 외부에서 강제로 시선 고정 중인 경우 자동 스위칭 타이머 정지
            if (isGazeOverridden) return;

            intervalTimer += Time.deltaTime;
            if (intervalTimer >= nextSwitchTime)
            {
                SwitchToNextTarget();
                ResetTimer();
            }
        }

        /// <summary>
        /// 씬 내의 타겟 목록을 수집하고 초기화합니다.
        /// </summary>
        public void InitializeTargets()
        {
            activeTargets.Clear();

            if (autoFindTargets)
            {
                var targets = FindObjectsOfType<GazeTarget>();
                foreach (var t in targets)
                {
                    if (t != null && t.gameObject.activeInHierarchy)
                    {
                        activeTargets.Add(t);
                    }
                }
            }
            else
            {
                foreach (var t in customTargets)
                {
                    if (t != null)
                    {
                        activeTargets.Add(t);
                    }
                }
            }
        }

        /// <summary>
        /// 외부 연출 시퀀스에서 특정 타입(예: 얼굴)을 강제로 바라보게 고정시킬 때 사용합니다.
        /// </summary>
        public void ForceGazeToType(GazeTargetType type)
        {
            isGazeOverridden = true;
            
            // 만약 타겟 리스트가 비어있다면 즉시 동적 재검색 수행
            if (activeTargets.Count == 0)
            {
                InitializeTargets();
            }

            foreach (var target in activeTargets)
            {
                if (target.TargetType == type)
                {
                    currentTarget = target;
                    gazeTracker.SetTarget(target);
                    return;
                }
            }
        }

        /// <summary>
        /// 강제 시선 고정을 해제하고, 다시 원래의 자동 시선 교체 정책(Policy)으로 복귀합니다.
        /// </summary>
        public void ResumeAutomaticGaze()
        {
            isGazeOverridden = false;
            ResetTimer();
            SwitchToNextTarget();
        }

        /// <summary>
        /// 시선 추적을 완전히 일시 중단하고 타겟을 클리어하여 원래 정면(애니메이션)을 보게 만듭니다.
        /// </summary>
        public void DisableGaze()
        {
            isGazeOverridden = true;
            currentTarget = null;
            if (gazeTracker != null)
            {
                gazeTracker.ClearTarget();
            }
        }

        private void SwitchToNextTarget()
        {
            if (selectionPolicy == null || gazeTracker == null) return;

            IGazeTarget nextTarget = selectionPolicy.DetermineNextTarget(activeTargets);
            if (nextTarget != currentTarget)
            {
                currentTarget = nextTarget;
                gazeTracker.SetTarget(currentTarget);
            }
        }

        private void ResetTimer()
        {
            intervalTimer = 0f;
            if (selectionPolicy != null)
            {
                nextSwitchTime = selectionPolicy.GetNextInterval();
            }
            else
            {
                nextSwitchTime = Random.Range(2f, 4f);
            }
        }

        public void RegisterTarget(IGazeTarget target)
        {
            if (target != null && !activeTargets.Contains(target))
            {
                activeTargets.Add(target);
            }
        }

        public void UnregisterTarget(IGazeTarget target)
        {
            if (target != null && activeTargets.Contains(target))
            {
                activeTargets.Remove(target);
                if (currentTarget == target)
                {
                    currentTarget = null;
                    gazeTracker.ClearTarget();
                }
            }
        }
    }
}
