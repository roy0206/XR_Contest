using UnityEngine;

public class InkLineZone : WorkZone
{
    protected override void Start()
    {
        base.Start();
        requiredToolType = ToolType.InkLine;
    }

    [Header("채점용 타겟 포인트")]
    public Transform targetStart;
    public Transform targetEnd;

    public void OnInkLineSnapped(Vector3 startPoint, Vector3 endPoint, float tension = 1f)
    {
        if (woodModifier != null)
        {
            woodModifier.CreateInkLine(startPoint, endPoint);

            float distanceError = 0f;
            if (targetStart != null && targetEnd != null)
            {
                // 방향 무관하게 매칭 (Start-Start, End-End 또는 Start-End, End-Start)
                float dist1 = Vector3.Distance(startPoint, targetStart.position) + Vector3.Distance(endPoint, targetEnd.position);
                float dist2 = Vector3.Distance(startPoint, targetEnd.position) + Vector3.Distance(endPoint, targetStart.position);
                distanceError = Mathf.Min(dist1, dist2);
            }
            
            // 10cm(0.1m) 이내 오차일 때 부분 점수 (1.0 ~ 0.0)
            float distanceScore = Mathf.Clamp01(1.0f - (distanceError / 0.1f)); 
            
            // 거리 점수(70%) + 장력 점수(30%)
            float qualityScore = (distanceScore * 0.7f) + (tension * 0.3f);

            CompleteWork(new WorkResult { qualityScore = qualityScore, toolName = requiredToolType.ToString() });
        }
    }

    public Vector3 GetSurfacePoint(Vector3 toolPos)
    {
        if (woodModifier != null) 
        {
            return woodModifier.GetClosestSurfacePoint(toolPos);
        }
        return toolPos;
    }
}
