using System.Collections.Generic;

namespace GazeSystem
{
    /// <summary>
    /// 여러 시선 타겟 목록 중 어떤 대상을 바라볼지 결정하는 알고리즘 정책 인터페이스입니다.
    /// </summary>
    public interface IGazeSelectionPolicy
    {
        /// <summary>
        /// 다음으로 응시할 타겟을 선택합니다.
        /// </summary>
        /// <param name="availableTargets">현재 유효한 전체 타겟 목록</param>
        /// <returns>선택된 다음 타겟 (없을 시 null)</returns>
        IGazeTarget DetermineNextTarget(List<IGazeTarget> availableTargets);

        /// <summary>
        /// 다음 타겟으로 전환하기까지 유지될 시간(초)을 결정합니다.
        /// </summary>
        float GetNextInterval();
    }
}
