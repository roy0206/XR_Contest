using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 카메라 또는 플레이어 오브젝트를 사람 머리 높이(눈높이)로 고정하고, 
    /// 상하 기울어짐 없이 바닥과 수평하게 정면(또는 특정 타겟)을 바라보도록 정렬해주는 헬퍼 스크립트입니다.
    /// </summary>
    [AddComponentMenu("Gaze System/Helpers/Camera Horizontal Aligner")]
    public class CameraHorizontalAligner : MonoBehaviour
    {
        [Header("Height Settings")]
        [SerializeField]
        [Tooltip("사람 머리 평균 높이(눈높이)로 Y축 위치를 고정합니다.")]
        private float eyeHeight = 1.65f;

        [SerializeField]
        [Tooltip("매 프레임 높이를 강제로 고정할지 여부입니다.")]
        private bool keepHeightFixed = true;

        [Header("Target Look Settings")]
        [SerializeField]
        [Tooltip("바라볼 타겟(NPC)입니다. 지정 시 해당 타겟 방향을 바라봅니다.")]
        private Transform lookTarget;

        [SerializeField]
        [Tooltip("타겟을 바라볼 때 목이 꺾이지 않도록 상하 회전(Pitch)을 강제로 0(수평)으로 만듭니다.")]
        private bool forceHorizontalLook = true;

        private void Start()
        {
            ApplyAlignment();
        }

        private void LateUpdate()
        {
            if (keepHeightFixed)
            {
                // 실시간으로 높이를 사람 눈높이로 고정
                Vector3 pos = transform.position;
                pos.y = eyeHeight;
                transform.position = pos;
            }

            if (lookTarget != null)
            {
                ApplyLookAtTarget();
            }
        }

        /// <summary>
        /// 초기 정렬을 즉시 적용합니다.
        /// </summary>
        public void ApplyAlignment()
        {
            // Y축 높이 고정
            Vector3 pos = transform.position;
            pos.y = eyeHeight;
            transform.position = pos;

            if (lookTarget != null)
            {
                ApplyLookAtTarget();
            }
            else
            {
                // 타겟이 없으면 정면을 바라보되, 수평(Pitch/Roll = 0)으로 정렬
                Vector3 currentEuler = transform.eulerAngles;
                transform.rotation = Quaternion.Euler(0f, currentEuler.y, 0f);
            }
        }

        /// <summary>
        /// 지정된 타겟을 상하 회전 없이 수평으로만 바라보도록 회전시킵니다.
        /// </summary>
        private void ApplyLookAtTarget()
        {
            Vector3 targetPosition = lookTarget.position;

            if (forceHorizontalLook)
            {
                // 타겟의 Y값을 카메라와 동일하게 맞춰 수평 상에서만 바라보도록 벡터를 보정
                targetPosition.y = transform.position.y;
            }

            Vector3 direction = targetPosition - transform.position;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                
                if (forceHorizontalLook)
                {
                    // X(상하), Z(기울기) 회전은 0으로 고정하고 Y(좌우) 회전만 적용
                    transform.rotation = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f);
                }
                else
                {
                    transform.rotation = targetRotation;
                }
            }
        }
    }
}
