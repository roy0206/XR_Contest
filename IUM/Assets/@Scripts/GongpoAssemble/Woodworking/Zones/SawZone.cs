using UnityEngine;

public class SawZone : WorkZone
{
    [Header("톱질 설정")]
    [Tooltip("톱질 1회(스트로크)로 인정되기 위한 최소 이동 거리 (미터)")]
    public float minStrokeDistance = 0.2f;
    
    [Tooltip("작업을 완료하기 위해 필요한 총 톱질 횟수")]
    public int requiredStrokes = 5;
    
    [Tooltip("톱질이 허용되는 정방향 (로컬 축 기준)")]
    public Vector3 sawDirection = Vector3.forward;

    private float totalQuality = 0f;
    private int strokeCount = 0;

    protected override void Start()
    {
        base.Start();
        requiredToolType = ToolType.Saw;
    }

    public bool IsStrokeSuccessful(float distance)
    {
        return distance >= minStrokeDistance;
    }

    public void AddStrokeProgress(float speed, float angleTolerance)
    {
        // 톱질 품질 계산 (임시 로직: 속도가 빠를수록, 각도 오차가 적을수록 고득점)
        float speedScore = Mathf.Clamp01(speed / 1.5f); // 1.5m/s 이상이면 만점
        float angleScore = Mathf.Clamp01(1.0f - (angleTolerance / 30f)); // 30도 이상 벗어나면 0점
        
        float strokeQuality = (speedScore * 0.4f) + (angleScore * 0.6f);
        
        totalQuality += strokeQuality;
        strokeCount++;

        float progressAmount = 1.0f / requiredStrokes;
        AddProgress(progressAmount);
        Debug.Log($"[SawZone] 🪚 톱질 스트로크 성공! 현재 진행도: {progress * 100f:F1}% (품질: {strokeQuality:F2})");
    }

    protected override void CompleteWork()
    {
        if (woodModifier != null)
        {
            woodModifier.HideWaste();
        }

        float finalQuality = strokeCount > 0 ? (totalQuality / strokeCount) : 0f;
        
        // 부모의 오버로딩된 CompleteWork(WorkResult) 호출
        base.CompleteWork(new WorkResult { qualityScore = finalQuality, toolName = requiredToolType.ToString() });
    }
}
