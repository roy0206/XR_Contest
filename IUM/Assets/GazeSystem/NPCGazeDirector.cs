using System.Collections;
using UnityEngine;

namespace GazeSystem
{
    /// <summary>
    /// 외부 시스템(퀘스트, 대화 엔진, 시나리오 스크립트 등)에서 NPC의 시선과 리액션을 
    /// 애니메이션 컨트롤러처럼 원격 통제할 수 있도록 단일 통로를 열어주는 최상위 라이브러리 API 컴포넌트입니다.
    /// 고개 흔들기 리액션의 반복 횟수(Repeat Count) 및 무한 루프 모드, 그리고 즉각 중단을 완벽히 지원합니다.
    /// </summary>
    [RequireComponent(typeof(GazeTracker))]
    [RequireComponent(typeof(NPCGazeController))]
    [AddComponentMenu("Gaze System/NPC/NPC Gaze Director")]
    public class NPCGazeDirector : MonoBehaviour
    {
        private GazeTracker gazeTracker;
        private NPCGazeController gazeController;
        private Coroutine activeSequenceCoroutine;

        private void Awake()
        {
            gazeTracker = GetComponent<GazeTracker>();
            gazeController = GetComponent<NPCGazeController>();
        }

        #region Public Gaze Control API

        /// <summary>
        /// 플레이어의 특정 부위(얼굴, 양손 등)를 강제로 응시하게 고정합니다.
        /// </summary>
        public void ForceGazeTo(GazeTargetType type)
        {
            if (gazeController == null) return;
            gazeController.ForceGazeToType(type);
        }

        /// <summary>
        /// 강제 시선 고정을 해제하고, 다시 원래의 자동 시선 교체 정책(Policy)으로 복귀시킵니다.
        /// </summary>
        public void ResumeAutomaticGaze()
        {
            if (gazeController == null) return;
            gazeController.ResumeAutomaticGaze();
        }

        /// <summary>
        /// 일시적으로 시선 추적 대상을 완전히 비우고 정면을 바라보게 만듭니다.
        /// </summary>
        public void ClearGazeTarget()
        {
            if (gazeTracker == null) return;
            gazeTracker.ClearTarget();
        }

        #endregion

        #region Public Reaction Motion API (Trigger & Loop)

        /// <summary>
        /// 긍정의 의미로 고개를 즉시 끄덕끄덕(Yes) 흔들도록 트리거합니다.
        /// </summary>
        /// <param name="repeatCount">끄덕일 횟수입니다. (기본값: 1회, 무한 반복을 원할 시 -1 지정)</param>
        /// <param name="speed">흔들림 속도</param>
        /// <param name="intensity">상하 흔들림 강도(각도)</param>
        public void TriggerNod(int repeatCount = 1, float speed = 10f, float intensity = 13f)
        {
            if (gazeTracker == null) return;
            gazeTracker.TriggerNod(repeatCount, speed, intensity);
        }

        /// <summary>
        /// 고개 끄덕끄덕 모션을 즉시 강제 중단시킵니다.
        /// </summary>
        public void StopNod()
        {
            if (gazeTracker == null) return;
            gazeTracker.StopNod();
        }

        /// <summary>
        /// 부정의 의미로 고개를 즉시 절레절레(No) 흔들도록 트리거합니다.
        /// </summary>
        /// <param name="repeatCount">흔들 횟수입니다. (기본값: 1회, 무한 반복을 원할 시 -1 지정)</param>
        /// <param name="speed">흔들림 속도</param>
        /// <param name="intensity">좌우 흔들림 강도(각도)</param>
        public void TriggerShake(int repeatCount = 1, float speed = 10f, float intensity = 11f)
        {
            if (gazeTracker == null) return;
            gazeTracker.TriggerShake(repeatCount, speed, intensity);
        }

        /// <summary>
        /// 고개 절레절레 모션을 즉시 강제 중단시킵니다.
        /// </summary>
        public void StopShake()
        {
            if (gazeTracker == null) return;
            gazeTracker.StopShake();
        }

        /// <summary>
        /// 진행 중인 끄덕끄덕 및 흔들기 모션을 전부 정지시킵니다.
        /// </summary>
        public void StopAllMotions()
        {
            if (gazeTracker == null) return;
            gazeTracker.StopAllMotions();
        }

        #endregion

        #region Public Scenario Sequence API

        /// <summary>
        /// 사용자가 원하는 3단계 대화 인터랙션 시퀀스(번갈아보기 -> 절레절레 -> 끄덕끄덕)를 
        /// 원하는 시간 및 횟수 설정으로 원격 1회 구동시킵니다.
        /// </summary>
        /// <param name="gazeTime">얼굴/손 번갈아보는 자동 시선 유지 시간(초)</param>
        /// <param name="shakeCount">얼굴 고정 후 절레절레 흔들 횟수 (기본 1회)</param>
        /// <param name="nodCount">얼굴 고정 후 끄덕끄덕 흔들 횟수 (기본 1회)</param>
        public void PlayDialogueSequence(float gazeTime = 5.0f, int shakeCount = 1, int nodCount = 1)
        {
            StopActiveSequence();
            activeSequenceCoroutine = StartCoroutine(CoRunDialogueSequence(gazeTime, shakeCount, nodCount));
        }

        /// <summary>
        /// 현재 실행 중인 시퀀스 흐름이 있다면 강제로 중단하고 일반 자동 시선 상태로 복구시킵니다.
        /// </summary>
        public void StopActiveSequence()
        {
            if (activeSequenceCoroutine != null)
            {
                StopCoroutine(activeSequenceCoroutine);
                activeSequenceCoroutine = null;
            }
            ResumeAutomaticGaze();
        }

        private IEnumerator CoRunDialogueSequence(float gazeTime, int shakeCount, int nodCount)
        {
            // 1단계: 번갈아보기
            gazeController.ResumeAutomaticGaze();
            yield return new WaitForSeconds(gazeTime);

            // 2단계: 얼굴 보며 절레절레 (지정된 횟수만큼 흔듬)
            gazeController.ForceGazeToType(GazeTargetType.Face);
            gazeTracker.TriggerShake(shakeCount, 10f, 12f);
            
            // 모션 완전 주기가 도는 시간을 대기 계산
            float oneShakeCycle = (2f * Mathf.PI) / 10f; 
            yield return new WaitForSeconds(oneShakeCycle * shakeCount + 0.1f);

            // 3단계: 얼굴 보며 끄덕끄덕 (지정된 횟수만큼 흔듬)
            gazeController.ForceGazeToType(GazeTargetType.Face);
            gazeTracker.TriggerNod(nodCount, 10f, 13f);
            
            float oneNodCycle = (2f * Mathf.PI) / 10f;
            yield return new WaitForSeconds(oneNodCycle * nodCount + 0.1f);

            // 시퀀스가 종료되면 자동 복귀하며 마무리
            gazeController.ResumeAutomaticGaze();
            activeSequenceCoroutine = null;
        }

        #endregion
    }
}
