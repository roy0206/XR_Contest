using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 플레이어의 부위(얼굴, 손 등) 또는 기타 응시 가능한 대상에 부착하는 컴포넌트입니다.
    /// </summary>
    [AddComponentMenu("Gaze System/Gaze Target")]
    public class GazeTarget : MonoBehaviour, IGazeTarget
    {
        [SerializeField]
        [Tooltip("타겟의 타입(얼굴, 왼손, 오른손 등)을 설정합니다.")]
        private GazeTargetType targetType = GazeTargetType.Other;

        [SerializeField]
        [Tooltip("실제 바라볼 피벗(Pivot) 위치입니다. 미지정 시 이 오브젝트의 Transform을 사용합니다.")]
        private Transform targetTransform;

        public Transform TargetTransform
        {
            get
            {
                // 지정되지 않은 경우 컴포넌트가 붙은 객체 자체의 transform을 반환
                return targetTransform != null ? targetTransform : transform;
            }
        }

        public GazeTargetType TargetType => targetType;

        // 시작 시 자동으로 자신을 등록하거나, 관리자가 찾기 쉽게 태그를 지정할 수 있음
        private void Awake()
        {
            if (targetTransform == null)
            {
                targetTransform = transform;
            }
        }
    }
}
