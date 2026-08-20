using System.Collections.Generic;
using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 얼굴 -> 손 -> 얼굴 순서로 시선이 자연스럽게 이동하도록 조율하는 순차적 시선 정책 컴포넌트입니다.
    /// 실제 대화 시 상대방의 얼굴을 보다가 잠시 손동작을 내려다본 뒤, 다시 눈을 맞추는 상호작용을 구현합니다.
    /// </summary>
    [AddComponentMenu("Gaze System/Policies/Sequence Gaze Selection Policy")]
    public class SequenceGazeSelectionPolicy : MonoBehaviour, IGazeSelectionPolicy
    {
        [Header("Stay Duration (Seconds)")]
        [SerializeField]
        [Tooltip("얼굴(Face)을 바라보고 유지하는 시간입니다.")]
        private float faceStayDuration = 4.0f;

        [SerializeField]
        [Tooltip("손(Hand)을 바라보고 유지하는 시간입니다. 얼굴보다 조금 짧은 것이 자연스럽습니다.")]
        private float handStayDuration = 1.8f;

        private enum SequenceState
        {
            LookingAtFace,
            LookingAtHand
        }

        private SequenceState currentState = SequenceState.LookingAtFace;
        private float nextInterval = 4.0f;

        public IGazeTarget DetermineNextTarget(List<IGazeTarget> availableTargets)
        {
            if (availableTargets == null || availableTargets.Count == 0)
            {
                return null;
            }

            IGazeTarget targetFace = null;
            List<IGazeTarget> targetHands = new List<IGazeTarget>();

            // 사용 가능한 타겟 분류 (얼굴 vs 손)
            foreach (var target in availableTargets)
            {
                if (target.TargetType == GazeTargetType.Face)
                {
                    targetFace = target;
                }
                else if (target.TargetType == GazeTargetType.LeftHand || target.TargetType == GazeTargetType.RightHand)
                {
                    targetHands.Add(target);
                }
            }

            // 시퀀스 로직 상태 전환
            if (currentState == SequenceState.LookingAtFace)
            {
                // 1. 얼굴을 보고 있었으므로 다음은 '손'으로 시선을 내림
                currentState = SequenceState.LookingAtHand;
                nextInterval = handStayDuration;

                // 손 타겟이 존재하는 경우 하나를 랜덤하게 골라 응시
                if (targetHands.Count > 0)
                {
                    return targetHands[Random.Range(0, targetHands.Count)];
                }
                else
                {
                    // 손이 없으면 얼굴 유지
                    currentState = SequenceState.LookingAtFace;
                    nextInterval = faceStayDuration;
                    return targetFace;
                }
            }
            else
            {
                // 2. 손을 보고 있었으므로 다음은 다시 '얼굴'로 복귀
                currentState = SequenceState.LookingAtFace;
                nextInterval = faceStayDuration;

                return targetFace != null ? targetFace : (availableTargets.Count > 0 ? availableTargets[0] : null);
            }
        }

        public float GetNextInterval()
        {
            // 약간의 랜덤 오차(±10%)를 줘서 기계적인 느낌을 탈피
            return nextInterval * Random.Range(0.9f, 1.1f);
        }
    }
}
