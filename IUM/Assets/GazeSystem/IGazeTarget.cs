using UnityEngine;

namespace GazeSystem
{
    public enum GazeTargetType
    {
        Face,
        LeftHand,
        RightHand,
        Other
    }

    /// <summary>
    /// 시선 추적 시스템에서 타겟으로 지정될 객체가 구현해야 하는 인터페이스입니다.
    /// </summary>
    public interface IGazeTarget
    {
        /// <summary>
        /// 실제 시선이 향할 위치를 나타내는 Transform입니다.
        /// </summary>
        Transform TargetTransform { get; }

        /// <summary>
        /// 타겟의 부위 타입(얼굴, 손 등)을 나타냅니다.
        /// </summary>
        GazeTargetType TargetType { get; }
    }
}
