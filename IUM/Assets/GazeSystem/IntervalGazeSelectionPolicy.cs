using System.Collections.Generic;
using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 가중치 기반으로 얼굴과 양손을 일정 시간마다 번갈아 바라보도록 타겟을 선택하는 정책 컴포넌트입니다.
    /// </summary>
    [AddComponentMenu("Gaze System/Policies/Interval Gaze Selection Policy")]
    public class IntervalGazeSelectionPolicy : MonoBehaviour, IGazeSelectionPolicy
    {
        [Header("Interval Settings")]
        [SerializeField]
        [Tooltip("최소 시선 유지 시간(초)입니다.")]
        private float minInterval = 1.5f;

        [SerializeField]
        [Tooltip("최대 시선 유지 시간(초)입니다.")]
        private float maxInterval = 3.5f;

        [Header("Target Weights (Probability)")]
        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("얼굴(Face)을 바라볼 확률 가중치입니다.")]
        private float faceWeight = 0.6f;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("왼손(Left Hand)을 바라볼 확률 가중치입니다.")]
        private float leftHandWeight = 0.2f;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("오른손(Right Hand)을 바라볼 확률 가중치입니다.")]
        private float rightHandWeight = 0.2f;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("그 외 기타 타겟(Other)을 바라볼 확률 가중치입니다.")]
        private float otherWeight = 0.1f;

        /// <summary>
        /// 사용 가능한 타겟 리스트에서 가중치 룰렛 방식을 사용하여 다음 타겟을 선택합니다.
        /// </summary>
        public IGazeTarget DetermineNextTarget(List<IGazeTarget> availableTargets)
        {
            if (availableTargets == null || availableTargets.Count == 0)
            {
                return null;
            }

            // 가중치 합산 계산
            float totalWeight = 0f;
            List<float> weights = new List<float>();

            foreach (var target in availableTargets)
            {
                float weight = GetWeightForType(target.TargetType);
                weights.Add(weight);
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                // 모든 가중치가 0인 경우 균등한 확률로 무작위 선택
                return availableTargets[Random.Range(0, availableTargets.Count)];
            }

            // 룰렛 선택 실행
            float randomValue = Random.Range(0f, totalWeight);
            float currentSum = 0f;

            for (int i = 0; i < availableTargets.Count; i++)
            {
                currentSum += weights[i];
                if (randomValue <= currentSum)
                {
                    return availableTargets[i];
                }
            }

            return availableTargets[availableTargets.Count - 1];
        }

        /// <summary>
        /// 다음 타겟 변경 시까지의 쿨타임(대기 시간)을 무작위로 반환합니다.
        /// </summary>
        public float GetNextInterval()
        {
            return Random.Range(minInterval, maxInterval);
        }

        private float GetWeightForType(GazeTargetType type)
        {
            switch (type)
            {
                case GazeTargetType.Face:
                    return faceWeight;
                case GazeTargetType.LeftHand:
                    return leftHandWeight;
                case GazeTargetType.RightHand:
                    return rightHandWeight;
                case GazeTargetType.Other:
                default:
                    return otherWeight;
            }
        }
    }
}
